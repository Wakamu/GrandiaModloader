#include "hd_match.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>

#if defined(_M_IX86)
extern "C" {
void* g_mod_hd_match_v4_tramp = nullptr;
void* g_mod_hd_match_v2_tramp = nullptr;
void ModHdMatchV4Detour();
void ModHdMatchV2Detour();
}
#endif

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kV4StoreRva = 0x29164u;
constexpr std::size_t kV4StorePatch = 7u;
constexpr std::uint8_t kV4StoreBytes[] = {0x0F, 0x10, 0x85, 0x2C, 0xFF, 0xFF, 0xFF};

constexpr std::uintptr_t kV2StoreRva = 0x29301u;
constexpr std::size_t kV2StorePatch = 6u;
constexpr std::uint8_t kV2StoreBytes[] = {0x8B, 0x85, 0x50, 0xFF, 0xFF, 0xFF};

void* g_v4_site = nullptr;
void* g_v2_site = nullptr;
void* g_v4_tramp_mem = nullptr;
void* g_v2_tramp_mem = nullptr;
std::uint8_t g_v4_original[8]{};
std::uint8_t g_v2_original[8]{};

}  // namespace

void FireHdMatch(void* ebp, void* vector, int rec_len) {
    if (!ebp || rec_len <= 0 || rec_len > 32) {
        return;
    }
    if (!ClrHostReady() || RuntimeHasHdSpriteMatchHooks() != 1) {
        return;
    }

    const auto frame = reinterpret_cast<std::uintptr_t>(ebp);
    std::uint32_t index = 0;
    if (!SafeReadU32(frame - 0xA4, &index)) {
        return;
    }

    HdSpriteMatchNative req{};
    req.index = static_cast<std::int32_t>(index);
    req.rec_len = rec_len;
    LastHdSpriteInfoPath(req.path, sizeof(req.path));

    const auto catalog = reinterpret_cast<std::uintptr_t>(vector) -
                         (rec_len == 8 ? 0xD8u : 0x120u);
    req.object = static_cast<std::uint32_t>(catalog);
    BindHdObjectPath(catalog);
    std::uint32_t tex = 0;
    if (catalog >= 0x10000u && SafeReadU32(catalog + 0xD0, &tex) && tex >= 0x10000u) {
        BindHdObjectPath(tex);
    }

    const std::uintptr_t rec_at = rec_len == 8 ? frame - 0xB0 : frame - 0xD4;
    for (int i = 0; i < rec_len; ++i) {
        std::uint8_t b = 0;
        if (!SafeReadByte(rec_at + static_cast<std::uint32_t>(i), &b)) {
            return;
        }
        req.record[i] = b;
    }

    RuntimeOnHdSpriteMatch(&req);
}

}  // namespace grandia_mod

#if defined(_M_IX86)
extern "C" void ModOnHdMatchV4(void* ebp, void* vector) {
    grandia_mod::FireHdMatch(ebp, vector, 32);
}

extern "C" void ModOnHdMatchV2(void* ebp, void* vector) {
    grandia_mod::FireHdMatch(ebp, vector, 8);
}

extern "C" __declspec(naked) void ModHdMatchV4Detour() {
    __asm {
        pushad
        push ecx
        push ebp
        call ModOnHdMatchV4
        add esp, 8
        popad
        jmp dword ptr [g_mod_hd_match_v4_tramp]
    }
}

extern "C" __declspec(naked) void ModHdMatchV2Detour() {
    __asm {
        pushad
        push esi
        push ebp
        call ModOnHdMatchV2
        add esp, 8
        popad
        jmp dword ptr [g_mod_hd_match_v2_tramp]
    }
}
#endif

namespace grandia_mod {

bool InstallHdMatchHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }

    auto* v4 = reinterpret_cast<std::uint8_t*>(base + kV4StoreRva);
    auto* v2 = reinterpret_cast<std::uint8_t*>(base + kV2StoreRva);
    if (!IsExecutableAddress(v4) || !BytesMatch(v4, kV4StoreBytes, kV4StorePatch)) {
        LogWarn("OnHdSpriteMatch +0x29164 site mismatch");
        return false;
    }
    if (!IsExecutableAddress(v2) || !BytesMatch(v2, kV2StoreBytes, kV2StorePatch)) {
        LogWarn("OnHdSpriteMatch +0x29301 site mismatch");
        return false;
    }

    g_v4_tramp_mem = MakeTrampoline(v4, kV4StorePatch, v4 + kV4StorePatch);
    g_v2_tramp_mem = MakeTrampoline(v2, kV2StorePatch, v2 + kV2StorePatch);
    if (!g_v4_tramp_mem || !g_v2_tramp_mem) {
        LogWarn("OnHdSpriteMatch trampoline alloc failed");
        if (g_v4_tramp_mem) {
            VirtualFree(g_v4_tramp_mem, 0, MEM_RELEASE);
            g_v4_tramp_mem = nullptr;
        }
        if (g_v2_tramp_mem) {
            VirtualFree(g_v2_tramp_mem, 0, MEM_RELEASE);
            g_v2_tramp_mem = nullptr;
        }
        return false;
    }

    g_mod_hd_match_v4_tramp = g_v4_tramp_mem;
    g_mod_hd_match_v2_tramp = g_v2_tramp_mem;
    if (!WriteJump(v4, reinterpret_cast<void*>(&ModHdMatchV4Detour), g_v4_original, kV4StorePatch) ||
        !WriteJump(v2, reinterpret_cast<void*>(&ModHdMatchV2Detour), g_v2_original, kV2StorePatch)) {
        RemoveHdMatchHook();
        LogWarn("OnHdSpriteMatch hook write failed");
        return false;
    }

    g_v4_site = v4;
    g_v2_site = v2;

    return true;
#endif
}

void RemoveHdMatchHook() {
#if defined(_M_IX86)
    if (g_v4_site) {
        RestoreBytes(g_v4_site, g_v4_original, kV4StorePatch);
        g_v4_site = nullptr;
    }
    if (g_v2_site) {
        RestoreBytes(g_v2_site, g_v2_original, kV2StorePatch);
        g_v2_site = nullptr;
    }
    g_mod_hd_match_v4_tramp = nullptr;
    g_mod_hd_match_v2_tramp = nullptr;
    if (g_v4_tramp_mem) {
        VirtualFree(g_v4_tramp_mem, 0, MEM_RELEASE);
        g_v4_tramp_mem = nullptr;
    }
    if (g_v2_tramp_mem) {
        VirtualFree(g_v2_tramp_mem, 0, MEM_RELEASE);
        g_v2_tramp_mem = nullptr;
    }
#endif
}

}  // namespace grandia_mod
