#include "travel.h"

#include "clr_host.h"
#include "wm_picture.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>

extern "C" int ModFlagGet(unsigned event_id);
extern "C" std::uint8_t g_mod_wm_picture[32] = {
    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
};
extern "C" std::uint8_t g_mod_wm_custom[32]{};
extern "C" void* g_mod_wm_pic_resume = nullptr;
extern "C" void* g_mod_wm_pic_skip = nullptr;
extern "C" void* g_mod_wm_rows_resume = nullptr;
extern "C" std::uint8_t* g_mod_wm_amap_rows = nullptr;
extern "C" std::uint8_t* g_mod_wm_cursor_base = nullptr;
extern "C" void* g_mod_wm_cursor_confirm_resume = nullptr;
extern "C" void* g_mod_wm_cursor_dest_resume = nullptr;
extern "C" void* g_mod_wm_cursor_init_resume = nullptr;
extern "C" void* g_mod_wm_loop_resume = nullptr;
extern "C" int g_mod_wm_force_draw = 0;
extern "C" int g_mod_wm_icon_loop_phase = 0;

static bool g_need_icon_count = false;
static bool g_added_icon[32]{};
static int g_apply_set_id = 0;

static bool WriteBytes(void* dest, const void* src, std::size_t size) {
    if (!dest || !src || size == 0) {
        return false;
    }
    DWORD old_protect = 0;
    if (!VirtualProtect(dest, size, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return false;
    }
    std::memcpy(dest, src, size);
    VirtualProtect(dest, size, old_protect, &old_protect);
    FlushInstructionCache(GetCurrentProcess(), dest, size);
    return true;
}

extern "C" void ModAfterAmapRowCount();
#if defined(_M_IX86)
extern "C" void ModWmCursorConfirm();
extern "C" void ModWmCursorDest();
extern "C" void ModWorldMapCursorInitDetour();
extern "C" void ModWmIconLoopStart();
extern "C" void ModWmIconLoopEnd();
#endif

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kWorldMapConfirmRva = 0x58491u;
constexpr std::uintptr_t kWorldMapConfirmResumeRva = 0x58496u;
constexpr std::uintptr_t kWorldMapConfirmSkipRva = 0x584FBu;
constexpr std::uintptr_t kWorldMapDestMapRva = 0x2C2990u;
constexpr std::uintptr_t kWorldMapConfirmStateRva = 0x241197u;
constexpr std::uintptr_t kWorldMapLoadRva = 0x59320u;
constexpr std::uintptr_t kWorldMapOriginCtxRva = 0x24122Au;
constexpr std::uintptr_t kWorldMapIconCountRva = 0x59C8Du;
constexpr std::uintptr_t kWorldMapIconCountByteRva = 0x24118Au;
constexpr std::uintptr_t kWorldMapIconUvRva = 0x59E3Fu;
constexpr std::uintptr_t kWorldMapIconSkipRva = 0x59F1Bu;
constexpr std::uintptr_t kWorldMapAmapRowsWriteRva = 0x59524u;
constexpr std::uintptr_t kWorldMapAmapRowsResumeRva = 0x59529u;
constexpr std::uintptr_t kWorldMapIconLoopStartRva = 0x59CCDu;
constexpr std::uintptr_t kWorldMapIconLoopStartResumeRva = 0x59CD3u;
constexpr std::uintptr_t kWorldMapIconLoopEndRva = 0x59F2Eu;
constexpr std::uintptr_t kWorldMapCursorInitRva = 0x59684u;
constexpr std::uintptr_t kWorldMapCursorInitResumeRva = 0x59689u;
constexpr std::uintptr_t kWorldMapCursorIconRva = 0x241196u;
constexpr std::uintptr_t kWorldMapCursorIconCopyRva = 0x24105Fu;
constexpr std::uintptr_t kWorldMapAmapIndexRva = 0x24118Bu;
constexpr std::uintptr_t kFlagBlobPtrRva = 0x318BD8u;
constexpr std::uintptr_t kSetRemapRva = 0x200AB0u;
constexpr std::uintptr_t kDestTableRva = 0x200920u;
constexpr std::uintptr_t kVisitedTableRva = 0x200B50u;
constexpr std::uintptr_t kCursorTableRva = 0x200BD0u;
constexpr std::uintptr_t kXyTableRva = 0x201218u;
constexpr std::uintptr_t kNavTableRva = 0x201318u;
constexpr std::uintptr_t kFlagTableRva = 0x201498u;

constexpr std::size_t kPatchSize = 5;
constexpr std::size_t kLoadPatchSize = 8;
constexpr std::size_t kIconCountPatchSize = 7;
constexpr std::size_t kIconUvPatchSize = 7;
constexpr int kStockSlots = 16;
constexpr int kSlots = 32;
constexpr int kStockDestPerSet = 25;
constexpr int kDestPerSet = 32;
constexpr int kSets = 4;
constexpr int kStockAmapNav = 6;
constexpr int kAmapNavPages = 16;
constexpr int kStockCursorPerSet = kStockDestPerSet * kStockSlots;
constexpr int kCursorPerSet = kDestPerSet * kSlots;
constexpr std::uint16_t kRevealBit = 0x397u;
constexpr std::uint16_t kVisitedBit = 0x397u;

void* g_wm_site = nullptr;
std::uint8_t g_wm_original[8]{};
void* g_wm_load_site = nullptr;
std::uint8_t g_wm_load_original[16]{};
void* g_wm_load_tramp_mem = nullptr;
void* g_icon_count_site = nullptr;
std::uint8_t g_icon_count_original[8]{};
void* g_wm_pic_site = nullptr;
std::uint8_t g_wm_pic_original[8]{};
void* g_wm_rows_site = nullptr;
std::uint8_t g_wm_rows_original[8]{};
void* g_wm_cursor_confirm_site = nullptr;
std::uint8_t g_wm_cursor_confirm_original[16]{};
void* g_wm_cursor_dest_site = nullptr;
std::uint8_t g_wm_cursor_dest_original[16]{};
void* g_wm_cursor_init_site = nullptr;
std::uint8_t g_wm_cursor_init_original[8]{};
void* g_wm_loop_start_site = nullptr;
std::uint8_t g_wm_loop_start_original[8]{};
void* g_wm_loop_end_site = nullptr;
std::uint8_t g_wm_loop_end_original[8]{};

void ResetPictures();
void SetPicture(int icon, int picture);

struct TableBackup {
    std::uint8_t dest[kStockDestPerSet * kSets * 4]{};
    std::uint8_t visited[kStockSlots * kSets * 2]{};
    std::uint8_t cursor[kStockCursorPerSet * kSets]{};
    std::uint8_t xy[kStockSlots * kSets * 4]{};
    std::uint8_t nav[kStockAmapNav * kStockSlots * 4]{};
    std::uint8_t flags[kStockSlots * kSets * 2]{};
    std::uint8_t icon_count[kIconCountPatchSize]{};
    bool captured = false;
};

constexpr std::size_t kWideDest = static_cast<std::size_t>(kSets * kDestPerSet * 4);
constexpr std::size_t kWideXy = static_cast<std::size_t>(kSets * kSlots * 4);
constexpr std::size_t kWideFlags = static_cast<std::size_t>(kSets * kSlots * 2);
constexpr std::size_t kWideVisited = static_cast<std::size_t>(kSets * kSlots * 2);
constexpr std::size_t kWideNav = static_cast<std::size_t>(kAmapNavPages * kSlots * 4);
constexpr std::size_t kWideCursor = static_cast<std::size_t>(kSets * kCursorPerSet);
constexpr std::size_t kWideTotal =
    kWideDest + kWideXy + kWideFlags + kWideVisited + kWideNav + kWideCursor;

TableBackup g_tables{};
std::uint8_t* g_wide = nullptr;
std::uint8_t* g_dest = nullptr;
std::uint8_t* g_xy = nullptr;
std::uint8_t* g_flags = nullptr;
std::uint8_t* g_visited = nullptr;
std::uint8_t* g_nav = nullptr;
std::uint8_t* g_cursor = nullptr;
bool g_wide_patched = false;

struct WidePatch {
    void* site = nullptr;
    std::uint8_t original[8]{};
    std::size_t size = 0;
};

WidePatch g_wide_patches[48]{};
unsigned g_wide_patch_n = 0;

bool WriteMem(void* dest, const void* src, std::size_t size) {
    if (!dest || !src || size == 0) {
        return false;
    }
    DWORD old_protect = 0;
    if (!VirtualProtect(dest, size, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return false;
    }
    std::memcpy(dest, src, size);
    VirtualProtect(dest, size, old_protect, &old_protect);
    FlushInstructionCache(GetCurrentProcess(), dest, size);
    return true;
}

bool RecordPatch(void* site, const void* src, std::size_t size) {
    if (!site || !src || size == 0 || size > 8 || g_wide_patch_n >= 48u) {
        return false;
    }
    auto& p = g_wide_patches[g_wide_patch_n];
    p.site = site;
    p.size = size;
    std::memcpy(p.original, site, size);
    if (!WriteMem(site, src, size)) {
        return false;
    }
    ++g_wide_patch_n;
    return true;
}

void ExpandStockToWide() {
    if (!g_dest || !g_xy || !g_tables.captured) {
        return;
    }
    std::memset(g_wide, 0, kWideTotal);
    for (int set = 0; set < kSets; ++set) {
        std::memcpy(g_dest + set * kDestPerSet * 4, g_tables.dest + set * kStockDestPerSet * 4,
                    kStockDestPerSet * 4);
        std::memcpy(g_xy + set * kSlots * 4, g_tables.xy + set * kStockSlots * 4, kStockSlots * 4);
        std::memcpy(g_flags + set * kSlots * 2, g_tables.flags + set * kStockSlots * 2,
                    kStockSlots * 2);
        std::memcpy(g_visited + set * kSlots * 2, g_tables.visited + set * kStockSlots * 2,
                    kStockSlots * 2);
        for (int ctx = 0; ctx < kStockDestPerSet; ++ctx) {
            std::memcpy(g_cursor + set * kCursorPerSet + ctx * kSlots,
                        g_tables.cursor + set * kStockCursorPerSet + ctx * kStockSlots, kStockSlots);
        }
    }
    for (int amap = 0; amap < kStockAmapNav; ++amap) {
        std::memcpy(g_nav + amap * kSlots * 4, g_tables.nav + amap * kStockSlots * 4,
                    kStockSlots * 4);
    }
}

void CaptureTables(std::uintptr_t base) {
    if (g_tables.captured) {
        return;
    }
    std::memcpy(g_tables.dest, reinterpret_cast<void*>(base + kDestTableRva), sizeof(g_tables.dest));
    std::memcpy(g_tables.visited, reinterpret_cast<void*>(base + kVisitedTableRva),
                sizeof(g_tables.visited));
    std::memcpy(g_tables.cursor, reinterpret_cast<void*>(base + kCursorTableRva),
                sizeof(g_tables.cursor));
    std::memcpy(g_tables.xy, reinterpret_cast<void*>(base + kXyTableRva), sizeof(g_tables.xy));
    std::memcpy(g_tables.nav, reinterpret_cast<void*>(base + kNavTableRva), sizeof(g_tables.nav));
    std::memcpy(g_tables.flags, reinterpret_cast<void*>(base + kFlagTableRva), sizeof(g_tables.flags));
    std::memcpy(g_tables.icon_count, reinterpret_cast<void*>(base + kWorldMapIconCountRva),
                kIconCountPatchSize);
    g_tables.captured = true;
}

void RestoreTables(std::uintptr_t base) {
    if (!g_tables.captured) {
        return;
    }
    WriteMem(reinterpret_cast<void*>(base + kDestTableRva), g_tables.dest, sizeof(g_tables.dest));
    WriteMem(reinterpret_cast<void*>(base + kWorldMapIconCountRva), g_tables.icon_count,
             kIconCountPatchSize);
    ExpandStockToWide();
}

bool PatchU32(std::uintptr_t base, std::uintptr_t rva, std::uint32_t expect, std::uint32_t want) {
    auto* p = reinterpret_cast<std::uint8_t*>(base + rva);
    std::uint32_t have = 0;
    std::memcpy(&have, p, 4);
    if (have != expect) {
        LogWarn("world-map table imm mismatch at +0x%X (have 0x%08X)", static_cast<unsigned>(rva),
                have);
        return false;
    }
    return RecordPatch(p, &want, 4);
}

bool PatchShl5(std::uintptr_t base, std::uintptr_t rva) {
    auto* p = reinterpret_cast<std::uint8_t*>(base + rva);
    if (p[0] != 0xC1 || p[2] != 0x04) {
        LogWarn("world-map shl mismatch at +0x%X", static_cast<unsigned>(rva));
        return false;
    }
    std::uint8_t next[3] = {p[0], p[1], 0x05};
    return RecordPatch(p, next, 3);
}

bool PatchImul32(std::uintptr_t base, std::uintptr_t rva) {
    auto* p = reinterpret_cast<std::uint8_t*>(base + rva);
    if (p[0] != 0x6B || p[2] != 0x19) {
        LogWarn("world-map dest imul mismatch at +0x%X", static_cast<unsigned>(rva));
        return false;
    }
    const std::uint8_t next[3] = {p[0], p[1], 0x20};
    return RecordPatch(p, next, 3);
}

bool AllocWideTables() {
    if (g_wide) {
        return true;
    }
    g_wide = static_cast<std::uint8_t*>(VirtualAlloc(nullptr, kWideTotal, MEM_COMMIT | MEM_RESERVE,
                                                    PAGE_READWRITE));
    if (!g_wide) {
        return false;
    }
    g_dest = g_wide;
    g_xy = g_dest + kWideDest;
    g_flags = g_xy + kWideXy;
    g_visited = g_flags + kWideFlags;
    g_nav = g_visited + kWideVisited;
    g_cursor = g_nav + kWideNav;
    g_mod_wm_cursor_base = g_cursor;
    return true;
}

void FreeWideTables() {
    g_mod_wm_cursor_base = nullptr;
    g_dest = nullptr;
    g_xy = nullptr;
    g_flags = nullptr;
    g_visited = nullptr;
    g_nav = nullptr;
    g_cursor = nullptr;
    if (g_wide) {
        VirtualFree(g_wide, 0, MEM_RELEASE);
        g_wide = nullptr;
    }
}

bool InstallWideTables(std::uintptr_t base) {
    if (g_wide_patched) {
        return true;
    }
    CaptureTables(base);
    if (!AllocWideTables()) {
        LogWarn("world-map wide tables alloc failed");
        return false;
    }
    ExpandStockToWide();

    auto rollback = [&]() {
        if (g_wm_cursor_confirm_site) {
            RestoreBytes(g_wm_cursor_confirm_site, g_wm_cursor_confirm_original, 10);
            g_wm_cursor_confirm_site = nullptr;
        }
        if (g_wm_cursor_dest_site) {
            RestoreBytes(g_wm_cursor_dest_site, g_wm_cursor_dest_original, 9);
            g_wm_cursor_dest_site = nullptr;
        }
        for (unsigned i = g_wide_patch_n; i > 0; --i) {
            auto& p = g_wide_patches[i - 1];
            if (p.site && p.size) {
                WriteMem(p.site, p.original, p.size);
            }
        }
        g_wide_patch_n = 0;
        FreeWideTables();
    };

    const auto dest_addr = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(g_dest));
    const auto xy = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(g_xy));
    const auto nav = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(g_nav));
    const auto flags = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(g_flags));
    const auto visited = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(g_visited));

    if (!PatchImul32(base, 0x5898Fu) || !PatchImul32(base, 0x58CF5u)) {
        rollback();
        return false;
    }
    const auto stock_dest = static_cast<std::uint32_t>(base + kDestTableRva);
    if (!PatchU32(base, 0x589ABu, stock_dest, dest_addr) ||
        !PatchU32(base, 0x589B9u, stock_dest + 2, dest_addr + 2) ||
        !PatchU32(base, 0x58D20u, stock_dest, dest_addr) ||
        !PatchU32(base, 0x58D28u, stock_dest + 2, dest_addr + 2)) {
        rollback();
        return false;
    }

    const std::uintptr_t shl[] = {0x598CBu, 0x599BFu, 0x59BA3u, 0x59CDCu, 0x59D67u, 0x58A05u,
                                  0x58A26u, 0x58ABEu, 0x58ADBu, 0x58B73u, 0x58B94u, 0x58C2Bu,
                                  0x58C48u};
    for (auto rva : shl) {
        if (!PatchShl5(base, rva)) {
            rollback();
            return false;
        }
    }

    const auto stock_xy = static_cast<std::uint32_t>(base + kXyTableRva);
    const auto stock_nav = static_cast<std::uint32_t>(base + kNavTableRva);
    const auto stock_flags = static_cast<std::uint32_t>(base + kFlagTableRva);
    const auto stock_visited = static_cast<std::uint32_t>(base + kVisitedTableRva);

    const std::uintptr_t xy_imm[] = {0x598D9u, 0x599D2u, 0x59BAEu, 0x59D37u};
    const std::uintptr_t xy2_imm[] = {0x598E9u, 0x599CAu, 0x59BB6u, 0x59D41u};
    const std::uintptr_t flag_imm[] = {0x59D70u, 0x58A2Fu, 0x58AE4u, 0x58B9Du, 0x58C51u};
    const std::uintptr_t vis_imm[] = {0x59DBCu, 0x58A4Bu, 0x58B00u, 0x58BB9u, 0x58C6Du};
    for (auto rva : xy_imm) {
        if (!PatchU32(base, rva, stock_xy, xy)) {
            rollback();
            return false;
        }
    }
    for (auto rva : xy2_imm) {
        if (!PatchU32(base, rva, stock_xy + 2, xy + 2)) {
            rollback();
            return false;
        }
    }
    if (!PatchU32(base, 0x58A0Du, stock_nav, nav) ||
        !PatchU32(base, 0x58AC6u, stock_nav + 1, nav + 1) ||
        !PatchU32(base, 0x58B7Bu, stock_nav + 2, nav + 2) ||
        !PatchU32(base, 0x58C33u, stock_nav + 3, nav + 3)) {
        rollback();
        return false;
    }
    for (auto rva : flag_imm) {
        if (!PatchU32(base, rva, stock_flags, flags)) {
            rollback();
            return false;
        }
    }
    for (auto rva : vis_imm) {
        if (!PatchU32(base, rva, stock_visited, visited)) {
            rollback();
            return false;
        }
    }

    auto* batch = reinterpret_cast<std::uint8_t*>(base + 0x59E80u);
    if (batch[0] != 0x80 || batch[1] != 0xFF || batch[2] != 0x10) {
        LogWarn("world-map sprite-batch cmp mismatch at +0x59E80");
        rollback();
        return false;
    }
    const std::uint8_t batch_to[3] = {0x80, 0xFF, 0x20};
    if (!RecordPatch(batch, batch_to, 3)) {
        rollback();
        return false;
    }

