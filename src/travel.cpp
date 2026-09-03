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
extern "C" std::uint8_t g_mod_wm_picture[16]{};
extern "C" std::uint8_t g_mod_wm_custom[16]{};
extern "C" void* g_mod_wm_pic_resume = nullptr;
extern "C" void* g_mod_wm_pic_skip = nullptr;
extern "C" void* g_mod_wm_rows_resume = nullptr;
extern "C" std::uint8_t* g_mod_wm_amap_rows = nullptr;

static bool g_need_icon_count = false;
static bool g_added_icon[16]{};
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
constexpr int kSlots = 16;
constexpr int kDestPerSet = 25;
constexpr int kSets = 4;
constexpr int kCursorPerSet = kDestPerSet * kSlots;
constexpr std::uint16_t kRevealBit = 0x397u;

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

void ResetPictures();
void SetPicture(int icon, int picture);

struct TableBackup {
    std::uint8_t dest[kDestPerSet * kSets * 4]{};
    std::uint8_t visited[kSlots * kSets * 2]{};
    std::uint8_t cursor[kCursorPerSet * kSets]{};
    std::uint8_t xy[kSlots * kSets * 4]{};
    std::uint8_t nav[kSlots * kSets * 4]{};
    std::uint8_t flags[kSlots * kSets * 2]{};
    std::uint8_t icon_count[kIconCountPatchSize]{};
    bool captured = false;
};

TableBackup g_tables{};

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
    WriteMem(reinterpret_cast<void*>(base + kVisitedTableRva), g_tables.visited,
             sizeof(g_tables.visited));
    WriteMem(reinterpret_cast<void*>(base + kCursorTableRva), g_tables.cursor, sizeof(g_tables.cursor));
    WriteMem(reinterpret_cast<void*>(base + kXyTableRva), g_tables.xy, sizeof(g_tables.xy));
    WriteMem(reinterpret_cast<void*>(base + kNavTableRva), g_tables.nav, sizeof(g_tables.nav));
    WriteMem(reinterpret_cast<void*>(base + kFlagTableRva), g_tables.flags, sizeof(g_tables.flags));
    WriteMem(reinterpret_cast<void*>(base + kWorldMapIconCountRva), g_tables.icon_count,
             kIconCountPatchSize);
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

