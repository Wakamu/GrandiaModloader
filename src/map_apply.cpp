#include "map_apply.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kSec7CopyDoneRva = 0x548C0u;
constexpr std::size_t kSec7CopyDonePatch = 6u;
constexpr std::uintptr_t kSec7CopyDoneResumeRva = 0x548C6u;
constexpr std::uintptr_t kSec7RelocateRva = 0x60DC6u;
constexpr std::size_t kSec7RelocatePatch = 6u;
constexpr std::uintptr_t kSec7RelocateResumeRva = 0x60DCCu;
constexpr std::uintptr_t kScnBindRva = 0x55F2Cu;
constexpr std::size_t kScnBindPatch = 6u;
constexpr std::uintptr_t kScnBindResumeRva = 0x55F32u;

constexpr std::uintptr_t kHeapSec7PtrRva = 0x23FA6Cu;  // VA 0x63FA6C
constexpr std::uintptr_t kMdpSec7PtrRva = 0x31A714u;   // VA 0x71A714
constexpr std::uintptr_t kScnPtrRva = 0x31CD30u;       // VA 0x71CD30
constexpr std::uintptr_t kOfsPtrRva = 0x31CD4Cu;       // VA 0x71CD4C
constexpr std::uintptr_t kMallocIatRva = 0x1FE334u;    // VA 0x5FE334
constexpr std::uintptr_t kFreeIatRva = 0x1FE33Cu;      // VA 0x5FE33C
constexpr unsigned kSec7Budget = 0x4000u;

char g_stem[16]{};
void* g_sec7_copy_site = nullptr;
std::uint8_t g_sec7_copy_original[8]{};
void* g_sec7_relocate_site = nullptr;
std::uint8_t g_sec7_relocate_original[8]{};
void* g_scn_bind_site = nullptr;
std::uint8_t g_scn_bind_original[8]{};
void* g_grown_scn = nullptr;
void* g_grown_ofs = nullptr;
int g_pin_logs = 0;

void* ReadGlobal(std::uintptr_t rva) {
    void* p = nullptr;
    SafeReadPointer(ModuleBase() + rva, &p);
    return p;
}

bool WriteRam(void* dst, const void* src, std::size_t n, std::size_t pad_to = 0) {
    if (!dst || !src || n == 0) {
        return false;
    }
    const std::size_t total = pad_to > n ? pad_to : n;
    DWORD old = 0;
    if (!VirtualProtect(dst, total, PAGE_EXECUTE_READWRITE, &old)) {
        return false;
    }
    std::memcpy(dst, src, n);
    if (total > n) {
        std::memset(static_cast<std::uint8_t*>(dst) + n, 0, total - n);
    }
    VirtualProtect(dst, total, old, &old);
    return true;
}

void* GameMalloc(std::size_t n) {
    void* fn = nullptr;
    SafeReadPointer(ModuleBase() + kMallocIatRva, &fn);
    if (!fn) {
        return nullptr;
    }
    return reinterpret_cast<void*(__cdecl*)(std::size_t)>(fn)(n);
}

void GameFree(void* p) {
    if (!p) {
        return;
    }
    void* fn = nullptr;
    SafeReadPointer(ModuleBase() + kFreeIatRva, &fn);
    if (!fn) {
        return;
    }
    reinterpret_cast<void(__cdecl*)(void*)>(fn)(p);
}

void FreeGrown() {
    GameFree(g_grown_scn);
    g_grown_scn = nullptr;
    GameFree(g_grown_ofs);
    g_grown_ofs = nullptr;
}

void* GrowBuf(const void* src, std::size_t n, void** slot) {
    if (!src || n == 0 || !slot) {
        return nullptr;
    }
    void* mem = GameMalloc(n + 1);
    if (!mem) {
        return nullptr;
    }
    std::memcpy(mem, src, n);
    static_cast<std::uint8_t*>(mem)[n] = 0;
    *slot = mem;
    return mem;
}

void ApplySec7() {
    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0 || info.sec7 == 0 ||
        info.sec7_len <= 0) {
        return;
    }
    auto* heap = static_cast<std::uint8_t*>(ReadGlobal(kHeapSec7PtrRva));
    if (!heap) {
        LogWarn("map apply: sec[7] heap [0x63FA6C] is null");
        return;
    }
    const auto n = static_cast<unsigned>(info.sec7_len) < kSec7Budget
                       ? static_cast<unsigned>(info.sec7_len)
                       : kSec7Budget;
    if (!WriteRam(heap, reinterpret_cast<const void*>(info.sec7), n, kSec7Budget)) {
        LogWarn("map apply: sec[7] heap write failed");
        return;
    }
    LogInfo("map apply: sec[7] heap %u bytes stem=%s (file-relative, before relocate)", n, g_stem);
}