#if defined(_M_IX86)
    auto* confirm = reinterpret_cast<std::uint8_t*>(base + 0x5899Bu);
    auto* dest = reinterpret_cast<std::uint8_t*>(base + 0x58D01u);
    if (confirm[0] != 0x03 || confirm[1] != 0xC9 || dest[0] != 0x03 || dest[1] != 0xC9) {
        LogWarn("world-map cursor lookup bytes mismatch");
        rollback();
        return false;
    }
    g_mod_wm_cursor_confirm_resume = reinterpret_cast<void*>(base + 0x589A5u);
    g_mod_wm_cursor_dest_resume = reinterpret_cast<void*>(base + 0x58D0Au);
    if (!WriteJump(confirm, reinterpret_cast<void*>(&ModWmCursorConfirm),
                   g_wm_cursor_confirm_original, 10)) {
        LogWarn("world-map cursor widen failed");
        rollback();
        return false;
    }
    g_wm_cursor_confirm_site = confirm;
    if (!WriteJump(dest, reinterpret_cast<void*>(&ModWmCursorDest), g_wm_cursor_dest_original,
                   9)) {
        LogWarn("world-map cursor widen failed");
        rollback();
        return false;
    }
    g_wm_cursor_dest_site = dest;
#endif

    g_wide_patched = true;
    LogInfo("OnWorldMapLoad: 32 icon/dest slots (custom plates for 16+)");
    return true;
}

