#include "overlay.h"

#include "clr_host.h"
#include "log.h"
#include "map_apply.h"
#include "party.h"
#include "virt_file.h"

#include <Windows.h>

#include <cctype>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

namespace grandia_mod {

namespace {

using FopenFn = FILE*(__cdecl*)(const char* path, const char* mode);

constexpr std::uintptr_t kFopenIatRva = 0x1FE40Cu;  // VA 0x5FE40C @ image base 0x400000
constexpr std::uintptr_t kSaveObjPtrRva = 0x23FA94u;  // VA 0x63FA94

FopenFn g_orig_fopen = nullptr;
void** g_fopen_iat_slot = nullptr;
FopenFn g_fopen_iat_original = nullptr;
std::string g_overlay_root;
HANDLE g_runtime_ready = nullptr;

void EnsureRuntimeGate() {
    if (!g_runtime_ready) {
        g_runtime_ready = CreateEventA(nullptr, TRUE, FALSE, nullptr);
    }
}

void WaitRuntimeReady() {
    EnsureRuntimeGate();
    if (g_runtime_ready) {
        WaitForSingleObject(g_runtime_ready, 60000);
    }
}

std::string Basename(const char* path) {
    if (!path || !*path) {
        return {};
    }
    const char* base = path;
    for (const char* p = path; *p; ++p) {
        if (*p == '\\' || *p == '/') {
            base = p + 1;
        }
    }
    return base;
}

std::string Dirname(const std::string& path) {
    const auto slash = path.find_last_of("\\/");
    if (slash == std::string::npos) {
        return ".";
    }
    return path.substr(0, slash);
}

std::string ModuleDirectory() {
    char buf[MAX_PATH]{};
    HMODULE self = nullptr;
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            reinterpret_cast<LPCSTR>(&ModuleDirectory), &self)) {
        return {};
    }
    if (!GetModuleFileNameA(self, buf, MAX_PATH)) {
        return {};
    }
    return Dirname(buf);
}

bool DirExists(const std::string& path) {
    const DWORD attrs = GetFileAttributesA(path.c_str());
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

bool LooksLikeOverlayRoot(const std::string& root) {
    return DirExists(root + "\\FIELD") || DirExists(root + "\\TEXT");
}

std::string TrimAscii(std::string s) {
    while (!s.empty() && (s.back() == '\r' || s.back() == '\n' || s.back() == ' ' || s.back() == '\t')) {
        s.pop_back();
    }
    size_t i = 0;
    while (i < s.size() && (s[i] == ' ' || s[i] == '\t')) {
        ++i;
    }
    return s.substr(i);
}

std::string ReadFirstLine(const std::string& path) {
    FILE* f = nullptr;
    if (fopen_s(&f, path.c_str(), "r") != 0 || !f) {
        return {};
    }
    char buf[MAX_PATH + 8]{};
    const char* got = fgets(buf, sizeof(buf), f);
    fclose(f);
    if (!got) {
        return {};
    }
    return TrimAscii(buf);
}

std::string ToUpperAscii(std::string s) {
    for (char& c : s) {
        if (c >= 'a' && c <= 'z') {
            c = static_cast<char>(c - 'a' + 'A');
        }
    }
    return s;
}

std::string ResolveOverlayRoot() {
    char env[MAX_PATH]{};
    if (GetEnvironmentVariableA("GRANDIA_FIELD_PATCH", env, MAX_PATH) > 0) {
        std::string root = env;
        if (LooksLikeOverlayRoot(root)) {
            return root;
        }
        if (LooksLikeOverlayRoot(root + "\\overlay")) {
            return root + "\\overlay";
        }
        if (LooksLikeOverlayRoot(root + "\\field_patch_overlay")) {
            return root + "\\field_patch_overlay";
        }
    }

    const std::string dll_dir = ModuleDirectory();
    if (dll_dir.empty()) {
        return {};
    }

    const std::string sidecar = ReadFirstLine(dll_dir + "\\overlay_root.txt");
    if (!sidecar.empty()) {
        CreateDirectoryA(sidecar.c_str(), nullptr);
        return sidecar;
    }

    const char* candidates[] = {
        "\\overlay",
        "\\field_patch_overlay",
        "\\..\\overlay",
        "\\..\\..\\overlay",
        "\\..\\..\\..\\overlay",
    };
    for (const char* rel : candidates) {
        std::string root = dll_dir + rel;
        if (LooksLikeOverlayRoot(root)) {
            return root;
        }
    }
    return {};
}

bool LooksLikeMapStem(const std::string& stem) {
    if (stem.size() < 3 || stem.size() > 8) {
        return false;
    }
    for (unsigned char c : stem) {
        if (!std::isxdigit(c)) {
            return false;
        }
    }
    return true;
}

std::string MapStemFromPath(const char* path) {
    const std::string base = Basename(path);
    const auto dot = base.find_last_of('.');
    std::string stem = dot == std::string::npos ? base : base.substr(0, dot);
    return ToUpperAscii(stem);
}

bool IsFieldMapPath(const char* path, std::string* stem_out) {
    if (!path) {
        return false;
    }
    const std::string base = Basename(path);
    const char* dot = base.empty() ? nullptr : strrchr(base.c_str(), '.');
    const bool is_mdp = dot && _stricmp(dot, ".MDP") == 0;
    const bool is_scn = dot && _stricmp(dot, ".SCN") == 0;
    const bool is_ofs = dot && _stricmp(dot, ".OFS") == 0;
    if (!is_mdp && !is_scn && !is_ofs) {
        return false;
    }
    const std::string stem = MapStemFromPath(path);
    if (!LooksLikeMapStem(stem)) {
        return false;
    }
    if (stem_out) {
        *stem_out = stem;
    }
    return true;
}

void ReadTravelContext(std::uint16_t* from, std::uint16_t* to, int* spawn) {
    *from = 0;
    *to = 0;
    *spawn = 0;
    __try {
        const auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
        if (base == 0) {
            return;
        }
        auto* slot = reinterpret_cast<std::uint8_t**>(base + kSaveObjPtrRva);
        if (!slot || !*slot) {
            return;
        }
        std::uint8_t* save = *slot;
        *from = *reinterpret_cast<std::uint16_t*>(save + 8);
        *to = *reinterpret_cast<std::uint16_t*>(save + 0x5B2);
        *spawn = *reinterpret_cast<std::uint16_t*>(save + 0x5B4);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *from = 0;
        *to = 0;
        *spawn = 0;
    }
}

std::uint16_t StemToMapId(const std::string& stem) {
    if (stem.empty()) {
        return 0;
    }
    char* end = nullptr;
    const unsigned long v = strtoul(stem.c_str(), &end, 16);
    if (!end || *end != 0 || v > 0xFFFF) {
        return 0;
    }
    return static_cast<std::uint16_t>(v);
}

FILE* __cdecl ModFopenHook(const char* path, const char* mode) {
    std::string stem;
    const bool field_map = path && IsFieldMapPath(path, &stem);
    TryRestoreFieldParty(field_map);
    if (field_map) {
        NoteMapStem(stem.c_str());
        WaitRuntimeReady();
        if (g_overlay_root.empty()) {
            g_overlay_root = ResolveOverlayRoot();
        }
        static char opened[48][16]{};
        static int opened_n = 0;
        bool seen = false;
        for (int i = 0; i < opened_n; ++i) {
            if (stem == opened[i]) {
                seen = true;
                break;
            }
        }
        if (!seen) {
            std::uint16_t from = 0;
            std::uint16_t to = 0;
            int spawn = 0;
            ReadTravelContext(&from, &to, &spawn);
            if (to == 0) {
                to = StemToMapId(stem);
            }
            RestoreShopPriceOverrides();
            const int dirty =
                RuntimeOnMapOpen(stem.c_str(), from, to, spawn, g_overlay_root.c_str());
            if (dirty < 0) {
                LogWarn("runtime OnMapOpen failed for %s", stem.c_str());
            } else {
                if (opened_n < 48 && stem.size() < 16) {
                    std::memset(opened[opened_n], 0, 16);
                    std::memcpy(opened[opened_n], stem.c_str(), stem.size());
                    ++opened_n;
                }
                if (dirty > 0) {
                    LogInfo("runtime map patch %s from=0x%04X to=0x%04X spawn=%d", stem.c_str(),
                            from, to, spawn);
                }
            }
        }
    }

    if (FILE* virt = VirtFileOpen(path, mode)) {
        return virt;
    }
    return g_orig_fopen(path, mode);
}

bool PatchIatSlot(void** slot, void* detour, void** original_out) {
    if (!slot || !detour) {
        return false;
    }
    DWORD old = 0;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_EXECUTE_READWRITE, &old)) {
        return false;
    }
    if (original_out) {
        *original_out = *slot;
    }
    *slot = detour;
    VirtualProtect(slot, sizeof(void*), old, &old);
    return true;
}

}  // namespace

