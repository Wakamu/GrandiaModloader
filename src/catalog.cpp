#include "catalog.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <atomic>
#include <cstdint>
#include <cstring>

#if defined(_M_IX86)
extern "C" {
void* g_mod_windt_fin1 = nullptr;
void* g_mod_windt_fin2 = nullptr;
void* g_mod_windt_fin3 = nullptr;
void ModWindtFinalize1();
void ModWindtFinalize2();
void ModWindtFinalize3();
void ModOnWindtReadyItems();
void ModOnWindtReadyMagic();
}
#endif

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kMapObjPtrRva = 0x23FA94u;
constexpr std::uintptr_t kCharBlockOff = 0x10Cu;
constexpr std::uintptr_t kCharStride = 0x80u;
constexpr std::uintptr_t kSkillBitBaseOff = 0x50Cu;
constexpr int kSkillIdMax = 0x7F;
constexpr unsigned kCharCount = 8;
constexpr std::uintptr_t kOffLevel = 0x03u;
constexpr std::uintptr_t kOffHpMax = 0x0Au;
constexpr std::uintptr_t kOffHpCur = 0x0Cu;
constexpr std::uintptr_t kOffStr = 0x0Eu;
constexpr std::uintptr_t kOffVit = 0x10u;
constexpr std::uintptr_t kOffWit = 0x12u;
constexpr std::uintptr_t kOffAgi = 0x14u;
constexpr std::uintptr_t kOffSpMax = 0x16u;
constexpr std::uintptr_t kOffSpCur = 0x18u;
constexpr std::uintptr_t kOffMagicLv = 0x2Cu;
constexpr std::uintptr_t kOffWeaponLv = 0x30u;
constexpr std::uintptr_t kOffExp = 0x34u;
constexpr std::uintptr_t kOffMp1Cur = 0x3Cu;
constexpr std::uintptr_t kOffWeaponType = 0x74u;

constexpr std::uintptr_t kWindtHeapRva = 0x240E68u;
constexpr std::uintptr_t kWindtFromHeap = 0x30000u;
constexpr unsigned kWindtRecSize = 28;
constexpr std::uintptr_t kWindtSec3Off = 0x444Cu;
constexpr std::uintptr_t kWindtSec3AliasRvas[] = {
    0x300240u, 0x3015A0u, 0x302558u, 0x30B364u, 0x308E24u, 0x307FD8u,
};
// After each menu loader publishes sec3 (status / item-assign / stash).
constexpr std::uintptr_t kWindtFinalizeCallRvas[] = {
    0x1C3E6Cu,
    0x1DCDBEu,
    0x1E9467u,
};
// PS1 RAM image; STAT.BIN is copied at +0x14000 (learn sec2, combat sec0).
constexpr std::uintptr_t kStatRamPtrRva = 0x2D1A98u;
constexpr std::uintptr_t kStatInRam = 0x14000u;
constexpr std::size_t kStatRamSize = 0x163A00u;
// FWIN copies relocated sec pointers here (status finalize +0x1C3E49).
// [0x70CF88] is the 24-byte combat table the magic list / MP subtract uses.
constexpr std::uintptr_t kMenuCombatPtrRvas[] = {
    0x30CF88u,
    0x30CFF4u,
    0x30D008u,
    0x30CF94u,
};
constexpr unsigned kSkillIdCount = 0x80;
constexpr unsigned kLearnStride = 7;
constexpr unsigned kCombatStride = 24;
constexpr unsigned kMagicTableCap = 16;

#include "catalog_magic.inc"

std::atomic<int> g_items_fired{0};
std::atomic<int> g_chars_fired{0};
std::atomic<int> g_chars_busy{0};
std::atomic<int> g_load_pending{0};
std::atomic<int> g_saw_justin_zero{0};
std::atomic<int> g_had_party_at_title{0};
std::atomic<int> g_magic_fired{0};
std::atomic<int> g_magic_spawn_done{0};
bool g_windt_finalize_hooked = false;
std::uint16_t g_catalog_sell_gold[512]{};
std::uint8_t g_catalog_sell_set[512]{};

void* g_fin_site[3]{};
std::uint8_t g_fin_original[3][5]{};

bool PtrReadable(const void* p, std::size_t bytes) {
    if (!p || bytes == 0) {
        return false;
    }
    MEMORY_BASIC_INFORMATION mbi{};
    if (VirtualQuery(p, &mbi, sizeof(mbi)) == 0) {
        return false;
    }
    if (mbi.State != MEM_COMMIT) {
        return false;
    }
    const DWORD prot = mbi.Protect & 0xFFu;
    if (prot == PAGE_NOACCESS || prot == PAGE_EXECUTE || (mbi.Protect & PAGE_GUARD) != 0) {
        return false;
    }
    const auto start = reinterpret_cast<const std::uint8_t*>(mbi.BaseAddress);
    const auto end = start + mbi.RegionSize;
    const auto ptr = reinterpret_cast<const std::uint8_t*>(p);
    return ptr >= start && ptr + bytes <= end;
}

std::uint16_t Read16(const std::uint8_t* p) {
    std::uint16_t v = 0;
    std::memcpy(&v, p, 2);
    return v;
}