void RemoveWideTables(std::uintptr_t base) {
    if (g_wm_cursor_confirm_site) {
        RestoreBytes(g_wm_cursor_confirm_site, g_wm_cursor_confirm_original, 10);
        g_wm_cursor_confirm_site = nullptr;
    }
    if (g_wm_cursor_dest_site) {
        RestoreBytes(g_wm_cursor_dest_site, g_wm_cursor_dest_original, 9);
        g_wm_cursor_dest_site = nullptr;
    }
    for (unsigned i = g_wide_patch_n; i > 0; --i) {
        auto& p = g_wide_patches[i - 1];
        if (p.site && p.size) {
            WriteMem(p.site, p.original, p.size);
        }
    }
    g_wide_patch_n = 0;
    g_wide_patched = false;
    if (g_tables.captured) {
        WriteMem(reinterpret_cast<void*>(base + kDestTableRva), g_tables.dest, sizeof(g_tables.dest));
        WriteMem(reinterpret_cast<void*>(base + kVisitedTableRva), g_tables.visited,
                 sizeof(g_tables.visited));
        WriteMem(reinterpret_cast<void*>(base + kCursorTableRva), g_tables.cursor,
                 sizeof(g_tables.cursor));
        WriteMem(reinterpret_cast<void*>(base + kXyTableRva), g_tables.xy, sizeof(g_tables.xy));
        WriteMem(reinterpret_cast<void*>(base + kNavTableRva), g_tables.nav, sizeof(g_tables.nav));
        WriteMem(reinterpret_cast<void*>(base + kFlagTableRva), g_tables.flags, sizeof(g_tables.flags));
        WriteMem(reinterpret_cast<void*>(base + kWorldMapIconCountRva), g_tables.icon_count,
                 kIconCountPatchSize);
    }
    FreeWideTables();
}

int ClampAmap(int amap) {
    if (amap < 0) {
        return 0;
    }
    if (amap >= kAmapNavPages) {
        return kAmapNavPages - 1;
    }
    return amap;
}

void WriteU16(std::uint8_t* p, std::uint16_t v) {
    p[0] = static_cast<std::uint8_t>(v & 0xFF);
    p[1] = static_cast<std::uint8_t>((v >> 8) & 0xFF);
}

void WriteI16(std::uint8_t* p, std::int16_t v) {
    WriteU16(p, static_cast<std::uint16_t>(v));
}

void HideIcon(std::uint8_t* cursor, int icon) {
    if (icon < 0 || icon >= kSlots) {
        return;
    }
    for (int ctx = 0; ctx < kDestPerSet; ++ctx) {
        cursor[ctx * kSlots + icon] = 0xFF;
    }
}

std::uint16_t ReadU16(const std::uint8_t* p) {
    return static_cast<std::uint16_t>(p[0] | (p[1] << 8));
}

void MarkDestUsed(const std::uint8_t* dest, const std::uint8_t* cursor, bool* used) {
    used[0] = true;
    for (int s = 0; s < kDestPerSet; ++s) {
        const auto map = ReadU16(dest + s * 4);
        const auto aux = ReadU16(dest + s * 4 + 2);
        if (map != 0 || (aux != 0 && aux != 0xFFFF)) {
            used[s] = true;
        }
    }
    for (int i = 0; i < kCursorPerSet; ++i) {
        const auto vis = cursor[i];
        if (vis != 0xFF && vis < kDestPerSet) {
            used[vis] = true;
        }
    }
}

int FindFreeDestSlot(const bool* used) {
    for (int s = 1; s < kDestPerSet; ++s) {
        if (!used[s]) {
            return s;
        }
    }
    return -1;
}

int FindFreeIcon(const std::uint8_t* flags, const std::uint8_t* xy, const bool* claimed) {
    for (int s = 0; s < kSlots; ++s) {
        if (claimed[s]) {
            continue;
        }
        if (ReadU16(flags + s * 2) != 0) {
            continue;
        }
        if (ReadU16(xy + s * 4) != 0 || ReadU16(xy + s * 4 + 2) != 0) {
            continue;
        }
        return s;
    }
    return -1;
}

bool IconLandable(std::uint16_t flag, std::uint16_t vis) {
    if (flag == 0 || vis == 0) {
        return false;
    }
    return ModFlagGet(flag) > 0 && ModFlagGet(vis) > 0;
}

int Dist2(int x0, int y0, int x1, int y1) {
    const int dx = x1 - x0;
    const int dy = y1 - y0;
    return dx * dx + dy * dy;
}

void RebuildAccessibleNav(std::uint8_t* nav, const std::uint8_t* xy, const std::uint8_t* flags,
                          const std::uint8_t* visited) {
    bool land[kSlots]{};
    int xs[kSlots]{};
    int ys[kSlots]{};
    for (int i = 0; i < kSlots; ++i) {
        xs[i] = static_cast<std::int16_t>(ReadU16(xy + i * 4));
        ys[i] = static_cast<std::int16_t>(ReadU16(xy + i * 4 + 2));
        land[i] = IconLandable(ReadU16(flags + i * 2), ReadU16(visited + i * 2));
        nav[i * 4 + 0] = 0xFF;
        nav[i * 4 + 1] = 0xFF;
        nav[i * 4 + 2] = 0xFF;
        nav[i * 4 + 3] = 0xFF;
    }
    for (int i = 0; i < kSlots; ++i) {
        if (!land[i]) {
            continue;
        }
        for (int dir = 0; dir < 4; ++dir) {
            int best = -1;
            int best_d = 0x7fffffff;
            for (int j = 0; j < kSlots; ++j) {
                if (j == i || !land[j]) {
                    continue;
                }
                const int dx = xs[j] - xs[i];
                const int dy = ys[j] - ys[i];
                if (dir == 0 && dy >= 0) {
                    continue;
                }
                if (dir == 1 && dy <= 0) {
                    continue;
                }
                if (dir == 2 && dx >= 0) {
                    continue;
                }
                if (dir == 3 && dx <= 0) {
                    continue;
                }
                const int d = Dist2(xs[i], ys[i], xs[j], ys[j]);
                if (d < best_d || (d == best_d && (best < 0 || j < best))) {
                    best_d = d;
                    best = j;
                }
            }
            if (best >= 0) {
                nav[i * 4 + dir] = static_cast<std::uint8_t>(best);
            }
        }
    }
}

int PickAccessibleStart(int set_id, int suggested) {
    if (set_id < 0 || set_id >= kSets || !g_flags || !g_visited || !g_xy) {
        return suggested;
    }
    const auto* flags = g_flags + set_id * kSlots * 2;
    const auto* visited = g_visited + set_id * kSlots * 2;
    const auto* xy = g_xy + set_id * kSlots * 4;
    if (suggested >= 0 && suggested < kSlots &&
        IconLandable(ReadU16(flags + suggested * 2), ReadU16(visited + suggested * 2))) {
        return suggested;
    }
    const bool have_from = suggested >= 0 && suggested < kSlots;
    const int sx = have_from ? static_cast<std::int16_t>(ReadU16(xy + suggested * 4)) : 0;
    const int sy = have_from ? static_cast<std::int16_t>(ReadU16(xy + suggested * 4 + 2)) : 0;
    int best = -1;
    int best_d = 0x7fffffff;
    for (int i = 0; i < kSlots; ++i) {
        if (!IconLandable(ReadU16(flags + i * 2), ReadU16(visited + i * 2))) {
            continue;
        }
        if (!have_from) {
            return i;
        }
        const int d = Dist2(sx, sy, static_cast<std::int16_t>(ReadU16(xy + i * 4)),
                            static_cast<std::int16_t>(ReadU16(xy + i * 4 + 2)));
        if (d < best_d || (d == best_d && (best < 0 || i < best))) {
            best_d = d;
            best = i;
        }
    }
    return best >= 0 ? best : suggested;
}