void ApplyDests(std::uintptr_t base, int set_id, const WorldMapLoadNative& req,
                const WorldMapLoadNative& before) {
    if (set_id < 0 || set_id >= kSets) {
        return;
    }
    int n = req.count;
    if (n < 0) {
        n = 0;
    }
    if (n > kSlots) {
        n = kSlots;
    }

    auto* dest = reinterpret_cast<std::uint8_t*>(base + kDestTableRva + set_id * kDestPerSet * 4);
    auto* xy = reinterpret_cast<std::uint8_t*>(base + kXyTableRva + set_id * kSlots * 4);
    auto* flags = reinterpret_cast<std::uint8_t*>(base + kFlagTableRva + set_id * kSlots * 2);
    auto* nav = reinterpret_cast<std::uint8_t*>(base + kNavTableRva + set_id * kSlots * 4);
    auto* cursor = reinterpret_cast<std::uint8_t*>(base + kCursorTableRva + set_id * kCursorPerSet);

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
            WriteU16(flag_bytes + icon * 2, kRevealBit);
            nav_bytes[icon * 4 + 0] = 0;
            nav_bytes[icon * 4 + 1] = 0;
            nav_bytes[icon * 4 + 2] = 0;
            nav_bytes[icon * 4 + 3] = 0;
            bool linked = false;
            for (int s = 0; s < kSlots && !linked; ++s) {
                if (s == icon || ReadU16(flag_bytes + s * 2) == 0) {
                    continue;
                }
                for (int dir = 0; dir < 4; ++dir) {
                    if (nav_bytes[s * 4 + dir] == 0xFF) {
                        nav_bytes[s * 4 + dir] = static_cast<std::uint8_t>(icon);
                        linked = true;
                        break;
                    }
                }
            }
            SetPicture(icon, req.picture[i] >= 0 ? req.picture[i] : 0);
            SetWorldMapCustomPicture(icon, static_cast<int>(x), static_cast<int>(y),
                                     req.picture_path[i], req.picture_w[i], req.picture_h[i]);
            g_mod_wm_custom[icon] = req.picture_path[i][0] ? 1 : 0;
            LogInfo("OnWorldMapLoad add 0x%04X icon=%d dest=%d xy=(%d,%d) pic=%d custom=%d",
                    req.map_id[i], icon, dest_slot, static_cast<int>(x), static_cast<int>(y),
                    static_cast<int>(g_mod_wm_picture[icon] == 0xFF ? icon : g_mod_wm_picture[icon]),
                    static_cast<int>(g_mod_wm_custom[icon]));
            continue;
        }

        seen[icon] = true;
        kept[icon] = true;
        SetPicture(icon, req.picture[i]);
        SetWorldMapCustomPicture(icon, req.x[i], req.y[i], req.picture_path[i], req.picture_w[i],
                                 req.picture_h[i]);
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
            req.y[i] == before.y[before_i] && req.revealed[i] == before.revealed[before_i]) {
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
    }

    for (int i = 0; i < before.count && i < kSlots; ++i) {
        const int icon = before.slot[i];
        if (icon < 0 || icon >= kSlots || kept[icon]) {
            continue;
        }
        HideIcon(cursor_bytes, icon);
        WriteU16(flag_bytes + icon * 2, 0);
    }

    WriteMem(dest, dest_bytes, sizeof(dest_bytes));
    WriteMem(xy, xy_bytes, sizeof(xy_bytes));
    WriteMem(flags, flag_bytes, sizeof(flag_bytes));
    WriteMem(nav, nav_bytes, sizeof(nav_bytes));
    WriteMem(cursor, cursor_bytes, sizeof(cursor_bytes));

    g_need_icon_count = added;
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
    req->count = 0;
    if (set_id < 0 || set_id >= kSets) {
        return;
    }
    const int origin = req->origin_ctx;
    const auto* dest =
        reinterpret_cast<const std::uint16_t*>(base + kDestTableRva + set_id * kDestPerSet * 4);
    const auto* xy =
        reinterpret_cast<const std::int16_t*>(base + kXyTableRva + set_id * kSlots * 4);
    const auto* flags =
        reinterpret_cast<const std::uint16_t*>(base + kFlagTableRva + set_id * kSlots * 2);
    const auto* cursor =
        reinterpret_cast<const std::uint8_t*>(base + kCursorTableRva + set_id * kCursorPerSet);
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
        LogInfo("OnWorldMapConfirm dest=0x%04X deny", req.map_id);
    } else {
        LogInfo("OnWorldMapConfirm dest=0x%04X allow", req.map_id);
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
    if (stock_draw > 16) {
        stock_draw = 16;
    }

    const int set_id = g_apply_set_id;
    if (set_id >= 0 && set_id < 4) {
        auto* flags = reinterpret_cast<std::uint8_t*>(base + 0x201498u + set_id * 16 * 2);
        std::uint8_t flag_bytes[32]{};
        std::memcpy(flag_bytes, flags, sizeof(flag_bytes));
        for (int s = stock_draw; s < 16; ++s) {
            if (!g_added_icon[s]) {
                flag_bytes[s * 2] = 0;
                flag_bytes[s * 2 + 1] = 0;
            }
        }
        WriteBytes(flags, flag_bytes, sizeof(flag_bytes));
    }

    int max_icon = stock_draw - 1;
    for (int s = 0; s < 16; ++s) {
        if (g_added_icon[s] && s > max_icon) {
            max_icon = s;
        }
    }
    int edx = max_icon + 2;
    if (edx < 2) {
        edx = 2;
    }
    if (edx > 17) {
        edx = 17;
    }
    const std::uint8_t force[7] = {0xBA, static_cast<std::uint8_t>(edx), 0, 0, 0, 0x90, 0x90};
    WriteBytes(reinterpret_cast<void*>(base + 0x59C8Du), force, sizeof(force));
    LogInfo("OnWorldMapLoad icon-count edx=%d stock_draw=%d max_icon=%d", edx, stock_draw, max_icon);
}