std::uint32_t Read32(const std::uint8_t* p) {
    std::uint32_t v = 0;
    std::memcpy(&v, p, 4);
    return v;
}

void Write16(std::uint8_t* p, int value) {
    auto v = static_cast<std::uint16_t>(value < 0 ? 0 : value > 0xFFFF ? 0xFFFF : value);
    std::memcpy(p, &v, 2);
}

void Write32(std::uint8_t* p, int value) {
    auto v = static_cast<std::uint32_t>(value < 0 ? 0 : value);
    std::memcpy(p, &v, 4);
}

std::uint8_t ClampU8(int value) {
    if (value < 0) {
        return 0;
    }
    if (value > 255) {
        return 255;
    }
    return static_cast<std::uint8_t>(value);
}

// Combat rows are 1-based skill ids with a dummy at index 0. The magic list,
// MP subtract, and heal/damage all use ptr + id*24 - 24 (Burn = row 11).
std::uint8_t* CombatRec(std::uint8_t* combat, int id) {
    if (!combat || id < 1) {
        return nullptr;
    }
    return combat + static_cast<unsigned>(id - 1) * kCombatStride;
}

bool WriteCall(void* site, void* destination, std::uint8_t* original_out) {
    auto* bytes = reinterpret_cast<std::uint8_t*>(site);
    if (!bytes || bytes[0] != 0xE8) {
        return false;
    }
    DWORD old_protect = 0;
    if (!VirtualProtect(site, 5, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return false;
    }
    if (original_out) {
        std::memcpy(original_out, site, 5);
    }
    bytes[0] = 0xE8;
    const auto rel = static_cast<std::int32_t>(reinterpret_cast<std::uint8_t*>(destination) - (bytes + 5));
    std::memcpy(bytes + 1, &rel, sizeof(rel));
    VirtualProtect(site, 5, old_protect, &old_protect);
    FlushInstructionCache(GetCurrentProcess(), site, 5);
    return true;
}

void* CallTarget(void* site) {
    auto* bytes = reinterpret_cast<std::uint8_t*>(site);
    if (!bytes || bytes[0] != 0xE8) {
        return nullptr;
    }
    std::int32_t rel = 0;
    std::memcpy(&rel, bytes + 1, 4);
    return bytes + 5 + rel;
}

std::uint8_t* MapObject() {
    void* ptr = nullptr;
    if (!SafeReadPointer(ModuleBase() + kMapObjPtrRva, &ptr) || !ptr) {
        return nullptr;
    }
    if (reinterpret_cast<std::uintptr_t>(ptr) < 0x10000) {
        return nullptr;
    }
    return static_cast<std::uint8_t*>(ptr);
}

std::uint8_t* CharBlock(std::uint8_t* map_obj, int char_id) {
    if (!map_obj || char_id < 1 || char_id > static_cast<int>(kCharCount)) {
        return nullptr;
    }
    return map_obj + kCharBlockOff + static_cast<std::uintptr_t>(char_id - 1) * kCharStride;
}

bool LooksLikeSec3(const std::uint8_t* sec3) {
    if (!sec3 || !PtrReadable(sec3, kWindtRecSize)) {
        return false;
    }
    return Read16(sec3) == 1;
}

void AddSec3(std::uint8_t** out, unsigned* n, unsigned cap, std::uint8_t* p) {
    if (!LooksLikeSec3(p) || !out || !n) {
        return;
    }
    for (unsigned i = 0; i < *n; ++i) {
        if (out[i] == p) {
            return;
        }
    }
    if (*n < cap) {
        out[(*n)++] = p;
    }
}

unsigned CollectSec3(std::uint8_t** out, unsigned cap) {
    unsigned n = 0;
    void* heap = nullptr;
    if (SafeReadPointer(ModuleBase() + kWindtHeapRva, &heap) && heap) {
        auto* windt = static_cast<std::uint8_t*>(heap) + kWindtFromHeap;
        if (PtrReadable(windt, 16)) {
            const auto off = *reinterpret_cast<std::uint32_t*>(windt + 12);
            if (off >= 16 && off < 0x40000) {
                AddSec3(out, &n, cap, windt + off);
            }
        }
    }
    for (auto rva : kWindtSec3AliasRvas) {
        void* p = nullptr;
        if (SafeReadPointer(ModuleBase() + rva, &p) && p) {
            AddSec3(out, &n, cap, static_cast<std::uint8_t*>(p));
        }
    }
    return n;
}

int ElementFromKind(char kind) {
    switch (kind) {
        case 'F':
            return 1;
        case 'W':
            return 2;
        case 'U':
            return 3;
        case 'E':
            return 4;
        default:
            return 0;
    }
}

char KindToChar(std::uint8_t k) {
    switch (k) {
        case 1:
            return 'D';
        case 2:
            return 'S';
        case 3:
            return 'M';
        case 4:
            return 'A';
        case 5:
            return 'H';
        case 6:
            return 'B';
        case 8:
            return 'F';
        case 9:
            return 'W';
        case 10:
            return 'U';
        case 11:
            return 'E';
        default:
            return 0;
    }
}

std::uint8_t CharToKind(int ch) {
    switch (ch) {
        case 'D':
            return 1;
        case 'S':
            return 2;
        case 'M':
            return 3;
        case 'A':
            return 4;
        case 'H':
            return 5;
        case 'B':
            return 6;
        case 'F':
            return 8;
        case 'W':
            return 9;
        case 'U':
            return 10;
        case 'E':
            return 11;
        default:
            return 0;
    }
}

int ElementFromFlags(int flags) {
    if (flags & 0x10) {
        return 1;
    }
    if (flags & 0x20) {
        return 2;
    }
    if (flags & 0x40) {
        return 3;
    }
    if (flags & 0x80) {
        return 4;
    }
    return 0;
}

const char* NameForSkill(int id) {
    for (unsigned i = 0; i < kMagicCatalogCount; ++i) {
        if (kMagicCatalog[i].id == static_cast<std::uint16_t>(id)) {
            return kMagicCatalog[i].name;
        }
    }
    return nullptr;
}

struct MagicTable {
    std::uint8_t* combat = nullptr;
    std::uint8_t* learn = nullptr;
};

bool CombatLooksLiveBytes(const std::uint8_t* combat) {
    if (!combat) {
        return false;
    }
    const auto* rec0 = combat;
    const auto* rec12 = combat + 12u * kCombatStride;
    return rec0[0] == 1 && rec0[1] == 1 && rec12[0] == 0x0D && rec12[1] == 0x0D;
}

bool CombatLooksLive(const std::uint8_t* combat) {
    if (!combat || !PtrReadable(combat, kCombatStride * kSkillIdCount)) {
        return false;
    }
    return CombatLooksLiveBytes(combat);
}

bool LooksLikeWindtMagic(std::uint8_t* p, MagicTable* out) {
    if (!p || !PtrReadable(p, 0x2Cu + 8u) || Read32(p) != 0x2Cu) {
        return false;
    }
    const auto o7 = Read32(p + 28);
    const auto o8 = Read32(p + 32);
    const auto o9 = Read32(p + 36);
    if (o8 < o7 || o8 - o7 != 0xC00u || o9 < o8 || o9 - o8 != 0x380u) {
        return false;
    }
    auto* combat = p + o7;
    auto* learn = p + o8;
    if (!CombatLooksLive(combat) || !PtrReadable(learn, kLearnStride * kSkillIdCount)) {
        return false;
    }
    if (out) {
        out->combat = combat;
        out->learn = learn;
    }
    return true;
}

bool LooksLikeStatMagic(std::uint8_t* p, MagicTable* out) {
    if (!p || !PtrReadable(p, 0x18u) || Read32(p) != 0x18u) {
        return false;
    }
    const auto o0 = Read32(p);
    const auto o2 = Read32(p + 8);
    const auto o3 = Read32(p + 12);
    if (o0 != 0x18u || o2 < o0 || o3 < o2 || o3 - o2 != 0x380u) {
        return false;
    }
    auto* combat = p + o0;
    auto* learn = p + o2;
    if (!CombatLooksLive(combat) || !PtrReadable(learn, kLearnStride * kSkillIdCount)) {
        return false;
    }
    if (out) {
        out->combat = combat;
        out->learn = learn;
    }
    return true;
}

void AddMagicTable(MagicTable* out, unsigned* n, unsigned cap, const MagicTable& t) {
    if (!t.combat || !out || !n) {
        return;
    }
    for (unsigned i = 0; i < *n; ++i) {
        if (out[i].combat == t.combat) {
            if (!out[i].learn && t.learn) {
                out[i].learn = t.learn;
            }
            return;
        }
        if (t.learn && out[i].learn == t.learn && out[i].combat == t.combat) {
            return;
        }
    }
    if (*n < cap) {
        out[(*n)++] = t;
    }
}

bool LearnLooksPaired(const std::uint8_t* learn) {
    if (!learn || !PtrReadable(learn, kLearnStride * kSkillIdCount)) {
        return false;
    }
    const auto* burn = learn + 12u * kLearnStride;
    return burn[0] == 8 || burn[6] != 0;
}

void TryPairLearn(MagicTable* t) {
    if (!t || !t->combat || t->learn) {
        return;
    }
    std::uint8_t* cands[] = {t->combat + 0xC00u, t->combat - 0x18u + 0x14B0u};
    for (auto* learn : cands) {
        if (LearnLooksPaired(learn)) {
            t->learn = learn;
            return;
        }
    }
}

bool TryAddCombat(MagicTable* out, unsigned* n, unsigned cap, std::uint8_t* combat) {
    if (!combat || !CombatLooksLiveBytes(combat)) {
        return false;
    }
    if (!PtrReadable(combat, kCombatStride * kSkillIdCount)) {
        return false;
    }
    MagicTable t{};
    t.combat = combat;
    TryPairLearn(&t);
    AddMagicTable(out, n, cap, t);
    return true;
}

void CollectFromMenuPtrs(MagicTable* out, unsigned* n, unsigned cap) {
    for (auto rva : kMenuCombatPtrRvas) {
        void* p = nullptr;
        if (!SafeReadPointer(ModuleBase() + rva, &p) || !p) {
            continue;
        }
        auto* combat = static_cast<std::uint8_t*>(p);
        if (!TryAddCombat(out, n, cap, combat)) {
            TryAddCombat(out, n, cap, combat - 24);
        }
    }
}

void AddWindtFromSec3(MagicTable* out, unsigned* n, unsigned cap, std::uint8_t* sec3) {
    if (!sec3) {
        return;
    }
    auto* p = sec3 - kWindtSec3Off;
    MagicTable t{};
    if (LooksLikeWindtMagic(p, &t) && p + Read32(p + 12) == sec3) {
        AddMagicTable(out, n, cap, t);
    }
}

void CollectFromWindt(MagicTable* out, unsigned* n, unsigned cap) {
    void* heap = nullptr;
    if (SafeReadPointer(ModuleBase() + kWindtHeapRva, &heap) && heap) {
        MagicTable t{};
        if (LooksLikeWindtMagic(static_cast<std::uint8_t*>(heap) + kWindtFromHeap, &t)) {
            AddMagicTable(out, n, cap, t);
        }
    }
    for (auto rva : kWindtSec3AliasRvas) {
        void* p = nullptr;
        if (SafeReadPointer(ModuleBase() + rva, &p) && p) {
            AddWindtFromSec3(out, n, cap, static_cast<std::uint8_t*>(p));
        }
    }
}

bool LooksLikeStatMagicBytes(std::uint8_t* p, const std::uint8_t* end, MagicTable* out) {
    if (!p || p + 0x18u > end || Read32(p) != 0x18u) {
        return false;
    }
    const auto o0 = Read32(p);
    const auto o2 = Read32(p + 8);
    const auto o3 = Read32(p + 12);
    if (o0 != 0x18u || o2 < o0 || o3 < o2 || o3 - o2 != 0x380u) {
        return false;
    }
    if (p + o3 > end || p + o0 + kCombatStride * kSkillIdCount > end) {
        return false;
    }
    auto* combat = p + o0;
    auto* learn = p + o2;
    if (!CombatLooksLiveBytes(combat)) {
        return false;
    }
    if (out) {
        out->combat = combat;
        out->learn = learn;
    }
    return true;
}

void TryAddStatAt(MagicTable* out, unsigned* n, unsigned cap, std::uint8_t* p) {
    MagicTable t{};
    if (LooksLikeStatMagic(p, &t)) {
        AddMagicTable(out, n, cap, t);
    }
}

void CollectFromStatRam(MagicTable* out, unsigned* n, unsigned cap) {
    void* ram = nullptr;
    if (!SafeReadPointer(ModuleBase() + kStatRamPtrRva, &ram) || !ram) {
        return;
    }
    auto* base = static_cast<std::uint8_t*>(ram);
    TryAddStatAt(out, n, cap, base + kStatInRam);
    TryAddStatAt(out, n, cap, base + kStatInRam - 0x18u);
    // B001 copies BBG+0x38A00 (strings) to ram+0x14000; STAT follows at +0x464.
    TryAddStatAt(out, n, cap, base + kStatInRam + 0x464u);
    TryAddCombat(out, n, cap, base + kStatInRam);
    TryAddCombat(out, n, cap, base + kStatInRam + 0x18u);
    TryAddCombat(out, n, cap, base + kStatInRam + 0x464u + 0x18u);
    if (PtrReadable(base + kStatInRam, 4)) {
        const auto off = Read32(base + kStatInRam);
        if (off > 0 && off < kStatRamSize) {
            TryAddStatAt(out, n, cap, base + 0x13FE8u);
            TryAddCombat(out, n, cap, base + 0x13FE8u + off);
            TryAddCombat(out, n, cap, base + off);
        } else if (off > 0x10000u) {
            TryAddCombat(out, n, cap, reinterpret_cast<std::uint8_t*>(static_cast<std::uintptr_t>(off)));
        }
    }
    auto* window = base + 0x13E00u;
    constexpr std::size_t kWindow = 0x2000u;
    if (!PtrReadable(window, kWindow)) {
        return;
    }
    for (std::size_t off = 0; off + 32u < kWindow; off += 4u) {
        if (Read32(window + off) != 0x18u) {
            continue;
        }
        MagicTable found{};
        if (LooksLikeStatMagicBytes(window + off, window + kWindow, &found)) {
            AddMagicTable(out, n, cap, found);
        }
    }
}

unsigned CollectMagicTables(MagicTable* out, unsigned cap) {
    unsigned n = 0;
    CollectFromWindt(out, &n, cap);
    CollectFromMenuPtrs(out, &n, cap);
    CollectFromStatRam(out, &n, cap);
    return n;
}

void WriteMagicRow(const MagicTable& t, int id, const MagicNative& req) {
    if (t.learn) {
        auto* lrec = t.learn + static_cast<unsigned>(id) * kLearnStride;
        if (PtrReadable(lrec, kLearnStride)) {
            lrec[6] = ClampU8(req.char_mask);
            std::memset(lrec, 0, 6);
            const auto write_n = req.req_count < 0 ? 0 : req.req_count > 3 ? 3 : req.req_count;
            unsigned slot = 0;
            for (int p = 0; p < write_n && slot < 3u; ++p) {
                const auto k = CharToKind(req.req_kind[p]);
                if (k == 0) {
                    continue;
                }
                lrec[slot * 2u] = k;
                lrec[slot * 2u + 1u] = ClampU8(req.req_level[p]);
                ++slot;
            }
        }
    }
    if (t.combat) {
        auto* crec = CombatRec(t.combat, id);
        if (crec && PtrReadable(crec, kCombatStride)) {
            Write16(crec + 2, req.cost);
            Write16(crec + 4, req.ip_cost);
            Write16(crec + 6, req.area);
            Write16(crec + 8, req.power);
            crec[12] = ClampU8(req.range);
            crec[14] = ClampU8(req.element_flags);
        }
    }
}

void FireMagicCatalog() {
    MagicTable tables[kMagicTableCap]{};
    const unsigned ntab = CollectMagicTables(tables, kMagicTableCap);
    if (ntab == 0) {
        return;
    }
    const MagicTable* src = nullptr;
    for (unsigned t = 0; t < ntab; ++t) {
        if (tables[t].learn && tables[t].combat) {
            src = &tables[t];
            break;
        }
    }
    if (!src) {
        src = &tables[0];
    }
    auto* lsrc = src->learn;
    auto* csrc = src->combat;
    if (!csrc) {
        return;
    }
    unsigned n = 0;
    int burn_cost = -1;
    int burn_pwr = -1;
    for (int id = 1; id < static_cast<int>(kSkillIdCount); ++id) {
        auto* lrec = lsrc ? lsrc + static_cast<unsigned>(id) * kLearnStride : nullptr;
        auto* crec = CombatRec(csrc, id);
        if (!crec || !PtrReadable(crec, kCombatStride)) {
            continue;
        }
        const bool have_learn = lrec && PtrReadable(lrec, kLearnStride) && (lrec[0] != 0 || lrec[6] != 0);
        bool have_combat = false;
        for (unsigned b = 0; b < kCombatStride; ++b) {
            if (crec[b] != 0) {
                have_combat = true;
                break;
            }
        }
        if (!have_learn && !have_combat) {
            continue;
        }
        MagicNative req{};
        req.id = id;
        if (have_learn) {
            req.char_mask = lrec[6];
            unsigned nreq = 0;
            for (unsigned p = 0; p < 3u; ++p) {
                const auto k = lrec[p * 2u];
                const char ch = KindToChar(k);
                if (ch == 0) {
                    break;
                }
                req.req_kind[nreq] = static_cast<std::int32_t>(ch);
                req.req_level[nreq] = lrec[p * 2u + 1u];
                if (req.element == 0) {
                    req.element = ElementFromKind(ch);
                }
                ++nreq;
            }
            req.req_count = static_cast<std::int32_t>(nreq);
        }
        req.cost = Read16(crec + 2);
        req.ip_cost = Read16(crec + 4);
        req.power = Read16(crec + 8);
        req.area = Read16(crec + 6);
        req.range = crec[12];
        req.element_flags = crec[14];
        if (req.element == 0) {
            req.element = ElementFromFlags(req.element_flags);
        }
        if (const auto* name = NameForSkill(id)) {
            const auto name_n = std::strlen(name);
            const auto cap = sizeof(req.name) - 1;
            const auto cpy = name_n < cap ? name_n : cap;
            std::memcpy(req.name, name, cpy);
        }
        if (RuntimeOnMagic(&req) != 0) {
            continue;
        }
        for (unsigned t = 0; t < ntab; ++t) {
            WriteMagicRow(tables[t], id, req);
        }
        if (id == 12) {
            auto* burn = CombatRec(tables[0].combat, 12);
            if (burn && PtrReadable(burn, kCombatStride)) {
                burn_cost = Read16(burn + 2);
                burn_pwr = Read16(burn + 8);
            }
        }
        ++n;
    }
    g_magic_fired.store(1, std::memory_order_release);
    LogInfo("OnMagic catalog %u skill(s) (%u table(s)) burn +2=%d +8=%d", n, ntab, burn_cost,
            burn_pwr);
}

void TryFireMagicPoll() {
    if (g_magic_fired.load(std::memory_order_acquire) != 0) {
        return;
    }
    MagicTable tables[kMagicTableCap]{};
    if (CollectMagicTables(tables, kMagicTableCap) == 0) {
        return;
    }
    FireMagicCatalog();
}

void ClearCatalogSell() {
    std::memset(g_catalog_sell_gold, 0, sizeof(g_catalog_sell_gold));
    std::memset(g_catalog_sell_set, 0, sizeof(g_catalog_sell_set));
}

void RememberCatalogSell(int item_id, int gold) {
    if (item_id < 1 || item_id > 511) {
        return;
    }
    if (gold < 0) {
        gold = 0;
    }
    if (gold > 0xFFFF) {
        gold = 0xFFFF;
    }
    g_catalog_sell_set[item_id] = 1;
    g_catalog_sell_gold[item_id] = static_cast<std::uint16_t>(gold);
}

int DefaultSellGold(int cost) {
    if (cost <= 0) {
        return 0;
    }
    const int half = cost / 2;
    return half > 0 ? half : 1;
}

void WriteItemRecord(std::uint8_t* rec, const ItemNative& req) {
    Write16(rec + 2, req.use_status);
    Write16(rec + 4, req.cost);
    rec[6] = ClampU8(req.icon);
    rec[7] = ClampU8(req.unknown7);
    rec[15] = ClampU8(req.para1_pre);
    rec[16] = ClampU8(req.para2);
    rec[17] = ClampU8(req.para3);
    rec[18] = ClampU8(req.para4);
    Write16(rec + 19, req.para1_post);
    Write16(rec + 21, req.para2_post);
    Write16(rec + 23, req.para3_post);
    Write16(rec + 25, req.para4_post);
}

void FireItemCatalog() {
    std::uint8_t* secs[8]{};
    const unsigned nsec = CollectSec3(secs, 8);
    if (nsec == 0) {
        LogWarn("WINDT finalize: no live sec3 yet");
        return;
    }
    ClearCatalogSell();
    unsigned n = 0;
    for (int id = 1; id <= 511; ++id) {
        const auto off = static_cast<unsigned>(id - 1) * kWindtRecSize;
        auto* rec = secs[0] + off;
        if (!PtrReadable(rec, kWindtRecSize)) {
            break;
        }
        if (Read16(rec) != static_cast<std::uint16_t>(id)) {
            continue;
        }
        ItemNative req{};
        req.id = id;
        req.use_status = Read16(rec + 2);
        req.cost = Read16(rec + 4);
        req.icon = rec[6];
        req.unknown7 = rec[7];
        req.para1_pre = rec[15];
        req.para2 = rec[16];
        req.para3 = rec[17];
        req.para4 = rec[18];
        req.para1_post = Read16(rec + 19);
        req.para2_post = Read16(rec + 21);
        req.para3_post = Read16(rec + 23);
        req.para4_post = Read16(rec + 25);
        req.sell_price = DefaultSellGold(req.cost);
        if (RuntimeOnItem(&req) != 0) {
            continue;
        }
        RememberCatalogSell(id, req.sell_price);
        for (unsigned s = 0; s < nsec; ++s) {
            auto* dest = secs[s] + off;
            if (PtrReadable(dest, kWindtRecSize)) {
                WriteItemRecord(dest, req);
            }
        }
        ++n;
    }
    g_items_fired.store(1, std::memory_order_release);
    LogInfo("OnItem catalog %u item(s)", n);
}

void TryFireItemsPoll() {
    if (g_windt_finalize_hooked || g_items_fired.load(std::memory_order_acquire) != 0) {
        return;
    }
    std::uint8_t* secs[8]{};
    if (CollectSec3(secs, 8) == 0) {
        return;
    }
    int expected = 0;
    if (!g_items_fired.compare_exchange_strong(expected, 1, std::memory_order_acq_rel)) {
        return;
    }
    FireItemCatalog();
}

void FillCharacter(std::uint8_t* map_obj, int char_id, CharacterNative* req) {
    auto* blk = CharBlock(map_obj, char_id);
    if (!blk || !req) {
        return;
    }
    req->id = char_id;
    req->level = blk[kOffLevel];
    req->max_hp = Read16(blk + kOffHpMax);
    req->hp = Read16(blk + kOffHpCur);
    req->str = Read16(blk + kOffStr);
    req->vit = Read16(blk + kOffVit);
    req->wit = Read16(blk + kOffWit);
    req->agi = Read16(blk + kOffAgi);
    req->max_sp = Read16(blk + kOffSpMax);
    req->sp = Read16(blk + kOffSpCur);
    req->fire = blk[kOffMagicLv + 0];
    req->water = blk[kOffMagicLv + 1];
    req->wind = blk[kOffMagicLv + 2];
    req->earth = blk[kOffMagicLv + 3];
    std::uint32_t exp = 0;
    std::memcpy(&exp, blk + kOffExp, 4);
    req->exp = static_cast<std::int32_t>(exp);
    req->mp1 = blk[kOffMp1Cur + 1];
    req->mp2 = blk[kOffMp1Cur + 3];
    req->mp3 = blk[kOffMp1Cur + 5];
    for (int i = 0; i < 4; ++i) {
        req->weapon_level[i] = blk[kOffWeaponLv + static_cast<std::uintptr_t>(i)];
        req->weapon_type[i] = blk[kOffWeaponType + static_cast<std::uintptr_t>(i)];
    }
    req->learned_count = 0;
    const auto bit = static_cast<std::uint8_t>(1u << (char_id - 1));
    for (int sid = 1; sid <= kSkillIdMax; ++sid) {
        auto* byte = map_obj + kSkillBitBaseOff + static_cast<std::uintptr_t>(sid);
        if (!PtrReadable(byte, 1)) {
            break;
        }
        if ((*byte & bit) != 0 && req->learned_count < static_cast<int>(kCharacterLearnedMax)) {
            req->learned[req->learned_count++] = sid;
        }
    }
}

void WriteCharacter(std::uint8_t* map_obj, int char_id, const CharacterNative& req) {
    auto* blk = CharBlock(map_obj, char_id);
    if (!blk) {
        return;
    }
    blk[kOffLevel] = ClampU8(req.level);
    Write16(blk + kOffHpMax, req.max_hp);
    Write16(blk + kOffHpCur, req.hp);
    Write16(blk + kOffStr, req.str);
    Write16(blk + kOffVit, req.vit);
    Write16(blk + kOffWit, req.wit);
    Write16(blk + kOffAgi, req.agi);
    Write16(blk + kOffSpMax, req.max_sp);
    Write16(blk + kOffSpCur, req.sp);
    blk[kOffMagicLv + 0] = ClampU8(req.fire);
    blk[kOffMagicLv + 1] = ClampU8(req.water);
    blk[kOffMagicLv + 2] = ClampU8(req.wind);
    blk[kOffMagicLv + 3] = ClampU8(req.earth);
    Write32(blk + kOffExp, req.exp);
    const auto mp1 = ClampU8(req.mp1);
    const auto mp2 = ClampU8(req.mp2);
    const auto mp3 = ClampU8(req.mp3);
    blk[kOffMp1Cur + 0] = mp1;
    blk[kOffMp1Cur + 1] = mp1;
    blk[kOffMp1Cur + 2] = mp2;
    blk[kOffMp1Cur + 3] = mp2;
    blk[kOffMp1Cur + 4] = mp3;
    blk[kOffMp1Cur + 5] = mp3;
    for (int i = 0; i < 4; ++i) {
        blk[kOffWeaponLv + static_cast<std::uintptr_t>(i)] = ClampU8(req.weapon_level[i]);
        auto wt = req.weapon_type[i];
        if (wt < 0) {
            wt = 0;
        }
        if (wt > 6) {
            wt = 6;
        }
        blk[kOffWeaponType + static_cast<std::uintptr_t>(i)] = static_cast<std::uint8_t>(wt);
    }
    const auto bit = static_cast<std::uint8_t>(1u << (char_id - 1));
    std::uint8_t want[kSkillIdMax + 1]{};
    const auto n = req.learned_count < 0 ? 0
                                         : req.learned_count > static_cast<int>(kCharacterLearnedMax)
                                               ? static_cast<int>(kCharacterLearnedMax)
                                               : req.learned_count;
    for (int i = 0; i < n; ++i) {
        const int sid = req.learned[i];
        if (sid >= 1 && sid <= kSkillIdMax) {
            want[sid] = 1;
        }
    }
    for (int sid = 1; sid <= kSkillIdMax; ++sid) {
        auto* byte = map_obj + kSkillBitBaseOff + static_cast<std::uintptr_t>(sid);
        if (!PtrReadable(byte, 1)) {
            break;
        }
        if (want[sid]) {
            *byte = static_cast<std::uint8_t>(*byte | bit);
        } else {
            *byte = static_cast<std::uint8_t>(*byte & static_cast<std::uint8_t>(~bit));
        }
    }
}

bool RaiseAllCharacters() {
    auto* map_obj = MapObject();
    auto* justin = CharBlock(map_obj, 1);
    if (!map_obj || !justin || !PtrReadable(justin, kCharStride)) {
        return false;
    }
    unsigned n = 0;
    for (int id = 1; id <= static_cast<int>(kCharCount); ++id) {
        auto* blk = CharBlock(map_obj, id);
        if (!blk || !PtrReadable(blk, kCharStride)) {
            continue;
        }
        CharacterNative req{};
        FillCharacter(map_obj, id, &req);
        if (RuntimeOnCharacter(&req) != 0) {
            continue;
        }
        WriteCharacter(map_obj, id, req);
        ++n;
    }
    g_chars_fired.store(1, std::memory_order_release);
    LogInfo("OnCharacter catalog %u char(s)", n);
    return true;
}

void TryFireCharacters() {
    if (g_chars_fired.load(std::memory_order_acquire) != 0) {
        return;
    }
    auto* map_obj = MapObject();
    auto* justin = CharBlock(map_obj, 1);
    if (!justin || !PtrReadable(justin, kCharStride)) {
        return;
    }
    if (justin[kOffLevel] == 0) {
        g_saw_justin_zero.store(1, std::memory_order_release);
        return;
    }
    const bool load_pending = g_load_pending.load(std::memory_order_acquire) != 0;
    const bool saw_zero = g_saw_justin_zero.load(std::memory_order_acquire) != 0;
    const bool leftover_at_title = g_had_party_at_title.load(std::memory_order_acquire) != 0;
    if (!load_pending && !saw_zero && leftover_at_title) {
        return;
    }
    int expected = 0;
    if (!g_chars_busy.compare_exchange_strong(expected, 1, std::memory_order_acq_rel)) {
        return;
    }
    if (g_chars_fired.load(std::memory_order_acquire) == 0) {
        if (RaiseAllCharacters()) {
            g_load_pending.store(0, std::memory_order_release);
        }
    }
    g_chars_busy.store(0, std::memory_order_release);
}

void OnWindtItemsReady() {
    FireItemCatalog();
    TryFireCharacters();
}

void OnWindtMagicReady() {
    FireMagicCatalog();
}

void OnWindtSec3Ready() {
    OnWindtItemsReady();
    OnWindtMagicReady();
}

}  // namespace