void ApplyDests(std::uintptr_t base, int set_id, int amap, const WorldMapLoadNative& req,
                const WorldMapLoadNative& before) {
    (void)base;
    if (set_id < 0 || set_id >= kSets || !g_dest || !g_xy) {
        return;
    }
    int n = req.count;
    if (n < 0) {
        n = 0;
    }
    if (n > kSlots) {
        n = kSlots;
    }
    amap = ClampAmap(amap);

    auto* dest = g_dest + set_id * kDestPerSet * 4;
    auto* xy = g_xy + set_id * kSlots * 4;
    auto* flags = g_flags + set_id * kSlots * 2;
    auto* nav = g_nav + amap * kSlots * 4;
    auto* cursor = g_cursor + set_id * kCursorPerSet;

    std::uint8_t dest_bytes[kDestPerSet * 4]{};
    std::memcpy(dest_bytes, dest, sizeof(dest_bytes));
    std::uint8_t xy_bytes[kSlots * 4]{};
    std::memcpy(xy_bytes, xy, sizeof(xy_bytes));
    std::uint8_t flag_bytes[kSlots * 2]{};
    std::memcpy(flag_bytes, flags, sizeof(flag_bytes));
    std::uint8_t cursor_bytes[kCursorPerSet]{};
    std::memcpy(cursor_bytes, cursor, sizeof(cursor_bytes));
    std::uint8_t nav_bytes[kSlots * 4]{};
    std::memcpy(nav_bytes, nav, sizeof(nav_bytes));
    auto* visited = g_visited + set_id * kSlots * 2;
    std::uint8_t visited_bytes[kSlots * 2]{};
    std::memcpy(visited_bytes, visited, sizeof(visited_bytes));

    bool seen[kSlots]{};
    bool kept[kSlots]{};
    bool dest_used[kDestPerSet]{};
    MarkDestUsed(dest_bytes, cursor_bytes, dest_used);

    bool added = false;
    bool added_icon[kSlots]{};
    int origin = req.origin_ctx;
    if (origin < 0 || origin >= kDestPerSet) {
        origin = 0;
    }

    for (int i = 0; i < n; ++i) {
        int icon = req.slot[i];
        const bool is_add = icon < 0 || icon >= kSlots || seen[icon];
        if (is_add) {
            icon = FindFreeIcon(flag_bytes, xy_bytes, seen);
            const int dest_slot = FindFreeDestSlot(dest_used);
            if (icon < 0 || dest_slot < 0) {
                LogWarn("OnWorldMapLoad: no free icon/dest for 0x%04X", req.map_id[i]);
                continue;
            }
            seen[icon] = true;
            added_icon[icon] = true;
            dest_used[dest_slot] = true;
            added = true;
            for (int ctx = 0; ctx < kDestPerSet; ++ctx) {
                cursor_bytes[ctx * kSlots + icon] = static_cast<std::uint8_t>(dest_slot);
            }
            WriteU16(dest_bytes + dest_slot * 4, req.map_id[i]);
            WriteU16(dest_bytes + dest_slot * 4 + 2, req.aux[i]);
            auto x = req.x[i];
            auto y = req.y[i];
            if (x == 0 && y == 0) {
                x = static_cast<std::int16_t>(-150 + (icon % 4) * 100);
                y = static_cast<std::int16_t>(-150 + (icon / 4) * 100);
            }
            WriteI16(xy_bytes + icon * 4, x);
            WriteI16(xy_bytes + icon * 4 + 2, y);
            WriteU16(flag_bytes + icon * 2, req.revealed[i] ? kRevealBit : 0);
            WriteU16(visited_bytes + icon * 2, req.accessible[i] ? kVisitedBit : 0);
            SetPicture(icon, req.picture[i] >= 0 ? req.picture[i] : 0);
            SetWorldMapCustomPicture(icon, static_cast<int>(x), static_cast<int>(y),
                                     req.picture_path[i], req.picture_w[i], req.picture_h[i],
                                     req.accessible[i] != 0);
            g_mod_wm_custom[icon] = req.picture_path[i][0] ? 1 : 0;
            continue;
        }

        seen[icon] = true;
        kept[icon] = true;
        SetPicture(icon, req.picture[i]);
        SetWorldMapCustomPicture(icon, req.x[i], req.y[i], req.picture_path[i], req.picture_w[i],
                                 req.picture_h[i], req.accessible[i] != 0);
        g_mod_wm_custom[icon] = req.picture_path[i][0] ? 1 : 0;
        int before_i = -1;
        for (int b = 0; b < before.count && b < kSlots; ++b) {
            if (before.slot[b] == icon) {
                before_i = b;
                break;
            }
        }
        if (before_i >= 0 && req.map_id[i] == before.map_id[before_i] &&
            req.aux[i] == before.aux[before_i] && req.x[i] == before.x[before_i] &&
            req.y[i] == before.y[before_i] && req.revealed[i] == before.revealed[before_i] &&
            req.accessible[i] == before.accessible[before_i]) {
            continue;
        }

        const auto vis = cursor_bytes[origin * kSlots + icon];
        const int dest_slot = vis == 0xFF ? -1 : static_cast<int>(vis);
        if (dest_slot >= 0 && dest_slot < kDestPerSet &&
            (before_i < 0 || req.map_id[i] != before.map_id[before_i] ||
             req.aux[i] != before.aux[before_i])) {
            WriteU16(dest_bytes + dest_slot * 4, req.map_id[i]);
            WriteU16(dest_bytes + dest_slot * 4 + 2, req.aux[i]);
        }
        WriteI16(xy_bytes + icon * 4, req.x[i]);
        WriteI16(xy_bytes + icon * 4 + 2, req.y[i]);
        const std::uint16_t bit = req.revealed[i] ? kRevealBit : 0;
        WriteU16(flag_bytes + icon * 2, bit);
        WriteU16(visited_bytes + icon * 2, req.accessible[i] ? kVisitedBit : 0);
    }

    for (int i = 0; i < before.count && i < kSlots; ++i) {
        const int icon = before.slot[i];
        if (icon < 0 || icon >= kSlots || kept[icon]) {
            continue;
        }
        HideIcon(cursor_bytes, icon);
        WriteU16(flag_bytes + icon * 2, 0);
        WriteU16(visited_bytes + icon * 2, 0);
    }

    RebuildAccessibleNav(nav_bytes, xy_bytes, flag_bytes, visited_bytes);

    WriteMem(dest, dest_bytes, sizeof(dest_bytes));
    WriteMem(xy, xy_bytes, sizeof(xy_bytes));
    WriteMem(flags, flag_bytes, sizeof(flag_bytes));
    WriteMem(visited, visited_bytes, sizeof(visited_bytes));
    WriteMem(nav, nav_bytes, sizeof(nav_bytes));
    WriteMem(cursor, cursor_bytes, sizeof(cursor_bytes));

    bool extra_icon = added;
    for (int s = kStockSlots; s < kSlots; ++s) {
        if (ReadU16(flag_bytes + s * 2) != 0) {
            extra_icon = true;
            added_icon[s] = true;
        }
    }
    g_need_icon_count = extra_icon;
    g_apply_set_id = set_id;
    std::memcpy(g_added_icon, added_icon, sizeof(g_added_icon));
}

int ComputeAmapIndex(std::uintptr_t base) {
    void* blob = nullptr;
    if (!SafeReadPointer(base + kFlagBlobPtrRva, &blob) || !blob) {
        return 0;
    }
    std::uint8_t bits = 0;
    if (!SafeReadByte(reinterpret_cast<std::uintptr_t>(blob) + 0x72u, &bits)) {
        return 0;
    }
    int amap = 0;
    if (bits & 2) {
        amap += 4;
    }
    if (bits & 4) {
        amap += 2;
    }
    if (bits & 8) {
        amap += 1;
    }
    return amap;
}

int ReadSetId(std::uintptr_t base, int amap) {
    if (amap < 0) {
        amap = 0;
    }
    if (amap > 15) {
        amap = 15;
    }
    std::uint8_t set_id = 0;
    if (!SafeReadByte(base + kSetRemapRva + static_cast<unsigned>(amap), &set_id)) {
        return 0;
    }
    return set_id;
}

void ResetPictures() {
    std::memset(g_mod_wm_picture, 0xFF, kSlots);
    std::memset(g_mod_wm_custom, 0, kSlots);
    std::memset(g_added_icon, 0, sizeof(g_added_icon));
    g_need_icon_count = false;
    g_mod_wm_force_draw = 0;
}

void SetPicture(int icon, int picture) {
    if (icon < 0 || icon >= kSlots) {
        return;
    }
    if (picture < 0 || picture >= kSlots || picture == icon) {
        g_mod_wm_picture[icon] = 0xFF;
        return;
    }
    g_mod_wm_picture[icon] = static_cast<std::uint8_t>(picture);
}

int ReadOriginCtx(std::uintptr_t base) {
    std::uint8_t ctx = 0;
    if (!SafeReadByte(base + kWorldMapOriginCtxRva, &ctx)) {
        return 0;
    }
    if (ctx >= kDestPerSet) {
        return 0;
    }
    return static_cast<int>(ctx);
}