extern "C" void ModAfterAmapRowCount() {
    AfterAmapRowCount();
}

void OnWorldMapLoad() {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    CaptureTables(base);
    ResetPictures();
    ClearWorldMapCustomPictures();

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

    char line[256]{};
    int used = 0;
    for (int i = 0; i < req.count; ++i) {
        const int n = std::snprintf(line + used, sizeof(line) - static_cast<std::size_t>(used),
                                    used ? " %04X@i%d%s" : "%04X@i%d%s", req.map_id[i], req.slot[i],
                                    req.revealed[i] ? "" : "?");
        if (n < 0) {
            break;
        }
        used += n;
        if (used >= static_cast<int>(sizeof(line)) - 8) {
            break;
        }
    }
    LogInfo("OnWorldMapLoad set=%d amap=%d origin=%d n=%d %s", set_id, amap, req.origin_ctx,
            req.count, line);

    if (RuntimeOnWorldMapLoad(&req) != 0) {
        return;
    }
    if (req.dirty == 0) {
        return;
    }
    RestoreTables(base);
    ApplyDests(base, set_id, req, before);
    LogInfo("OnWorldMapLoad write set=%d n=%d", set_id, req.count);
}

}  // namespace grandia_mod

extern "C" void ModOnWorldMapConfirm() {
    grandia_mod::OnWorldMapConfirm();
}

extern "C" void ModOnWorldMapLoad() {
    grandia_mod::OnWorldMapLoad();
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

extern "C" __declspec(naked) void ModWorldMapIconUvDetour() {
    __asm {
        mov byte ptr [esi + 10h], 0
        cmp edi, 16
        jae wm_pic_stock
        cmp byte ptr [g_mod_wm_custom + edi], 0
        je wm_pic_remap
        mov word ptr [esi + 16h], 10h
    wm_pic_remap:
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
        cmp edi, 16
        jae wm_pic_stock_draw
        cmp byte ptr [g_mod_wm_custom + edi], 0
        je wm_pic_stock_draw
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
    return true;
#endif
}

void RemoveWorldMapHook() {
    const auto base = ModuleBase();
    if (base != 0) {
        RestoreTables(base);
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
    g_mod_wm_pic_resume = nullptr;
    g_mod_wm_pic_skip = nullptr;
    g_mod_wm_rows_resume = nullptr;
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
constexpr std::uintptr_t kMapObjPtrRva = 0x23FA94u;
constexpr std::uintptr_t kPartyPtrRva = 0x31CD28u;
constexpr std::uintptr_t kSetupTravelRet = 0x7301Cu;
constexpr std::uintptr_t kWorldMapTravelRet = 0x584D8u;

void* g_map_travel_site = nullptr;
std::uint8_t g_map_travel_original[8]{};
void* g_setup_travel_site = nullptr;
std::uint8_t g_setup_travel_original[8]{};
int g_map_travel_logs = 0;

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
    if (g_map_travel_logs < 16) {
        ++g_map_travel_logs;
        grandia_mod::LogInfo("OnMapTravel from=0x%04X dest=0x%04X spawn=%d kind=%d allow=%d", req.from,
                             req.dest, req.spawn, req.kind, req.allow);
    }
    return req.allow ? 1 : 0;
}

}  // namespace

extern "C" {
void* g_mod_map_travel_resume = nullptr;
void* g_mod_setup_travel_resume = nullptr;
std::uint32_t g_mod_map_travel_dest = 0;
std::uint32_t g_mod_map_travel_spawn = 0;
}

extern "C" int __cdecl ModOnMapTravel(std::uint32_t dest, std::uint32_t spawn, std::uint32_t ret) {
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
}

}  // namespace grandia_mod