void NotifyWindtReady() {
    OnWindtSec3Ready();
}

void NotifyWindtItemsReady() {
    OnWindtItemsReady();
}

void NotifyWindtMagicReady() {
    OnWindtMagicReady();
}

bool TryCatalogSellGold(int item_id, int* gold) {
    if (!gold || item_id < 1 || item_id > 511 || g_catalog_sell_set[item_id] == 0) {
        return false;
    }
    *gold = g_catalog_sell_gold[item_id];
    return true;
}

#if defined(_M_IX86)
}  // namespace grandia_mod

extern "C" void ModOnWindtReady() {
    grandia_mod::NotifyWindtReady();
}

extern "C" void ModOnWindtReadyItems() {
    grandia_mod::NotifyWindtItemsReady();
}

extern "C" void ModOnWindtReadyMagic() {
    grandia_mod::NotifyWindtMagicReady();
}

extern "C" __declspec(naked) void ModWindtFinalize1() {
    __asm {
        pushad
        call ModOnWindtReadyItems
        popad
        call dword ptr [g_mod_windt_fin1]
        pushad
        call ModOnWindtReadyMagic
        popad
        ret
    }
}

extern "C" __declspec(naked) void ModWindtFinalize2() {
    __asm {
        pushad
        call ModOnWindtReadyItems
        popad
        call dword ptr [g_mod_windt_fin2]
        pushad
        call ModOnWindtReadyMagic
        popad
        ret
    }
}