void FillFromTables(std::uintptr_t base, int set_id, WorldMapLoadNative* req) {
    (void)base;
    req->count = 0;
    if (set_id < 0 || set_id >= kSets || !g_dest || !g_xy) {
        return;
    }
    const int origin = req->origin_ctx;
    const auto* dest = reinterpret_cast<const std::uint16_t*>(g_dest + set_id * kDestPerSet * 4);
    const auto* xy = reinterpret_cast<const std::int16_t*>(g_xy + set_id * kSlots * 4);
    const auto* flags = reinterpret_cast<const std::uint16_t*>(g_flags + set_id * kSlots * 2);
    const auto* visited = reinterpret_cast<const std::uint16_t*>(g_visited + set_id * kSlots * 2);
    const auto* cursor = g_cursor + set_id * kCursorPerSet;
    const auto hub = dest[0];
    for (int icon = 0; icon < kSlots; ++icon) {
        const auto flag = flags[icon];
        if (flag == 0) {
            continue;
        }
        const auto vis = cursor[origin * kSlots + icon];
        std::uint16_t map = 0;
        std::uint16_t aux = 0;
        if (vis != 0xFF && vis < kDestPerSet) {
            map = dest[vis * 2];
            aux = dest[vis * 2 + 1];
        }
        const int row = req->count;
        req->slot[row] = icon;
        req->map_id[row] = map;
        req->aux[row] = aux;
        req->x[row] = xy[icon * 2];
        req->y[row] = xy[icon * 2 + 1];
        const int bit = ModFlagGet(flag);
        req->revealed[row] = bit > 0 ? 1 : 0;
        req->accessible[row] = ModFlagGet(visited[icon]) > 0 ? 1 : 0;
        req->picture[row] = icon;

        int extra_n = 0;
        std::uint16_t seen[8]{};
        int seen_n = 0;
        if (map != 0) {
            seen[seen_n++] = map;
        }
        for (int ctx = 0; ctx < kDestPerSet && extra_n < 4; ++ctx) {
            const auto v = cursor[ctx * kSlots + icon];
            if (v == 0xFF || v >= kDestPerSet) {
                continue;
            }
            const auto other = dest[v * 2];
            if (other == 0 || other == map) {
                continue;
            }
            if (other == hub && icon != 0) {
                continue;
            }
            bool dup = false;
            for (int s = 0; s < seen_n; ++s) {
                if (seen[s] == other) {
                    dup = true;
                    break;
                }
            }
            if (dup) {
                continue;
            }
            if (seen_n < 8) {
                seen[seen_n++] = other;
            }
            req->extra[row * 4 + extra_n] = other;
            extra_n++;
        }
        req->extra_n[row] = static_cast<std::uint8_t>(extra_n);
        req->count++;
    }
}

}  // namespace
}  // namespace grandia_mod

extern "C" {
void* g_mod_wm_resume = nullptr;
void* g_mod_wm_skip = nullptr;
void* g_mod_wm_load_tramp = nullptr;
std::uint8_t g_mod_wm_orig_mov_eax[5]{};
std::uint16_t* g_mod_wm_dest = nullptr;
std::uint8_t* g_mod_wm_confirm_state = nullptr;
volatile unsigned g_mod_wm_pending_id = 0;
volatile int g_mod_wm_allow = 1;
}

namespace grandia_mod {

void OnWorldMapConfirm() {
    WorldMapConfirmNative req{};
    req.map_id = static_cast<std::uint16_t>(g_mod_wm_pending_id);
    req.allow = 1;
    if (RuntimeOnWorldMapConfirm(&req) != 0) {
        g_mod_wm_allow = 1;
        WorldMapNotifyTravelStarted();
        return;
    }
    g_mod_wm_allow = req.allow ? 1 : 0;
    if (!req.allow) {
        if (g_mod_wm_confirm_state) {
            *g_mod_wm_confirm_state = 0;
        }
    } else {
        WorldMapNotifyTravelStarted();
    }
}

void AfterAmapRowCount() {
    if (!g_need_icon_count) {
        return;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    std::uint8_t amap_rows = 0;
    SafeReadByte(base + 0x24118Au, &amap_rows);
    if (amap_rows < 2) {
        LogWarn("OnWorldMapLoad skip icon-count tweak; AMAP rows=%u", amap_rows);
        return;
    }
    int stock_draw = static_cast<int>(amap_rows) - 1;
    if (stock_draw < 0) {
        stock_draw = 0;
    }
    if (stock_draw > kSlots) {
        stock_draw = kSlots;
    }

    const int set_id = g_apply_set_id;
    if (set_id >= 0 && set_id < kSets && g_flags) {
        auto* flags = g_flags + set_id * kSlots * 2;
        std::uint8_t flag_bytes[kSlots * 2]{};
        std::memcpy(flag_bytes, flags, sizeof(flag_bytes));
        for (int s = stock_draw; s < kSlots; ++s) {
            if (!g_added_icon[s]) {
                flag_bytes[s * 2] = 0;
                flag_bytes[s * 2 + 1] = 0;
            }
        }
        WriteBytes(flags, flag_bytes, sizeof(flag_bytes));
    }

    int max_icon = stock_draw - 1;
    for (int s = 0; s < kSlots; ++s) {
        if (g_added_icon[s] && s > max_icon) {
            max_icon = s;
        }
    }
    int count = max_icon + 1;
    if (count < 1) {
        count = 1;
    }
    if (count > kSlots) {
        count = kSlots;
    }
    g_mod_wm_force_draw = count;
    if (g_tables.captured) {
        WriteBytes(reinterpret_cast<void*>(base + kWorldMapIconCountRva), g_tables.icon_count,
                   kIconCountPatchSize);
    }
}

extern "C" void ModAfterAmapRowCount() {
    AfterAmapRowCount();
}

void OnWorldMapCursorInit(int suggested) {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    std::uint8_t amap = 0;
    SafeReadByte(base + kWorldMapAmapIndexRva, &amap);
    int set_id = ReadSetId(base, static_cast<int>(amap));
    if (set_id < 0 || set_id >= kSets) {
        set_id = 0;
    }
    int icon = PickAccessibleStart(set_id, suggested);
    if (icon < 0) {
        icon = 0;
    }
    if (icon > 255) {
        icon = 255;
    }
    const auto v = static_cast<std::uint8_t>(icon);
    WriteBytes(reinterpret_cast<void*>(base + kWorldMapCursorIconRva), &v, 1);
    WriteBytes(reinterpret_cast<void*>(base + kWorldMapCursorIconCopyRva), &v, 1);
}

void OnWorldMapLoad() {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    CaptureTables(base);
    ResetPictures();
    if (g_tables.captured) {
        WriteMem(reinterpret_cast<void*>(base + kWorldMapIconCountRva), g_tables.icon_count,
                 kIconCountPatchSize);
    }
    ClearWorldMapCustomPictures();
    if (!InstallWideTables(base)) {
        LogWarn("OnWorldMapLoad: 32-slot tables unavailable; stock 16-icon map only");
        return;
    }

    const int amap = ComputeAmapIndex(base);
    int set_id = ReadSetId(base, amap);
    if (set_id < 0 || set_id >= kSets) {
        set_id = 0;
    }

    WorldMapLoadNative req{};
    req.set_id = set_id;
    req.amap_index = amap;
    req.origin_ctx = ReadOriginCtx(base);
    FillFromTables(base, set_id, &req);
    const WorldMapLoadNative before = req;

    if (RuntimeOnWorldMapLoad(&req) != 0) {
        return;
    }
    if (req.dirty == 0) {
        return;
    }
    RestoreTables(base);
    ApplyDests(base, set_id, amap, req, before);
}

}  // namespace grandia_mod

extern "C" void ModOnWorldMapConfirm() {
    grandia_mod::OnWorldMapConfirm();
}

extern "C" void ModOnWorldMapLoad() {
    grandia_mod::OnWorldMapLoad();
}

extern "C" void ModOnWorldMapCursorInit(int suggested) {
    grandia_mod::OnWorldMapCursorInit(suggested);
}

#if defined(_M_IX86)

extern "C" __declspec(naked) void ModWorldMapConfirmDetour() {
    __asm {
        pushad

        mov eax, dword ptr [g_mod_wm_dest]
        movzx eax, word ptr [eax]
        mov dword ptr [g_mod_wm_pending_id], eax

        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnWorldMapConfirm
        mov esp, dword ptr [esp]

        popad

        cmp dword ptr [g_mod_wm_allow], 0
        je wm_deny

        mov eax, dword ptr [g_mod_wm_orig_mov_eax + 1]
        mov eax, dword ptr [eax]
        jmp dword ptr [g_mod_wm_resume]

    wm_deny:
        jmp dword ptr [g_mod_wm_skip]
    }
}

extern "C" __declspec(naked) void ModWorldMapLoadDetour() {
    __asm {
        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnWorldMapLoad
        mov esp, dword ptr [esp]
        popad
        jmp dword ptr [g_mod_wm_load_tramp]
    }
}

extern "C" __declspec(naked) void ModWmCursorConfirm() {
    __asm {
        add ecx, ecx
        add ecx, ecx
        push eax
        add eax, dword ptr [g_mod_wm_cursor_base]
        movsx ecx, byte ptr [eax + ecx*8]
        pop eax
        jmp dword ptr [g_mod_wm_cursor_confirm_resume]
    }
}

extern "C" __declspec(naked) void ModWmCursorDest() {
    __asm {
        add ecx, ecx
        add ecx, ecx
        push eax
        add eax, dword ptr [g_mod_wm_cursor_base]
        mov al, byte ptr [eax + ecx*8]
        add esp, 4
        jmp dword ptr [g_mod_wm_cursor_dest_resume]
    }
}

extern "C" __declspec(naked) void ModWorldMapIconUvDetour() {
    __asm {
        mov byte ptr [esi + 10h], 0
        cmp edi, 32
        jae wm_pic_stock
        movzx eax, byte ptr [g_mod_wm_picture + edi]
        cmp al, 0FFh
        je wm_pic_stock
        inc eax
        shl eax, 4
        mov byte ptr [esi + 11h], al
        jmp wm_pic_note
    wm_pic_stock:
        mov al, byte ptr [ebp - 1]
        mov byte ptr [esi + 11h], al
    wm_pic_note:
        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 32
        mov dword ptr [esp + 28], eax
        mov dword ptr [esp], edi
        movsx eax, word ptr [esi + 0Ch]
        mov dword ptr [esp + 4], eax
        movsx eax, word ptr [esi + 0Eh]
        mov dword ptr [esp + 8], eax
        movsx eax, word ptr [esi + 14h]
        mov dword ptr [esp + 12], eax
        movsx eax, word ptr [esi + 16h]
        mov dword ptr [esp + 16], eax
        call WorldMapOnIconSubmit
        mov esp, dword ptr [esp + 28]
        popad
        cmp edi, 32
        jae wm_pic_skip_stock
        cmp byte ptr [g_mod_wm_custom + edi], 0
        je wm_pic_stock_draw
    wm_pic_skip_stock:
        jmp dword ptr [g_mod_wm_pic_skip]
    wm_pic_stock_draw:
        jmp dword ptr [g_mod_wm_pic_resume]
    }
}

extern "C" __declspec(naked) void ModWorldMapAmapRowsDetour() {
    __asm {
        push ecx
        mov ecx, dword ptr [g_mod_wm_amap_rows]
        mov byte ptr [ecx], al
        pop ecx
        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModAfterAmapRowCount
        mov esp, dword ptr [esp]
        popad
        jmp dword ptr [g_mod_wm_rows_resume]
    }
}

extern "C" __declspec(naked) void ModWorldMapCursorInitDetour() {
    __asm {
        pushad
        movzx eax, al
        mov ecx, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp + 12], ecx
        mov dword ptr [esp], eax
        call ModOnWorldMapCursorInit
        mov esp, dword ptr [esp + 12]
        popad
        jmp dword ptr [g_mod_wm_cursor_init_resume]
    }
}