void ApplyScripts() {
    // SoftHD already overwrote [0x71CD30]/[0x71CD4C]; free last map's grown
    // CRT buffers now so we neither leak nor double-free the live pointers.
    FreeGrown();

    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0) {
        return;
    }
    if (info.scn == 0 && info.ofs == 0) {
        return;
    }
    const auto base = ModuleBase();
    auto* scn = static_cast<std::uint8_t*>(ReadGlobal(kScnPtrRva));
    auto* ofs = static_cast<std::uint8_t*>(ReadGlobal(kOfsPtrRva));
    const bool scn_fits =
        info.scn_len > 0 && info.stock_scn_len > 0 && info.scn_len <= info.stock_scn_len && scn;
    const bool ofs_fits =
        info.ofs_len > 0 && info.stock_ofs_len > 0 && info.ofs_len <= info.stock_ofs_len && ofs;

    if (info.scn_len > 0 && scn_fits) {
        const auto pad = info.stock_scn_len > info.scn_len
                             ? static_cast<std::size_t>(info.stock_scn_len)
                             : static_cast<std::size_t>(info.scn_len);
        WriteRam(scn, reinterpret_cast<const void*>(info.scn),
                 static_cast<std::size_t>(info.scn_len), pad);
    } else if (info.scn_len > 0) {
        auto* grown = static_cast<std::uint8_t*>(
            GrowBuf(reinterpret_cast<const void*>(info.scn), static_cast<std::size_t>(info.scn_len),
                    &g_grown_scn));
        if (grown) {
            SafeWriteU32(base + kScnPtrRva,
                         static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(grown)));
            LogInfo("map apply: SCN grew %d -> %d stem=%s", info.stock_scn_len, info.scn_len, g_stem);
        } else {
            LogWarn("map apply: SCN grow alloc failed");
        }
    }

    if (info.ofs_len > 0 && ofs_fits) {
        const auto pad = info.stock_ofs_len > info.ofs_len
                             ? static_cast<std::size_t>(info.stock_ofs_len)
                             : static_cast<std::size_t>(info.ofs_len);
        WriteRam(ofs, reinterpret_cast<const void*>(info.ofs),
                 static_cast<std::size_t>(info.ofs_len), pad);
    } else if (info.ofs_len > 0) {
        auto* grown = static_cast<std::uint8_t*>(
            GrowBuf(reinterpret_cast<const void*>(info.ofs), static_cast<std::size_t>(info.ofs_len),
                    &g_grown_ofs));
        if (grown) {
            SafeWriteU32(base + kOfsPtrRva,
                         static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(grown)));
            LogInfo("map apply: OFS grew %d -> %d stem=%s", info.stock_ofs_len, info.ofs_len, g_stem);
        } else {
            LogWarn("map apply: OFS grow alloc failed");
        }
    }

    if (scn_fits && ofs_fits) {
        LogInfo("map apply: SCN/OFS in-place scn=%d ofs=%d stem=%s", info.scn_len, info.ofs_len,
                g_stem);
    }
}

void LogPins(const char* tag) {
    if (g_pin_logs >= 8) {
        return;
    }
    ++g_pin_logs;
    MapPatchInfoNative info{};
    RuntimeMapPatchInfo(g_stem, &info);
    LogInfo("map bind %s stem=%s heap=%p mdp7=%p scn=%p ofs=%p dirty=%d sec7=%d scn=%d/%d ofs=%d/%d",
            tag, g_stem[0] ? g_stem : "-", ReadGlobal(kHeapSec7PtrRva),
            ReadGlobal(kMdpSec7PtrRva), ReadGlobal(kScnPtrRva), ReadGlobal(kOfsPtrRva), info.dirty,
            info.sec7_len, info.scn_len, info.stock_scn_len, info.ofs_len, info.stock_ofs_len);
}

}  // namespace

#if defined(_M_IX86)
extern "C" void* g_mod_sec7_copy_resume = nullptr;
extern "C" void* g_mod_sec7_relocate_resume = nullptr;
extern "C" void* g_mod_scn_bind_resume = nullptr;
extern "C" void* g_mod_scn_bank_abs = nullptr;

extern "C" void ModAfterSec7Copy() {
    LogPins("sec7-copy");
    ApplySec7();
}

extern "C" void ModAfterSec7Relocate() {
    LogPins("sec7-relocate");
}

extern "C" void ModAfterScnBind() {
    LogPins("scn-bind");
    ApplyScripts();
}