extern "C" __declspec(naked) void ModWindtFinalize3() {
    __asm {
        pushad
        call ModOnWindtReadyItems
        popad
        call dword ptr [g_mod_windt_fin3]
        pushad
        call ModOnWindtReadyMagic
        popad
        ret
    }
}

namespace grandia_mod {
#endif

bool InstallCatalogHooks() {
#if defined(_M_IX86)
    const auto base = ModuleBase();
    if (base != 0) {
        void* detours[3] = {reinterpret_cast<void*>(&ModWindtFinalize1),
                            reinterpret_cast<void*>(&ModWindtFinalize2),
                            reinterpret_cast<void*>(&ModWindtFinalize3)};
        void** orig[3] = {&g_mod_windt_fin1, &g_mod_windt_fin2, &g_mod_windt_fin3};
        unsigned ok = 0;
        for (unsigned i = 0; i < 3; ++i) {
            auto* site = reinterpret_cast<std::uint8_t*>(base + kWindtFinalizeCallRvas[i]);
            if (!IsExecutableAddress(site) || site[0] != 0xE8) {
                LogWarn("WINDT finalize call mismatch at +0x%X",
                        static_cast<unsigned>(kWindtFinalizeCallRvas[i]));
                continue;
            }
            *orig[i] = CallTarget(site);
            if (!WriteCall(site, detours[i], g_fin_original[i])) {
                LogWarn("WINDT finalize hook failed at +0x%X",
                        static_cast<unsigned>(kWindtFinalizeCallRvas[i]));
                *orig[i] = nullptr;
                continue;
            }
            g_fin_site[i] = site;
            ++ok;
        }
        g_windt_finalize_hooked = ok > 0;
        if (ok > 0) {
            LogInfo("OnItem / OnMagic at WINDT finalize (%u/3 sites)", ok);
        }
    }
#endif
    LogInfo("OnCharacter: after load copy / new-game party (Justin level > 0)");
    LogInfo("OnMagic: WINDT sec7/sec8 (menus) + STAT/BBG (battle); Cost is MP/SP at combat row id-1");
    return true;
}

void RemoveCatalogHooks() {
    for (unsigned i = 0; i < 3; ++i) {
        if (g_fin_site[i]) {
            RestoreBytes(g_fin_site[i], g_fin_original[i], 5);
            g_fin_site[i] = nullptr;
        }
    }
    g_windt_finalize_hooked = false;
    ClearCatalogSell();
#if defined(_M_IX86)
    g_mod_windt_fin1 = nullptr;
    g_mod_windt_fin2 = nullptr;
    g_mod_windt_fin3 = nullptr;
#endif
}

void PollCatalog() {
    TryFireItemsPoll();
    TryFireCharacters();
    TryFireMagicPoll();
}

void ResetCharacterSession() {
    auto* map_obj = MapObject();
    auto* justin = CharBlock(map_obj, 1);
    const bool leftover = justin && PtrReadable(justin, kCharStride) && justin[kOffLevel] != 0;
    g_had_party_at_title.store(leftover ? 1 : 0, std::memory_order_release);
    g_saw_justin_zero.store(0, std::memory_order_release);
    g_load_pending.store(0, std::memory_order_release);
    g_chars_fired.store(0, std::memory_order_release);
    g_magic_fired.store(0, std::memory_order_release);
    g_magic_spawn_done.store(0, std::memory_order_release);
}

void RaiseCharactersFromLoad() {
    g_load_pending.store(1, std::memory_order_release);
    g_chars_fired.store(0, std::memory_order_release);
}

void RaiseMagicCatalog() {
    g_magic_spawn_done.store(0, std::memory_order_release);
    FireMagicCatalog();
}

void RaiseMagicCatalogOnSpawn() {
    FireMagicCatalog();
}

}  // namespace grandia_mod