extern "C" __declspec(naked) void ModWmIconLoopStart() {
    __asm {
        mov dword ptr [g_mod_wm_icon_loop_phase], 1
        cmp dword ptr [g_mod_wm_force_draw], 0
        je wm_loop_keep
        mov eax, dword ptr [g_mod_wm_force_draw]
        mov dword ptr [ebp - 18h], eax
    wm_loop_keep:
        xor bh, bh
        xor edi, edi
        test eax, eax
        jmp dword ptr [g_mod_wm_loop_resume]
    }
}

extern "C" __declspec(naked) void ModWmIconLoopEnd() {
    __asm {
        mov dword ptr [g_mod_wm_icon_loop_phase], 2
        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call WorldMapDrawOverlaysAfterIcons
        mov esp, dword ptr [esp]
        popad
        pop edi
        pop esi
        pop ebx
        mov esp, ebp
        pop ebp
        ret
    }
}

#endif

namespace grandia_mod {

bool InstallWorldMapHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    auto* site = reinterpret_cast<std::uint8_t*>(base + kWorldMapConfirmRva);
    if (!IsExecutableAddress(site) || site[0] != 0xA1) {
        LogWarn("world-map confirm bytes mismatch at +0x%X (expected A1)",
                static_cast<unsigned>(kWorldMapConfirmRva));
        return false;
    }

    g_mod_wm_dest = reinterpret_cast<std::uint16_t*>(base + kWorldMapDestMapRva);
    g_mod_wm_confirm_state = reinterpret_cast<std::uint8_t*>(base + kWorldMapConfirmStateRva);
    g_mod_wm_resume = reinterpret_cast<void*>(base + kWorldMapConfirmResumeRva);
    g_mod_wm_skip = reinterpret_cast<void*>(base + kWorldMapConfirmSkipRva);

    if (!WriteJump(site, reinterpret_cast<void*>(&ModWorldMapConfirmDetour), g_wm_original, kPatchSize)) {
        LogWarn("failed to install world-map gate");
        return false;
    }
    std::memcpy(g_mod_wm_orig_mov_eax, g_wm_original, 5);
    g_wm_site = site;
    LogInfo("OnWorldMapConfirm hook at grandia.exe+0x%X", static_cast<unsigned>(kWorldMapConfirmRva));

    auto* load = reinterpret_cast<std::uint8_t*>(base + kWorldMapLoadRva);
    if (!IsExecutableAddress(load) || load[0] != 0x55 || load[1] != 0x8B || load[2] != 0xEC ||
        load[3] != 0xA1) {
        LogWarn("OnWorldMapLoad bytes mismatch at +0x%X", static_cast<unsigned>(kWorldMapLoadRva));
        return true;
    }
    auto* icon = reinterpret_cast<std::uint8_t*>(base + kWorldMapIconCountRva);
    if (!IsExecutableAddress(icon) || icon[0] != 0x0F || icon[1] != 0xB6 || icon[2] != 0x15) {
        LogWarn("world-map icon-count bytes mismatch at +0x%X",
                static_cast<unsigned>(kWorldMapIconCountRva));
        return true;
    }

    CaptureTables(base);
    if (!InstallWideTables(base)) {
        LogWarn("OnWorldMapLoad 32-slot tables not installed");
    }
    g_wm_load_tramp_mem = MakeTrampoline(load, kLoadPatchSize, load + kLoadPatchSize);
    if (!g_wm_load_tramp_mem) {
        LogWarn("OnWorldMapLoad trampoline alloc failed");
        return true;
    }
    g_mod_wm_load_tramp = g_wm_load_tramp_mem;
    if (!WriteJump(load, reinterpret_cast<void*>(&ModWorldMapLoadDetour), g_wm_load_original,
                   kLoadPatchSize)) {
        VirtualFree(g_wm_load_tramp_mem, 0, MEM_RELEASE);
        g_wm_load_tramp_mem = nullptr;
        g_mod_wm_load_tramp = nullptr;
        LogWarn("failed to install OnWorldMapLoad");
        return true;
    }
    g_wm_load_site = load;
    g_icon_count_site = icon;
    LogInfo("OnWorldMapLoad hook at grandia.exe+0x%X", static_cast<unsigned>(kWorldMapLoadRva));

    auto* uv = reinterpret_cast<std::uint8_t*>(base + kWorldMapIconUvRva);
    if (!IsExecutableAddress(uv) || uv[0] != 0xC6 || uv[1] != 0x46 || uv[2] != 0x10) {
        LogWarn("world-map icon UV bytes mismatch at +0x%X",
                static_cast<unsigned>(kWorldMapIconUvRva));
        return true;
    }
    g_mod_wm_pic_resume = reinterpret_cast<void*>(base + kWorldMapIconUvRva + kIconUvPatchSize);
    g_mod_wm_pic_skip = reinterpret_cast<void*>(base + kWorldMapIconSkipRva);
    if (WriteJump(uv, reinterpret_cast<void*>(&ModWorldMapIconUvDetour), g_wm_pic_original,
                  kIconUvPatchSize)) {
        g_wm_pic_site = uv;
        LogInfo("OnWorldMapLoad icon art hook at +0x%X", static_cast<unsigned>(kWorldMapIconUvRva));
    }

    auto* rows = reinterpret_cast<std::uint8_t*>(base + kWorldMapAmapRowsWriteRva);
    if (IsExecutableAddress(rows) && rows[0] == 0xA2) {
        g_mod_wm_amap_rows = reinterpret_cast<std::uint8_t*>(base + kWorldMapIconCountByteRva);
        g_mod_wm_rows_resume = reinterpret_cast<void*>(base + kWorldMapAmapRowsResumeRva);
        if (WriteJump(rows, reinterpret_cast<void*>(&ModWorldMapAmapRowsDetour), g_wm_rows_original,
                      5)) {
            g_wm_rows_site = rows;
            LogInfo("OnWorldMapLoad AMAP row-count hook at +0x%X",
                    static_cast<unsigned>(kWorldMapAmapRowsWriteRva));
        }
    } else {
        LogWarn("AMAP row-count write bytes mismatch at +0x%X",
                static_cast<unsigned>(kWorldMapAmapRowsWriteRva));
    }

    auto* cursor_init = reinterpret_cast<std::uint8_t*>(base + kWorldMapCursorInitRva);
    if (IsExecutableAddress(cursor_init) && cursor_init[0] == 0xA2) {
        g_mod_wm_cursor_init_resume = reinterpret_cast<void*>(base + kWorldMapCursorInitResumeRva);
        if (WriteJump(cursor_init, reinterpret_cast<void*>(&ModWorldMapCursorInitDetour),
                      g_wm_cursor_init_original, 5)) {
            g_wm_cursor_init_site = cursor_init;
            LogInfo("OnWorldMapLoad cursor-start hook at +0x%X",
                    static_cast<unsigned>(kWorldMapCursorInitRva));
        }
    } else {
        LogWarn("world-map cursor-start bytes mismatch at +0x%X",
                static_cast<unsigned>(kWorldMapCursorInitRva));
    }