extern "C" __declspec(naked) void ModSec7CopyDetour() {
    __asm {
        pushad
        call ModAfterSec7Copy
        popad
        mov ecx, dword ptr [0x719700]
        jmp dword ptr [g_mod_sec7_copy_resume]
    }
}

extern "C" __declspec(naked) void ModSec7RelocateDetour() {
    __asm {
        pushad
        call ModAfterSec7Relocate
        popad
        mov ecx, dword ptr [0x63FA6C]
        jmp dword ptr [g_mod_sec7_relocate_resume]
    }
}

extern "C" __declspec(naked) void ModScnBindDetour() {
    __asm {
        mov eax, dword ptr [g_mod_scn_bank_abs]
        mov dword ptr [eax], esi
        pushad
        call ModAfterScnBind
        popad
        jmp dword ptr [g_mod_scn_bind_resume]
    }
}
#endif

const char* CurrentMapStem() {
    return g_stem[0] ? g_stem : "";
}

void NoteMapStem(const char* stem) {
    if (!stem || !*stem) {
        return;
    }
    std::memset(g_stem, 0, sizeof(g_stem));
    for (int i = 0; i < 15 && stem[i]; ++i) {
        char c = stem[i];
        if (c >= 'a' && c <= 'z') {
            c = static_cast<char>(c - 'a' + 'A');
        }
        g_stem[i] = c;
    }
}

bool InstallMapApplyHooks() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    g_mod_scn_bank_abs = reinterpret_cast<void*>(base + kScnPtrRva);

    auto install = [&](std::uintptr_t rva, std::size_t size, const std::uint8_t* expect,
                       void* detour, void** resume_slot, std::uintptr_t resume_rva,
                       std::uint8_t* original, void** site_out, const char* name) {
        auto* site = reinterpret_cast<std::uint8_t*>(base + rva);
        if (!IsExecutableAddress(site) || !BytesMatch(site, expect, size)) {
            LogWarn("map apply %s site mismatch at +0x%X", name, static_cast<unsigned>(rva));
            return false;
        }
        *resume_slot = reinterpret_cast<void*>(base + resume_rva);
        if (!WriteJump(site, detour, original, size)) {
            LogWarn("map apply %s hook failed", name);
            return false;
        }
        *site_out = site;
        LogInfo("map apply %s hook at +0x%X", name, static_cast<unsigned>(rva));
        return true;
    };

    const std::uint8_t sec7_copy_expect[] = {0x8B, 0x0D, 0x00, 0x97, 0x71, 0x00};
    const std::uint8_t sec7_rel_expect[] = {0x8B, 0x0D, 0x6C, 0xFA, 0x63, 0x00};
    const std::uint8_t scn_expect[] = {0x89, 0x35, 0x30, 0xCD, 0x71, 0x00};
    int ok = 0;
    ok += install(kSec7CopyDoneRva, kSec7CopyDonePatch, sec7_copy_expect,
                  reinterpret_cast<void*>(&ModSec7CopyDetour), &g_mod_sec7_copy_resume,
                  kSec7CopyDoneResumeRva, g_sec7_copy_original, &g_sec7_copy_site, "sec7-copy")
              ? 1
              : 0;
    ok += install(kSec7RelocateRva, kSec7RelocatePatch, sec7_rel_expect,
                  reinterpret_cast<void*>(&ModSec7RelocateDetour), &g_mod_sec7_relocate_resume,
                  kSec7RelocateResumeRva, g_sec7_relocate_original, &g_sec7_relocate_site,
                  "sec7-relocate")
              ? 1
              : 0;
    ok += install(kScnBindRva, kScnBindPatch, scn_expect, reinterpret_cast<void*>(&ModScnBindDetour),
                  &g_mod_scn_bind_resume, kScnBindResumeRva, g_scn_bind_original, &g_scn_bind_site,
                  "scn-bind")
              ? 1
              : 0;
    return ok > 0;
#endif
}

void RemoveMapApplyHooks() {
    if (g_sec7_copy_site) {
        RestoreBytes(g_sec7_copy_site, g_sec7_copy_original, kSec7CopyDonePatch);
        g_sec7_copy_site = nullptr;
    }
    if (g_sec7_relocate_site) {
        RestoreBytes(g_sec7_relocate_site, g_sec7_relocate_original, kSec7RelocatePatch);
        g_sec7_relocate_site = nullptr;
    }
    if (g_scn_bind_site) {
        RestoreBytes(g_scn_bind_site, g_scn_bind_original, kScnBindPatch);
        g_scn_bind_site = nullptr;
    }
    FreeGrown();
}

}  // namespace grandia_mod