void SignalRuntimeReady() {
    EnsureRuntimeGate();
    if (g_runtime_ready) {
        SetEvent(g_runtime_ready);
    }
}

bool InstallOverlayHooks() {
    EnsureRuntimeGate();
    if (g_fopen_iat_slot) {
        return true;
    }
    const auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    if (base == 0) {
        return false;
    }

    g_overlay_root = ResolveOverlayRoot();
    if (!g_overlay_root.empty()) {
        LogInfo("Map assemble cache %s (apply after bind, no fopen remap)", g_overlay_root.c_str());
    } else {
        LogInfo("Map assemble cache idle (overlay_root.txt, overlay/ next to DLL, or GRANDIA_FIELD_PATCH)");
    }
    if (!InstallMapApplyHooks()) {
        LogWarn("map apply bind hooks not installed");
    }

    auto* slot = reinterpret_cast<void**>(base + kFopenIatRva);
    if (!*slot) {
        LogWarn("fopen IAT slot empty");
        return false;
    }

    if (!PatchIatSlot(slot, reinterpret_cast<void*>(&ModFopenHook),
                      reinterpret_cast<void**>(&g_fopen_iat_original))) {
        LogWarn("failed to patch fopen IAT");
        return false;
    }
    g_fopen_iat_slot = slot;
    g_orig_fopen = g_fopen_iat_original;
    VirtFileSetOrigFopen(g_orig_fopen);
    if (!InstallVirtFileHooks()) {
        LogWarn("virt file IAT hooks not installed — embedded maps will not load");
    }
    LogInfo("fopen IAT patched");
    return true;
}

void RemoveOverlayHooks() {
    RemoveVirtFileHooks();
    RemoveMapApplyHooks();
    if (g_fopen_iat_slot && g_fopen_iat_original) {
        PatchIatSlot(g_fopen_iat_slot, reinterpret_cast<void*>(g_fopen_iat_original), nullptr);
    }
    g_fopen_iat_slot = nullptr;
    g_fopen_iat_original = nullptr;
    g_orig_fopen = nullptr;
    g_overlay_root.clear();
}

bool IsOverlayHookInstalled() {
    return g_fopen_iat_slot != nullptr;
}

}  // namespace grandia_mod