    auto* loop_start = reinterpret_cast<std::uint8_t*>(base + kWorldMapIconLoopStartRva);
    if (IsExecutableAddress(loop_start) && loop_start[0] == 0x32 && loop_start[1] == 0xFF) {
        g_mod_wm_loop_resume = reinterpret_cast<void*>(base + kWorldMapIconLoopStartResumeRva);
        if (WriteJump(loop_start, reinterpret_cast<void*>(&ModWmIconLoopStart),
                      g_wm_loop_start_original, 6)) {
            g_wm_loop_start_site = loop_start;
            LogInfo("OnWorldMapLoad icon-loop count hook at +0x%X",
                    static_cast<unsigned>(kWorldMapIconLoopStartRva));
        }
    } else {
        LogWarn("world-map icon-loop start bytes mismatch at +0x%X",
                static_cast<unsigned>(kWorldMapIconLoopStartRva));
    }

    auto* loop_end = reinterpret_cast<std::uint8_t*>(base + kWorldMapIconLoopEndRva);
    if (IsExecutableAddress(loop_end) && loop_end[0] == 0x5F) {
        if (WriteJump(loop_end, reinterpret_cast<void*>(&ModWmIconLoopEnd), g_wm_loop_end_original,
                      7)) {
            g_wm_loop_end_site = loop_end;
            LogInfo("OnWorldMapLoad icon-loop end overlay at +0x%X",
                    static_cast<unsigned>(kWorldMapIconLoopEndRva));
        }
    } else {
        LogWarn("world-map icon-loop end bytes mismatch at +0x%X",
                static_cast<unsigned>(kWorldMapIconLoopEndRva));
    }
    return true;
#endif
}

void RemoveWorldMapHook() {
    const auto base = ModuleBase();
    if (base != 0) {
        RemoveWideTables(base);
    }
    if (g_wm_site) {
        RestoreBytes(g_wm_site, g_wm_original, kPatchSize);
        g_wm_site = nullptr;
    }
    if (g_wm_load_site) {
        RestoreBytes(g_wm_load_site, g_wm_load_original, kLoadPatchSize);
        g_wm_load_site = nullptr;
    }
    if (g_wm_pic_site) {
        RestoreBytes(g_wm_pic_site, g_wm_pic_original, kIconUvPatchSize);
        g_wm_pic_site = nullptr;
    }
    if (g_wm_rows_site) {
        RestoreBytes(g_wm_rows_site, g_wm_rows_original, 5);
        g_wm_rows_site = nullptr;
    }
    if (g_wm_cursor_init_site) {
        RestoreBytes(g_wm_cursor_init_site, g_wm_cursor_init_original, 5);
        g_wm_cursor_init_site = nullptr;
    }
    if (g_wm_loop_start_site) {
        RestoreBytes(g_wm_loop_start_site, g_wm_loop_start_original, 6);
        g_wm_loop_start_site = nullptr;
    }
    if (g_wm_loop_end_site) {
        RestoreBytes(g_wm_loop_end_site, g_wm_loop_end_original, 7);
        g_wm_loop_end_site = nullptr;
    }
    g_mod_wm_force_draw = 0;
    g_mod_wm_loop_resume = nullptr;
    g_mod_wm_pic_resume = nullptr;
    g_mod_wm_pic_skip = nullptr;
    g_mod_wm_rows_resume = nullptr;
    g_mod_wm_cursor_init_resume = nullptr;
    if (g_wm_load_tramp_mem) {
        VirtualFree(g_wm_load_tramp_mem, 0, MEM_RELEASE);
        g_wm_load_tramp_mem = nullptr;
    }
    g_mod_wm_load_tramp = nullptr;
    g_icon_count_site = nullptr;
    g_mod_wm_dest = nullptr;
    g_mod_wm_confirm_state = nullptr;
}

}  // namespace grandia_mod

extern "C" std::uint32_t g_mod_map_travel_dest;
extern "C" std::uint32_t g_mod_map_travel_spawn;

namespace {

constexpr std::uintptr_t kMapTravelRva = 0x614D0u;
constexpr std::size_t kMapTravelPatch = 6u;
constexpr std::uintptr_t kMapTravelResumeRva = 0x614D6u;
constexpr std::uintptr_t kSetupTravelRva = 0x72F93u;
constexpr std::size_t kSetupTravelPatch = 7u;
constexpr std::uintptr_t kSetupTravelResumeRva = 0x72F9Au;
constexpr std::uintptr_t kClearAmapRva = 0x58330u;
constexpr std::uintptr_t kOpenAmapCallRva = 0x77618u;
constexpr std::uintptr_t kOpenAmap2CallRva = 0x7774Du;
constexpr std::uintptr_t kOpenAmapSkipRva = 0x77891u;
constexpr std::size_t kOpenAmapPatch = 5u;
constexpr std::uintptr_t kMapObjPtrRva = 0x23FA94u;
constexpr std::uintptr_t kPartyPtrRva = 0x31CD28u;
constexpr std::uintptr_t kSetupTravelRet = 0x7301Cu;
constexpr std::uintptr_t kWorldMapTravelRet = 0x584D8u;

void* g_map_travel_site = nullptr;
std::uint8_t g_map_travel_original[8]{};
void* g_setup_travel_site = nullptr;
std::uint8_t g_setup_travel_original[8]{};
void* g_open_amap_site = nullptr;
std::uint8_t g_open_amap_original[8]{};
void* g_open_amap2_site = nullptr;
std::uint8_t g_open_amap2_original[8]{};

std::uint16_t ReadCurrentMapId() {
    const auto base = grandia_mod::ModuleBase();
    if (base == 0) {
        return 0;
    }
    void* map_obj = nullptr;
    if (!grandia_mod::SafeReadPointer(base + kMapObjPtrRva, &map_obj) || !map_obj) {
        return 0;
    }
    std::uint32_t word = 0;
    if (!grandia_mod::SafeReadU32(reinterpret_cast<std::uintptr_t>(map_obj) + 8, &word)) {
        return 0;
    }
    return static_cast<std::uint16_t>(word);
}

int TravelKindFromRet(std::uintptr_t ret) {
    const auto rva = grandia_mod::CallerRva(ret);
    if (rva == kSetupTravelRet) {
        return 0;
    }
    if (rva == kWorldMapTravelRet) {
        return 1;
    }
    return 2;
}

void ReleasePartyWalk() {
    const auto base = grandia_mod::ModuleBase();
    if (base == 0) {
        return;
    }
    void* party = nullptr;
    if (!grandia_mod::SafeReadPointer(base + kPartyPtrRva, &party) || !party) {
        return;
    }
    const auto at = reinterpret_cast<std::uintptr_t>(party);
    std::uint8_t flags = 0;
    if (grandia_mod::SafeReadByte(at + 1, &flags)) {
        grandia_mod::SafeWriteByte(at + 1, static_cast<std::uint8_t>(flags & ~0x48u));
    }
    grandia_mod::SafeWriteByte(at + 0x1B, 0);
    grandia_mod::SafeWriteU32(at + 0x3A, 0);
}

int RaiseMapTravel(std::uint32_t dest, std::uint32_t spawn, int kind) {
    grandia_mod::MapTravelNative req{};
    req.from = ReadCurrentMapId();
    req.dest = static_cast<std::uint16_t>(dest);
    req.spawn = static_cast<std::int32_t>(spawn & 0xFFFF);
    req.allow = 1;
    req.kind = kind;
    if (grandia_mod::RuntimeOnMapTravel(&req) != 0) {
        g_mod_map_travel_dest = dest;
        g_mod_map_travel_spawn = spawn;
        return 1;
    }
    g_mod_map_travel_dest = req.dest;
    g_mod_map_travel_spawn = static_cast<std::uint32_t>(req.spawn) & 0xFFFFu;
    return req.allow ? 1 : 0;
}

}  // namespace

extern "C" {
void* g_mod_map_travel_resume = nullptr;
void* g_mod_setup_travel_resume = nullptr;
void* g_mod_map_travel_fn = nullptr;
void* g_mod_clear_amap = nullptr;
void* g_mod_open_amap_resume = nullptr;
void* g_mod_open_amap4_resume = nullptr;
void* g_mod_open_amap6_resume = nullptr;
void* g_mod_open_amap_skip = nullptr;
std::uint32_t g_mod_map_travel_dest = 0;
std::uint32_t g_mod_map_travel_spawn = 0;
volatile int g_mod_skip_map_travel = 0;
volatile int g_mod_open_amap_rc = 0;
}

extern "C" int __cdecl ModOnMapTravel(std::uint32_t dest, std::uint32_t spawn, std::uint32_t ret) {
    if (g_mod_skip_map_travel) {
        g_mod_map_travel_dest = dest;
        g_mod_map_travel_spawn = spawn;
        return 1;
    }
    if (grandia_mod::CallerRva(ret) == kSetupTravelRet) {
        g_mod_map_travel_dest = dest;
        g_mod_map_travel_spawn = spawn;
        return 1;
    }
    const int allow = RaiseMapTravel(dest, spawn, TravelKindFromRet(ret));
    if (allow == 0) {
        ReleasePartyWalk();
    }
    return allow;
}

extern "C" int __cdecl ModOnFieldTravel(std::uint32_t dest, std::uint32_t spawn) {
    const int allow = RaiseMapTravel(dest, spawn, 0);
    if (allow == 0) {
        ReleasePartyWalk();
    }
    return allow;
}

