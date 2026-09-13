#include "hook_util.h"

#include <cctype>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

namespace grandia_mod {

bool BytesMatch(const std::uint8_t* data, const std::uint8_t* expected, std::size_t size) {
    for (std::size_t i = 0; i < size; ++i) {
        if (data[i] != expected[i]) {
            return false;
        }
    }
    return true;
}

bool IsExecutableAddress(void* address) {
    MEMORY_BASIC_INFORMATION info{};
    if (VirtualQuery(address, &info, sizeof(info)) == 0) {
        return false;
    }
    const DWORD prot = info.Protect & 0xFF;
    return prot == PAGE_EXECUTE || prot == PAGE_EXECUTE_READ || prot == PAGE_EXECUTE_READWRITE ||
           prot == PAGE_EXECUTE_WRITECOPY;
}

bool WriteJump(void* site, void* destination, std::uint8_t* original_out, std::size_t patch_size) {
    if (!site || !destination || patch_size < 5) {
        return false;
    }
    DWORD old_protect = 0;
    if (!VirtualProtect(site, patch_size, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return false;
    }
    if (original_out) {
        std::memcpy(original_out, site, patch_size);
    }
    const auto rel = static_cast<std::int32_t>(reinterpret_cast<std::uint8_t*>(destination) -
                                               (reinterpret_cast<std::uint8_t*>(site) + 5));
    auto* bytes = reinterpret_cast<std::uint8_t*>(site);
    bytes[0] = 0xE9;
    std::memcpy(bytes + 1, &rel, sizeof(rel));
    for (std::size_t i = 5; i < patch_size; ++i) {
        bytes[i] = 0x90;
    }
    VirtualProtect(site, patch_size, old_protect, &old_protect);
    FlushInstructionCache(GetCurrentProcess(), site, patch_size);
    return true;
}

void RestoreBytes(void* site, const std::uint8_t* original, std::size_t size) {
    if (!site || !original || size == 0) {
        return;
    }
    DWORD old_protect = 0;
    if (!VirtualProtect(site, size, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return;
    }
    std::memcpy(site, original, size);
    VirtualProtect(site, size, old_protect, &old_protect);
    FlushInstructionCache(GetCurrentProcess(), site, size);
}

void* MakeTrampoline(const void* stolen, std::size_t stolen_size, void* continue_at) {
    if (!stolen || stolen_size == 0 || stolen_size > 16 || !continue_at) {
        return nullptr;
    }
    void* mem = VirtualAlloc(nullptr, 32, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!mem) {
        return nullptr;
    }
    auto* tramp = reinterpret_cast<std::uint8_t*>(mem);
    std::memcpy(tramp, stolen, stolen_size);
    tramp[stolen_size] = 0xE9;
    const auto rel = static_cast<std::int32_t>(reinterpret_cast<std::uint8_t*>(continue_at) -
                                               (tramp + stolen_size + 5));
    std::memcpy(tramp + stolen_size + 1, &rel, sizeof(rel));
    FlushInstructionCache(GetCurrentProcess(), mem, 32);
    return mem;
}

void* ReadIatFunction(void* call_site) {
    if (!call_site) {
        return nullptr;
    }
    const auto* bytes = reinterpret_cast<const std::uint8_t*>(call_site);
    if (bytes[0] != 0xFF || bytes[1] != 0x15) {
        return nullptr;
    }
    std::uintptr_t iat_va = 0;
    std::memcpy(&iat_va, bytes + 2, sizeof(iat_va));
    void* fn = nullptr;
    __try {
        fn = *reinterpret_cast<void**>(iat_va);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return nullptr;
    }
    return fn;
}

std::uintptr_t ModuleBase() {
    return reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
}

std::uintptr_t CallerRva(std::uintptr_t caller) {
    const std::uintptr_t base = ModuleBase();
    if (base == 0 || caller < base) {
        return 0;
    }
    return caller - base;
}

bool IsNearRva(std::uintptr_t rva, std::uintptr_t target, std::uintptr_t slack) {
    return rva + slack >= target && rva <= target + slack;
}

bool PatternMatch(const std::uint8_t* data, const int* pat, std::size_t n) {
    for (std::size_t i = 0; i < n; ++i) {
        if (pat[i] >= 0 && data[i] != static_cast<std::uint8_t>(pat[i])) {
            return false;
        }
    }
    return true;
}

std::uintptr_t ScanExecutable(HMODULE module, const int* pat, std::size_t n) {
    if (!module || !pat || n == 0) {
        return 0;
    }
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(module);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
        return 0;
    }
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(reinterpret_cast<std::uint8_t*>(module) + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) {
        return 0;
    }
    auto* base = reinterpret_cast<std::uint8_t*>(module);
    auto* section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
        if ((section[i].Characteristics & IMAGE_SCN_MEM_EXECUTE) == 0) {
            continue;
        }
        const auto* start = base + section[i].VirtualAddress;
        const std::size_t size = section[i].Misc.VirtualSize;
        if (size < n) {
            continue;
        }
        for (std::size_t offset = 0; offset + n <= size; ++offset) {
            if (PatternMatch(start + offset, pat, n)) {
                return reinterpret_cast<std::uintptr_t>(start + offset);
            }
        }
    }
    return 0;
}

bool SafeReadByte(std::uintptr_t address, std::uint8_t* out_byte) {
    if (!out_byte || address == 0) {
        return false;
    }
    __try {
        *out_byte = *reinterpret_cast<const std::uint8_t*>(address);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool SafeReadU32(std::uintptr_t address, std::uint32_t* out_value) {
    if (!out_value || address == 0) {
        return false;
    }
    __try {
        *out_value = *reinterpret_cast<const std::uint32_t*>(address);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool SafeReadPointer(std::uintptr_t address, void** out_pointer) {
    if (!out_pointer) {
        return false;
    }
    *out_pointer = nullptr;
    __try {
        *out_pointer = *reinterpret_cast<void**>(address);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool SafeWriteByte(std::uintptr_t address, std::uint8_t value) {
    if (address == 0) {
        return false;
    }
    DWORD old_protect = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(address), 1, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return false;
    }
    bool ok = false;
    __try {
        *reinterpret_cast<std::uint8_t*>(address) = value;
        ok = true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        ok = false;
    }
    VirtualProtect(reinterpret_cast<void*>(address), 1, old_protect, &old_protect);
    return ok;
}

void ReadMsvcString(std::uintptr_t str, char* dest, int dest_len) {
    if (!dest || dest_len <= 0) {
        return;
    }
    dest[0] = 0;
    if (str == 0) {
        return;
    }
    std::uint32_t cap = 0;
    std::uint32_t size = 0;
    if (!SafeReadU32(str + 0x14, &cap) || !SafeReadU32(str + 0x10, &size)) {
        return;
    }
    if (size == 0 || size > 259 || cap > 0x10000u || (cap < 0x10u && size > 15u) ||
        (cap >= 0x10u && size > cap)) {
        return;
    }
    std::uintptr_t p = str;
    if (cap >= 0x10) {
        void* heap = nullptr;
        if (!SafeReadPointer(str, &heap) || !heap) {
            return;
        }
        p = reinterpret_cast<std::uintptr_t>(heap);
    }
    if (size >= static_cast<std::uint32_t>(dest_len)) {
        size = static_cast<std::uint32_t>(dest_len - 1);
    }
    for (std::uint32_t i = 0; i < size; ++i) {
        std::uint8_t b = 0;
        if (!SafeReadByte(p + i, &b)) {
            dest[i] = 0;
            return;
        }
        dest[i] = static_cast<char>(b);
    }
    dest[size] = 0;
}

namespace {

bool LooksLikeHdPath(const char* path) {
    if (!path || !path[0]) {
        return false;
    }
    char lower[260]{};
    int n = 0;
    for (; path[n] && n < 259; ++n) {
        const unsigned char c = static_cast<unsigned char>(path[n]);
        if (c < 32 || c > 126) {
            return false;
        }
        lower[n] = static_cast<char>(std::tolower(c));
    }
    if (n < 5) {
        return false;
    }
    return std::strstr(lower, "__atlas") != nullptr || std::strstr(lower, "__spriteinfo") != nullptr;
}

void CopyPath(char* dest, int dest_len, const char* src) {
    if (!dest || dest_len <= 0) {
        return;
    }
    dest[0] = 0;
    if (!src) {
        return;
    }
    int i = 0;
    for (; src[i] && i < dest_len - 1; ++i) {
        dest[i] = src[i];
    }
    dest[i] = 0;
}

}  // namespace

bool ReadHdAssetPath(std::uintptr_t object, char* dest, int dest_len) {
    if (!dest || dest_len <= 0) {
        return false;
    }
    dest[0] = 0;
    if (object == 0) {
        return false;
    }

    constexpr std::uintptr_t kOffs[] = {0x38u, 0x18u, 0x00u, 0x30u, 0x48u};
    for (std::uintptr_t off : kOffs) {
        char tmp[260]{};
        ReadMsvcString(object + off, tmp, sizeof(tmp));
        if (LooksLikeHdPath(tmp)) {
            CopyPath(dest, dest_len, tmp);
            return true;
        }
    }
    return false;
}

namespace {

std::mutex g_hd_path_mu;
char g_last_spriteinfo[260]{};
std::unordered_map<std::uintptr_t, std::string> g_object_paths;

bool HasSpriteInfoMark(const char* path) {
    if (!path || !path[0]) {
        return false;
    }
    char lower[260]{};
    int n = 0;
    for (; path[n] && n < 259; ++n) {
        lower[n] = static_cast<char>(std::tolower(static_cast<unsigned char>(path[n])));
    }
    return std::strstr(lower, "__spriteinfo") != nullptr;
}

}  // namespace

void NoteHdAssetPath(const char* path) {
    if (!HasSpriteInfoMark(path)) {
        return;
    }
    std::lock_guard<std::mutex> lock(g_hd_path_mu);
    CopyPath(g_last_spriteinfo, sizeof(g_last_spriteinfo), path);
}

bool LastHdSpriteInfoPath(char* dest, int dest_len) {
    std::lock_guard<std::mutex> lock(g_hd_path_mu);
    if (!g_last_spriteinfo[0]) {
        if (dest && dest_len > 0) {
            dest[0] = 0;
        }
        return false;
    }
    CopyPath(dest, dest_len, g_last_spriteinfo);
    return true;
}

void BindHdObjectPath(std::uintptr_t object) {
    if (object == 0) {
        return;
    }
    std::lock_guard<std::mutex> lock(g_hd_path_mu);
    if (!g_last_spriteinfo[0]) {
        return;
    }
    if (g_object_paths.size() >= 512) {
        g_object_paths.clear();
    }
    g_object_paths[object] = g_last_spriteinfo;
}

bool LookupHdObjectPath(std::uintptr_t object, char* dest, int dest_len) {
    if (object == 0 || !dest || dest_len <= 0) {
        return false;
    }
    dest[0] = 0;
    std::lock_guard<std::mutex> lock(g_hd_path_mu);
    auto it = g_object_paths.find(object);
    if (it == g_object_paths.end() || it->second.empty()) {
        return false;
    }
    CopyPath(dest, dest_len, it->second.c_str());
    return true;
}

bool SafeWriteU32(std::uintptr_t address, std::uint32_t value) {
    if (address == 0) {
        return false;
    }
    DWORD old_protect = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(address), 4, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return false;
    }
    bool ok = false;
    __try {
        *reinterpret_cast<std::uint32_t*>(address) = value;
        ok = true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        ok = false;
    }
    VirtualProtect(reinterpret_cast<void*>(address), 4, old_protect, &old_protect);
    return ok;
}

}  // namespace grandia_mod
