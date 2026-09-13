#include "hd_draw.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>

#if defined(_M_IX86)
extern "C" {
void* g_mod_hd_sprite_draw_tramp = nullptr;
void ModHdSpriteDrawDetour();
}
#endif

namespace grandia_mod {
namespace {

// Common SoftHD epilogue after every kind. Hits write 1 to this flag,
// misses memset the result and write 0, then both land here.
// 0F 10 45 A8  8B 45 D0  — no on-disk immediates.
constexpr std::uintptr_t kDrawHitRva = 0x1B67Au;
constexpr std::uintptr_t kDrawHitFlagRva = 0x240E7Bu;
constexpr std::size_t kDrawHitPatch = 7u;
constexpr std::uint8_t kDrawHitBytes[] = {0x0F, 0x10, 0x45, 0xA8, 0x8B, 0x45, 0xD0};

void* g_draw_site = nullptr;
void* g_draw_tramp_mem = nullptr;
std::uint8_t g_draw_original[8]{};
int g_draw_hooks = -1;

bool RegionReadable(std::uintptr_t address, std::size_t bytes) {
    if (address < 0x10000u || bytes == 0) {
        return false;
    }
    MEMORY_BASIC_INFORMATION mbi{};
    if (VirtualQuery(reinterpret_cast<void*>(address), &mbi, sizeof(mbi)) == 0) {
        return false;
    }
    if (mbi.State != MEM_COMMIT) {
        return false;
    }
    const DWORD prot = mbi.Protect & 0xFFu;
    if (prot == PAGE_NOACCESS || prot == PAGE_EXECUTE || (mbi.Protect & PAGE_GUARD) != 0) {
        return false;
    }
    const auto start = reinterpret_cast<std::uintptr_t>(mbi.BaseAddress);
    return address >= start && address + bytes <= start + mbi.RegionSize;
}

bool ReadBytes(std::uintptr_t address, void* dest, std::size_t bytes) {
    if (!dest || !RegionReadable(address, bytes)) {
        return false;
    }
    std::memcpy(dest, reinterpret_cast<const void*>(address), bytes);
    return true;
}

}  // namespace

void FireHdDraw(void* ebp) {
    if (!ebp) {
        return;
    }
    if (!ClrHostReady()) {
        return;
    }
    if (g_draw_hooks < 0) {
        g_draw_hooks = RuntimeHasHdSpriteDrawHooks();
    }
    if (g_draw_hooks != 1) {
        return;
    }

    std::uint8_t hit = 0;
    if (!ReadBytes(ModuleBase() + kDrawHitFlagRva, &hit, 1) || hit == 0) {
        return;
    }

    const auto frame = reinterpret_cast<std::uintptr_t>(ebp);
    const std::uintptr_t result = frame - 0x58;
    HdSpriteDrawNative req{};
    if (!ReadBytes(result, req.record, sizeof(req.record))) {
        return;
    }
    req.rec_len = 32;

    std::uint32_t tex = 0;
    ReadBytes(result + 0x10, &tex, 4);
    if (tex >= 0x10000u) {
        req.object = tex;
        LookupHdObjectPath(tex, req.path, sizeof(req.path));
    }
    if (!req.path[0]) {
        std::uint32_t catalog = 0;
        ReadBytes(result, &catalog, 4);
        if (catalog >= 0x10000u) {
            if (req.object == 0) {
                req.object = catalog;
            }
            LookupHdObjectPath(catalog, req.path, sizeof(req.path));
        }
    }

    std::uint32_t sprite = 0;
    if (ReadBytes(frame + 0x24, &sprite, 4) && RegionReadable(sprite + 6, 8)) {
        std::uint16_t live[4]{};
        if (ReadBytes(sprite + 6, live, sizeof(live))) {
            req.live_x = live[0];
            req.live_y = live[1];
            req.live_w = live[2];
            req.live_h = live[3];
        }
    }

    RuntimeOnHdSpriteDraw(&req);
}

}  // namespace grandia_mod

#if defined(_M_IX86)
extern "C" void ModOnHdSpriteDraw(void* ebp) {
    grandia_mod::FireHdDraw(ebp);
}

extern "C" __declspec(naked) void ModHdSpriteDrawDetour() {
    __asm {
        pushad
        push ebp
        call ModOnHdSpriteDraw
        add esp, 4
        popad
        jmp dword ptr [g_mod_hd_sprite_draw_tramp]
    }
}
#endif

namespace grandia_mod {

bool InstallHdDrawHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }

    auto* site = reinterpret_cast<std::uint8_t*>(base + kDrawHitRva);
    if (!IsExecutableAddress(site) || !BytesMatch(site, kDrawHitBytes, kDrawHitPatch)) {
        LogWarn("OnHdSpriteDraw +0x1B67A site mismatch");
        return false;
    }

    g_draw_tramp_mem = MakeTrampoline(site, kDrawHitPatch, site + kDrawHitPatch);
    if (!g_draw_tramp_mem) {
        LogWarn("OnHdSpriteDraw trampoline alloc failed");
        return false;
    }

    g_mod_hd_sprite_draw_tramp = g_draw_tramp_mem;
    if (!WriteJump(site, reinterpret_cast<void*>(&ModHdSpriteDrawDetour), g_draw_original, kDrawHitPatch)) {
        RemoveHdDrawHook();
        LogWarn("OnHdSpriteDraw hook write failed");
        return false;
    }

    g_draw_site = site;

    return true;
#endif
}

void RemoveHdDrawHook() {
#if defined(_M_IX86)
    if (g_draw_site) {
        RestoreBytes(g_draw_site, g_draw_original, kDrawHitPatch);
        g_draw_site = nullptr;
    }
    g_mod_hd_sprite_draw_tramp = nullptr;
    if (g_draw_tramp_mem) {
        VirtualFree(g_draw_tramp_mem, 0, MEM_RELEASE);
        g_draw_tramp_mem = nullptr;
    }
#endif
}

}  // namespace grandia_mod