// 0 = stay, 1 = open area map, 2 = dest-load Destination.
extern "C" int __cdecl ModOnOpenAmapTravel(unsigned node) {
    const int allow = RaiseMapTravel(0, node, 3);
    if (allow == 0) {
        ReleasePartyWalk();
        return 0;
    }
    if (g_mod_map_travel_dest != 0) {
        return 2;
    }
    return 1;
}

#if defined(_M_IX86)
extern "C" __declspec(naked) void ModSetupTravelDetour() {
    __asm {
        mov dword ptr [ebp - 4], eax
        movzx eax, byte ptr [edx + 0x11]
        pushad
        push dword ptr [ebp - 4]
        push dword ptr [ebp - 8]
        call ModOnFieldTravel
        add esp, 8
        test eax, eax
        jz setup_cancel
        popad
        mov eax, dword ptr [g_mod_map_travel_dest]
        mov dword ptr [ebp - 8], eax
        mov eax, dword ptr [g_mod_map_travel_spawn]
        mov dword ptr [ebp - 4], eax
        test byte ptr [edx + 4], 0x10
        movzx eax, byte ptr [edx + 0x11]
        jmp dword ptr [g_mod_setup_travel_resume]
    setup_cancel:
        popad
        mov word ptr [edi], 0
        and byte ptr [edx + 3], 0x80
        pop edi
        pop esi
        pop ebx
        mov esp, ebp
        pop ebp
        ret
    }
}

extern "C" void ModOpenAmapShared();
extern "C" void ModOpenAmap4Detour();
extern "C" void ModOpenAmap6Detour();

extern "C" __declspec(naked) void ModOpenAmap4Detour() {
    __asm {
        pushad
        mov eax, dword ptr [g_mod_open_amap4_resume]
        mov dword ptr [g_mod_open_amap_resume], eax
        jmp ModOpenAmapShared
    }
}

extern "C" __declspec(naked) void ModOpenAmap6Detour() {
    __asm {
        pushad
        mov eax, dword ptr [g_mod_open_amap6_resume]
        mov dword ptr [g_mod_open_amap_resume], eax
        jmp ModOpenAmapShared
    }
}

extern "C" __declspec(naked) void ModOpenAmapShared() {
    __asm {
        movzx eax, byte ptr [esi + 5]
        push eax
        call ModOnOpenAmapTravel
        add esp, 4
        mov dword ptr [g_mod_open_amap_rc], eax
        popad
        cmp dword ptr [g_mod_open_amap_rc], 0
        je open_amap_cancel
        cmp dword ptr [g_mod_open_amap_rc], 2
        je open_amap_redirect
        call dword ptr [g_mod_clear_amap]
        jmp dword ptr [g_mod_open_amap_resume]
    open_amap_cancel:
        jmp dword ptr [g_mod_open_amap_skip]
    open_amap_redirect:
        mov eax, dword ptr [edi + 4]
        mov word ptr [edi], 0
        and byte ptr [eax + 3], 0x80
        mov ecx, dword ptr [g_mod_map_travel_dest]
        mov edx, dword ptr [g_mod_map_travel_spawn]
        mov dword ptr [g_mod_skip_map_travel], 1
        call dword ptr [g_mod_map_travel_fn]
        mov dword ptr [g_mod_skip_map_travel], 0
        pop edi
        pop esi
        pop ebx
        mov esp, ebp
        pop ebp
        ret
    }
}

extern "C" __declspec(naked) void ModMapTravelDetour() {
    // PUSHAD low→high: EDI ESI EBP ESP EBX EDX ECX EAX ; [esp+0x20] = caller ret
    __asm {
        pushad
        push dword ptr [esp + 0x20]
        push dword ptr [esp + 0x18]
        push dword ptr [esp + 0x20]
        call ModOnMapTravel
        add esp, 12
        test eax, eax
        jz travel_cancel
        popad
        mov ecx, dword ptr [g_mod_map_travel_dest]
        mov edx, dword ptr [g_mod_map_travel_spawn]
        push ebp
        mov ebp, esp
        sub esp, 0x18
        jmp dword ptr [g_mod_map_travel_resume]
    travel_cancel:
        popad
        ret
    }
}
#endif

namespace grandia_mod {

bool InstallMapTravelHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    auto* site = reinterpret_cast<std::uint8_t*>(base + kMapTravelRva);
    const std::uint8_t expect[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x18};
    if (!IsExecutableAddress(site) || !BytesMatch(site, expect, kMapTravelPatch)) {
        LogWarn("OnMapTravel +0x614D0 site mismatch");
        return false;
    }
    g_mod_map_travel_resume = reinterpret_cast<void*>(base + kMapTravelResumeRva);
    if (!WriteJump(site, reinterpret_cast<void*>(&ModMapTravelDetour), g_map_travel_original,
                   kMapTravelPatch)) {
        LogWarn("OnMapTravel +0x614D0 hook failed");
        return false;
    }
    g_map_travel_site = site;
    LogInfo("OnMapTravel hook at +0x614D0");

    auto* setup = reinterpret_cast<std::uint8_t*>(base + kSetupTravelRva);
    const std::uint8_t setup_expect[] = {0x89, 0x45, 0xFC, 0x0F, 0xB6, 0x42, 0x11};
    if (!IsExecutableAddress(setup) || !BytesMatch(setup, setup_expect, kSetupTravelPatch)) {
        LogWarn("OnMapTravel setup +0x72F93 site mismatch");
        return true;
    }
    g_mod_setup_travel_resume = reinterpret_cast<void*>(base + kSetupTravelResumeRva);
    if (!WriteJump(setup, reinterpret_cast<void*>(&ModSetupTravelDetour), g_setup_travel_original,
                   kSetupTravelPatch)) {
        LogWarn("OnMapTravel setup +0x72F93 hook failed");
        return true;
    }
    g_setup_travel_site = setup;
    LogInfo("OnMapTravel field setup hook at +0x72F93");

    g_mod_map_travel_fn = reinterpret_cast<void*>(base + kMapTravelRva);
    g_mod_clear_amap = reinterpret_cast<void*>(base + kClearAmapRva);
    auto* skip = reinterpret_cast<std::uint8_t*>(base + kOpenAmapSkipRva);
    const std::uint8_t skip_expect[] = {0x8B, 0x47, 0x04};
    if (!IsExecutableAddress(skip) || !BytesMatch(skip, skip_expect, 3)) {
        LogWarn("OnMapTravel open_amap skip +0x%X site mismatch",
                static_cast<unsigned>(kOpenAmapSkipRva));
        return true;
    }
    g_mod_open_amap_skip = skip;

    auto install_open_amap = [&](std::uintptr_t rva, void* detour, void** site_out,
                                 std::uint8_t* original, void** resume_out) -> bool {
        auto* call = reinterpret_cast<std::uint8_t*>(base + rva);
        if (!IsExecutableAddress(call) || call[0] != 0xE8) {
            return false;
        }
        const auto rel = *reinterpret_cast<std::int32_t*>(call + 1);
        const auto target = reinterpret_cast<std::uintptr_t>(call + 5 + rel);
        if (target != base + kClearAmapRva) {
            return false;
        }
        *resume_out = call + kOpenAmapPatch;
        if (!WriteJump(call, detour, original, kOpenAmapPatch)) {
            return false;
        }
        *site_out = call;
        return true;
    };

    if (!install_open_amap(kOpenAmapCallRva, reinterpret_cast<void*>(&ModOpenAmap4Detour),
                           &g_open_amap_site, g_open_amap_original, &g_mod_open_amap4_resume) ||
        !install_open_amap(kOpenAmap2CallRva, reinterpret_cast<void*>(&ModOpenAmap6Detour),
                           &g_open_amap2_site, g_open_amap2_original, &g_mod_open_amap6_resume)) {
        if (g_open_amap_site) {
            RestoreBytes(g_open_amap_site, g_open_amap_original, kOpenAmapPatch);
            g_open_amap_site = nullptr;
        }
        if (g_open_amap2_site) {
            RestoreBytes(g_open_amap2_site, g_open_amap2_original, kOpenAmapPatch);
            g_open_amap2_site = nullptr;
        }
        g_mod_open_amap4_resume = nullptr;
        g_mod_open_amap6_resume = nullptr;
        LogWarn("OnMapTravel open_amap +0x%X / +0x%X site mismatch",
                static_cast<unsigned>(kOpenAmapCallRva),
                static_cast<unsigned>(kOpenAmap2CallRva));
        return true;
    }
    LogInfo("OnMapTravel world-map exit hook at +0x%X / +0x%X",
            static_cast<unsigned>(kOpenAmapCallRva),
            static_cast<unsigned>(kOpenAmap2CallRva));
    return true;
#endif
}

void RemoveMapTravelHook() {
    if (g_map_travel_site) {
        RestoreBytes(g_map_travel_site, g_map_travel_original, kMapTravelPatch);
        g_map_travel_site = nullptr;
    }
    if (g_setup_travel_site) {
        RestoreBytes(g_setup_travel_site, g_setup_travel_original, kSetupTravelPatch);
        g_setup_travel_site = nullptr;
    }
    if (g_open_amap_site) {
        RestoreBytes(g_open_amap_site, g_open_amap_original, kOpenAmapPatch);
        g_open_amap_site = nullptr;
    }
    if (g_open_amap2_site) {
        RestoreBytes(g_open_amap2_site, g_open_amap2_original, kOpenAmapPatch);
        g_open_amap2_site = nullptr;
    }
}

}  // namespace grandia_mod
