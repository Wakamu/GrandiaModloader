#include "party_pdat.h"

#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kBattleCtxPtrRva = 0x2D1A98u;
constexpr unsigned kMaxCharId = 8u;

struct PdatDonorPack {
    std::uint8_t count;
    std::uint8_t ids[4];
    std::uint16_t w0;
    std::uint16_t w2;
    std::uint16_t w6;
};

// 1 Justin, 2 Feena, 3 Sue, 4 Gadwin, 5 Rapp, 6 Milda, 7 Guido, 8 Liete.
constexpr PdatDonorPack kPdatDonors[] = {
    {2, {1, 3, 0, 0}, 30, 38, 8},    // Justin+Sue
    {3, {1, 3, 2, 0}, 76, 54, 11},   // Justin+Sue+Feena
    {2, {1, 2, 0, 0}, 141, 42, 7},   // Justin+Feena
    {4, {1, 3, 2, 4}, 255, 64, 28},  // +Gadwin
    {3, {1, 2, 4, 0}, 408, 69, 9},   // Justin+Feena+Gadwin
    {3, {1, 2, 5, 0}, 486, 53, 10},  // Justin+Feena+Rapp
    {4, {1, 2, 5, 6}, 549, 73, 12},  // +Milda
    {4, {1, 2, 5, 7}, 664, 66, 12},  // +Guido
    {4, {1, 2, 5, 8}, 833, 71, 12},  // +Liete
};
constexpr int kPdatDonorCount = sizeof(kPdatDonors) / sizeof(kPdatDonors[0]);

#pragma pack(push, 1)
struct Gpd1FileHeader {
    char magic[4];
    std::uint32_t version;
    std::uint32_t count;
    std::uint32_t reserved;
};
struct Gpd1Entry {
    std::uint8_t char_id;
    std::uint8_t variant;
    std::uint8_t flags;
    std::uint8_t pad;
    std::uint32_t hdr[4];
    std::uint32_t blob_size;
    std::uint32_t blob_off;
};
#pragma pack(pop)

constexpr std::uint32_t kGpd1Version = 1u;
constexpr std::uint8_t kGpd1FlagPreferred = 1u;

bool g_pack_rebuilt = false;
void* g_pdat_slot_alloc[4]{};
std::uint32_t g_pdat_slot_alloc_size[4]{};
void* g_e6a0_alloc[4]{};
std::uint32_t g_e6a0_alloc_size[4]{};
bool g_pdat_alloc_is_contiguous = false;

const char* CharName(std::uint8_t id) {
    switch (id) {
        case 1:
            return "Justin";
        case 2:
            return "Feena";
        case 3:
            return "Sue";
        case 4:
            return "Gadwin";
        case 5:
            return "Rapp";
        case 6:
            return "Milda";
        case 7:
            return "Guido";
        case 8:
            return "Liete";
        default:
            return "?";
    }
}

std::string Dirname(const std::string& path) {
    const auto slash = path.find_last_of("\\/");
    if (slash == std::string::npos) {
        return ".";
    }
    return path.substr(0, slash);
}

std::string DllDirectory() {
    char buf[MAX_PATH]{};
    HMODULE self = nullptr;
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            reinterpret_cast<LPCSTR>(&DllDirectory), &self)) {
        return {};
    }
    if (!GetModuleFileNameA(self, buf, MAX_PATH)) {
        return {};
    }
    return Dirname(buf);
}

bool FileExists(const char* path) {
    const DWORD attrs = GetFileAttributesA(path);
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

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
    const auto start = static_cast<const std::uint8_t*>(mbi.BaseAddress);
    const auto end = start + mbi.RegionSize;
    const auto ptr = static_cast<const std::uint8_t*>(p);
    return ptr >= start && ptr + bytes <= end;
}

bool ReadFileAll(const char* path, std::vector<std::uint8_t>* out) {
    HANDLE h = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                           FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) {
        return false;
    }
    LARGE_INTEGER li{};
    if (!GetFileSizeEx(h, &li) || li.QuadPart <= 0 || li.QuadPart > 0x4000000) {
        CloseHandle(h);
        return false;
    }
    out->resize(static_cast<std::size_t>(li.QuadPart));
    DWORD got = 0;
    const BOOL ok = ReadFile(h, out->data(), static_cast<DWORD>(out->size()), &got, nullptr);
    CloseHandle(h);
    return ok && got == out->size();
}

bool ResolvePdatPath(char* out, std::size_t out_size) {
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    char exe_path[MAX_PATH]{};
    if (!GetModuleFileNameA(reinterpret_cast<HMODULE>(base), exe_path, MAX_PATH)) {
        return false;
    }
    const std::string exe_dir = Dirname(exe_path);
    const char* rels[] = {
        "\\content\\BATLE\\P_DAT.BIN",
        "\\content\\batle\\P_DAT.BIN",
        "\\content\\BATLE\\p_dat.bin",
    };
    for (const char* rel : rels) {
        if (std::snprintf(out, out_size, "%s%s", exe_dir.c_str(), rel) <= 0) {
            continue;
        }
        if (FileExists(out)) {
            return true;
        }
    }
    return false;
}

bool ResolveCharpackPath(char* out, std::size_t out_size) {
    const std::string dll_dir = DllDirectory();
    if (!dll_dir.empty()) {
        if (std::snprintf(out, out_size, "%s\\pdat_charpack.bin", dll_dir.c_str()) > 0 &&
            FileExists(out)) {
            return true;
        }
    }
    const auto base = ModuleBase();
    if (base != 0) {
        char exe_path[MAX_PATH]{};
        if (GetModuleFileNameA(reinterpret_cast<HMODULE>(base), exe_path, MAX_PATH)) {
            const std::string exe_dir = Dirname(exe_path);
            const char* rels[] = {
                "\\pdat_charpack.bin",
                "\\content\\BATLE\\pdat_charpack.bin",
                "\\content\\batle\\pdat_charpack.bin",
            };
            for (const char* rel : rels) {
                if (std::snprintf(out, out_size, "%s%s", exe_dir.c_str(), rel) > 0 &&
                    FileExists(out)) {
                    return true;
                }
            }
        }
    }
    return false;
}

const std::vector<std::uint8_t>* GetCachedPdat() {
    static std::vector<std::uint8_t> cached;
    static int state = 0;
    if (state == 0) {
        char pdat_path[MAX_PATH]{};
        if (!ResolvePdatPath(pdat_path, sizeof(pdat_path)) || !ReadFileAll(pdat_path, &cached)) {
            cached.clear();
            state = -1;
            LogWarn("Party P_DAT.BIN not found next to grandia.exe");
            return nullptr;
        }
        state = 1;

    }
    return state == 1 ? &cached : nullptr;
}

const std::vector<std::uint8_t>* GetCachedCharpack() {
    static std::vector<std::uint8_t> cached;
    static int state = 0;
    if (state == 0) {
        char path[MAX_PATH]{};
        if (!ResolveCharpackPath(path, sizeof(path)) || !ReadFileAll(path, &cached) ||
            cached.size() < sizeof(Gpd1FileHeader)) {
            cached.clear();
            state = -1;
            return nullptr;
        }
        const auto* hdr = reinterpret_cast<const Gpd1FileHeader*>(cached.data());
        if (std::memcmp(hdr->magic, "GPD1", 4) != 0 || hdr->version != kGpd1Version) {
            cached.clear();
            state = -1;
            LogWarn("Party charpack: bad header");
            return nullptr;
        }
        const std::uint32_t need =
            static_cast<std::uint32_t>(sizeof(Gpd1FileHeader) + hdr->count * sizeof(Gpd1Entry));
        if (cached.size() < need) {
            cached.clear();
            state = -1;
            LogWarn("Party charpack: truncated index");
            return nullptr;
        }
        state = 1;
    }
    return state == 1 ? &cached : nullptr;
}

const Gpd1Entry* FindCharpackEntry(const std::vector<std::uint8_t>& pack, std::uint8_t char_id) {
    if (pack.size() < sizeof(Gpd1FileHeader)) {
        return nullptr;
    }
    const auto* hdr = reinterpret_cast<const Gpd1FileHeader*>(pack.data());
    const auto* ents = reinterpret_cast<const Gpd1Entry*>(pack.data() + sizeof(Gpd1FileHeader));
    const Gpd1Entry* fallback = nullptr;
    for (std::uint32_t i = 0; i < hdr->count; ++i) {
        if (ents[i].char_id != char_id) {
            continue;
        }
        if (static_cast<std::size_t>(ents[i].blob_off) + ents[i].blob_size > pack.size()) {
            continue;
        }
        if ((ents[i].flags & kGpd1FlagPreferred) != 0 || ents[i].variant == 0) {
            return &ents[i];
        }
        if (!fallback) {
            fallback = &ents[i];
        }
    }
    return fallback;
}

const PdatDonorPack* FindPdatDonorForChar(std::uint8_t char_id, std::uint8_t* out_index) {
    const PdatDonorPack* best = nullptr;
    std::uint8_t best_index = 0;
    std::uint32_t best_score = 0xFFFFFFFFu;
    for (int i = 0; i < kPdatDonorCount; ++i) {
        const auto& d = kPdatDonors[i];
        for (std::uint8_t s = 0; s < d.count; ++s) {
            if (d.ids[s] != char_id) {
                continue;
            }
            const std::uint32_t pack_bytes =
                static_cast<std::uint32_t>(d.w2 + d.w6) * 0x800u;
            const bool is_last = (s + 1u) >= d.count;
            const std::uint32_t score = pack_bytes + (is_last ? 0x01000000u : 0u);
            if (score < best_score) {
                best_score = score;
                best = &d;
                best_index = s;
            }
        }
    }
    if (best && out_index) {
        *out_index = best_index;
    }
    return best;
}

std::uint32_t PdatAnimTableReach(const std::uint8_t* pack, std::uint32_t pack_size,
                                 const std::uint32_t* hdr) {
    if (!pack || !hdr || hdr[3] == 0 || hdr[3] + 0x40u > pack_size) {
        return 0;
    }
    const std::uint32_t table_off = hdr[3];
    std::uint32_t reach = table_off + 0x100u;
    for (unsigned i = 0; i < 64u; ++i) {
        const std::uint32_t ent_off = table_off + i * 4u;
        if (ent_off + 4u > pack_size) {
            break;
        }
        const std::uint32_t entry = *reinterpret_cast<const std::uint32_t*>(pack + ent_off);
        if (entry != 0 && entry < 0x8000u) {
            const std::uint32_t end = table_off + entry + 0x200u;
            if (end > reach) {
                reach = end;
            }
        }
    }
    return reach > pack_size ? pack_size : reach;
}

bool ExtractPdatCharSpan(const std::uint8_t* pack, std::uint32_t pack_size, std::uint8_t index,
                         std::uint8_t donor_char_count, std::uint32_t* out_start,
                         std::uint32_t* out_end, const std::uint32_t** out_hdr) {
    if (!pack || !out_start || !out_end || index >= 4u || donor_char_count == 0 ||
        index >= donor_char_count) {
        return false;
    }
    const std::uint32_t hdr_off = static_cast<std::uint32_t>(index) * 0x10u;
    if (hdr_off + 0x10u > pack_size) {
        return false;
    }
    const auto* hdr = reinterpret_cast<const std::uint32_t*>(pack + hdr_off);
    std::uint32_t data_start = hdr[0];
    std::uint32_t data_end = hdr[0];
    for (int i = 1; i < 4; ++i) {
        if (hdr[i] < data_start) {
            data_start = hdr[i];
        }
        if (hdr[i] > data_end) {
            data_end = hdr[i];
        }
    }
    constexpr std::uint32_t kAnimSpillCap = 0x9000u;
    std::uint32_t boundary = pack_size;
    const std::uint32_t next_off = (static_cast<std::uint32_t>(index) + 1u) * 0x10u;
    if (index + 1u < donor_char_count && next_off + 0x10u <= pack_size) {
        const auto* next_hdr = reinterpret_cast<const std::uint32_t*>(pack + next_off);
        if (next_hdr[0] > data_start && next_hdr[0] <= pack_size) {
            boundary = next_hdr[0];
        }
    } else if (index + 1u >= donor_char_count && next_off + 0x10u <= pack_size) {
        const auto* next_hdr = reinterpret_cast<const std::uint32_t*>(pack + next_off);
        if (next_hdr[0] == next_hdr[1] && next_hdr[1] == next_hdr[2] && next_hdr[2] == next_hdr[3] &&
            next_hdr[0] >= data_end && next_hdr[0] <= pack_size) {
            boundary = next_hdr[0];
        }
    }
    if (boundary > data_end) {
        data_end = boundary;
    }
    const std::uint32_t anim_reach = PdatAnimTableReach(pack, pack_size, hdr);
    if (anim_reach > data_end) {
        const std::uint32_t capped = boundary + kAnimSpillCap;
        data_end = anim_reach < capped ? anim_reach : capped;
        if (data_end > pack_size) {
            data_end = pack_size;
        }
    }
    if (data_end <= data_start || data_end > pack_size) {
        return false;
    }
    *out_start = data_start;
    *out_end = data_end;
    if (out_hdr) {
        *out_hdr = hdr;
    }
    return true;
}

void FreePdatSlotAllocs() {
    for (int i = 0; i < 4; ++i) {
        if (g_pdat_slot_alloc[i]) {
            VirtualFree(g_pdat_slot_alloc[i], 0, MEM_RELEASE);
            g_pdat_slot_alloc[i] = nullptr;
            g_pdat_slot_alloc_size[i] = 0;
        }
        if (g_e6a0_alloc[i]) {
            VirtualFree(g_e6a0_alloc[i], 0, MEM_RELEASE);
            g_e6a0_alloc[i] = nullptr;
            g_e6a0_alloc_size[i] = 0;
        }
    }
    g_pdat_alloc_is_contiguous = false;
}

bool TryInstallContiguousCharpack(std::uint8_t* c8a, std::uint32_t c8a_abs, unsigned n,
                                  const std::uint8_t* ids, const char* tag) {
    (void)tag;
    const std::vector<std::uint8_t>* charpack = GetCachedCharpack();
    if (!charpack || n == 0 || n > 4) {
        return false;
    }

    const Gpd1Entry* ents[4]{};
    for (unsigned slot = 0; slot < n; ++slot) {
        const std::uint8_t char_id = ids[slot];
        if (char_id < 1u || char_id > kMaxCharId) {
            return false;
        }
        ents[slot] = FindCharpackEntry(*charpack, char_id);
        if (!ents[slot]) {
            return false;
        }
    }

    const std::uint32_t header_bytes = static_cast<std::uint32_t>(n + (n < 4u ? 1u : 0u)) * 0x10u;
    std::uint32_t body = 0;
    for (unsigned slot = 0; slot < n; ++slot) {
        body += (ents[slot]->blob_size + 3u) & ~3u;
    }
    const std::uint32_t pack_size = (header_bytes + body + 0xFFu) & ~0xFFu;
    void* mem = VirtualAlloc(nullptr, pack_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!mem) {
        LogWarn("Party P_DAT contiguous: VirtualAlloc failed size=%u", pack_size);
        return false;
    }
    std::memset(mem, 0, pack_size);
    auto* pack = static_cast<std::uint8_t*>(mem);
    std::uint32_t cursor = header_bytes;

    for (unsigned slot = 0; slot < n; ++slot) {
        const Gpd1Entry* ent = ents[slot];
        auto* ph = reinterpret_cast<std::uint32_t*>(pack + static_cast<std::uint32_t>(slot) * 0x10u);
        for (int i = 0; i < 4; ++i) {
            ph[i] = cursor + ent->hdr[i];
        }
        std::memcpy(pack + cursor, charpack->data() + ent->blob_off, ent->blob_size);
        cursor += (ent->blob_size + 3u) & ~3u;
    }
    if (n < 4u) {
        auto* term = reinterpret_cast<std::uint32_t*>(pack + static_cast<std::uint32_t>(n) * 0x10u);
        term[0] = term[1] = term[2] = term[3] = cursor;
    }

    FreePdatSlotAllocs();
    g_pdat_slot_alloc[0] = mem;
    g_pdat_slot_alloc_size[0] = pack_size;
    g_pdat_alloc_is_contiguous = true;

    const std::uint32_t base_rel =
        static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(mem)) - c8a_abs;
    std::memset(c8a, 0, 0x40u);
    for (unsigned slot = 0; slot < n; ++slot) {
        const auto* ph =
            reinterpret_cast<const std::uint32_t*>(pack + static_cast<std::uint32_t>(slot) * 0x10u);
        auto* dst = reinterpret_cast<std::uint32_t*>(c8a + static_cast<std::uint32_t>(slot) * 0x10u);
        for (int i = 0; i < 4; ++i) {
            dst[i] = base_rel + ph[i];
        }
    }
    if (n < 4u) {
        auto* term = reinterpret_cast<std::uint32_t*>(c8a + static_cast<std::uint32_t>(n) * 0x10u);
        const std::uint32_t end_rel = base_rel + cursor;
        term[0] = term[1] = term[2] = term[3] = end_rel;
    }

    g_pack_rebuilt = true;

    return true;
}

}  // namespace

void ResetPdatBattlePack() {
    FreePdatSlotAllocs();
    g_pack_rebuilt = false;
}

bool PdatPackRebuilt() {
    return g_pack_rebuilt;
}

void EnsureKeyedSlotBuffers(unsigned n) {
    if (n <= 1 || n > 4) {
        return;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    void* ctxp = nullptr;
    if (!SafeReadPointer(base + kBattleCtxPtrRva, &ctxp) || !ctxp) {
        return;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    if (!PtrReadable(ctx + 0xE6A0u, 0x10u) || !PtrReadable(ctx + 0xEC4Cu, 0x10u)) {
        return;
    }
    auto* e6a0 = reinterpret_cast<std::uint32_t*>(ctx + 0xE6A0u);
    auto* ec4c = reinterpret_cast<std::uint32_t*>(ctx + 0xEC4Cu);
    std::uint32_t buf_size = ec4c[0];
    if (buf_size < 0x1000u || buf_size > 0x200000u) {
        buf_size = 0x11000u;
    }
    for (unsigned slot = 1; slot < n; ++slot) {
        if (e6a0[slot] != 0) {
            continue;
        }
        void* mem = VirtualAlloc(nullptr, buf_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (!mem) {
            LogWarn("Party keyed buf: VirtualAlloc failed slot=%u", slot);
            return;
        }
        std::memset(mem, 0, buf_size);
        e6a0[slot] = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(mem));
        ec4c[slot] = buf_size;
        g_e6a0_alloc[slot] = mem;
        g_e6a0_alloc_size[slot] = buf_size;
    }
}

void FixupAllyAnimPointers(void* combatant, unsigned c8a_slot) {
    if (!g_pack_rebuilt || !combatant || !PtrReadable(combatant, 0x170u)) {
        return;
    }
    const auto base = ModuleBase();
    void* ctxp = nullptr;
    if (!SafeReadPointer(base + kBattleCtxPtrRva, &ctxp) || !ctxp) {
        return;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    auto* actor = static_cast<std::uint8_t*>(combatant);
    if (!PtrReadable(ctx + 0xC8A00u, 0x40u)) {
        return;
    }
    unsigned slot = c8a_slot;
    if (slot >= 4u) {
        const std::uint8_t slot1 = actor[0x15A];
        if (slot1 >= 1u && slot1 <= 4u) {
            slot = static_cast<unsigned>(slot1 - 1u);
        }
    }
    if (slot >= 4u) {
        LogWarn("Party anim fixup: no slot char=%u a15a=%u", actor[0x10F], actor[0x15A]);
        return;
    }
    auto* hdr = reinterpret_cast<std::uint32_t*>(ctx + 0xC8A00u + slot * 0x10u);
    if (hdr[0] == 0 || hdr[3] == 0) {
        LogWarn("Party anim fixup: empty hdr slot=%u char=%u", slot, actor[0x10F]);
        return;
    }
    const std::uint32_t c8a_abs =
        static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(ctx)) + 0xC8A00u;
    const std::uint32_t p98 = c8a_abs + hdr[1];
    const std::uint32_t p9c = c8a_abs + hdr[2];
    const std::uint32_t p164 = c8a_abs + hdr[3];
    if (!PtrReadable(reinterpret_cast<void*>(static_cast<std::uintptr_t>(p98)), 2) ||
        !PtrReadable(reinterpret_cast<void*>(static_cast<std::uintptr_t>(p164)), 8)) {
        LogWarn("Party anim fixup: +0x98=%08X +0x164=%08X not readable slot=%u", p98, p164, slot);
        return;
    }
    *reinterpret_cast<std::uint32_t*>(actor + 0x98) = p98;
    *reinterpret_cast<std::uint32_t*>(actor + 0x9C) = p9c;
    *reinterpret_cast<std::uint32_t*>(actor + 0x164) = p164;
}

void TrySplicePdatPlayables(const std::uint8_t* ids, unsigned n, const char* tag) {
    if (g_pack_rebuilt || !ids || n == 0 || n > 4) {
        return;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    void* ctxp = nullptr;
    if (!SafeReadPointer(base + kBattleCtxPtrRva, &ctxp) || !ctxp) {
        LogWarn("Party P_DAT [%s]: battle ctx missing", tag ? tag : "?");
        return;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    if (!PtrReadable(ctx + 0xC8A00u, 0x40u)) {
        LogWarn("Party P_DAT [%s]: c8a pack not readable", tag ? tag : "?");
        return;
    }

    std::uint8_t* c8a = ctx + 0xC8A00u;
    const std::uint32_t c8a_abs = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(c8a));

    if (TryInstallContiguousCharpack(c8a, c8a_abs, n, ids, tag)) {
        return;
    }

    const std::vector<std::uint8_t>* charpack = GetCachedCharpack();
    const std::vector<std::uint8_t>* pdat = GetCachedPdat();
    if (!charpack && !pdat) {
        LogWarn("Party P_DAT rebuild: no charpack and P_DAT.BIN missing");
        return;
    }

    FreePdatSlotAllocs();
    g_pack_rebuilt = true;
    std::memset(c8a, 0, 0x40u);
    int built = 0;

    for (unsigned slot = 0; slot < n; ++slot) {
        const std::uint8_t char_id = ids[slot];
        if (char_id < 1u || char_id > kMaxCharId) {
            continue;
        }

        const std::uint8_t* src = nullptr;
        std::uint32_t span = 0;
        std::uint32_t hdr_rel[4]{};

        if (charpack) {
            const Gpd1Entry* ent = FindCharpackEntry(*charpack, char_id);
            if (ent) {
                src = charpack->data() + ent->blob_off;
                span = ent->blob_size;
                for (int i = 0; i < 4; ++i) {
                    hdr_rel[i] = ent->hdr[i];
                }
            }
        }
        if (!src && pdat) {
            std::uint8_t donor_index = 0;
            const PdatDonorPack* donor = FindPdatDonorForChar(char_id, &donor_index);
            if (!donor) {
                LogWarn("Party P_DAT rebuild: no donor for char=%u (%s)", char_id, CharName(char_id));
                continue;
            }
            const std::uint32_t pack_off = static_cast<std::uint32_t>(donor->w0) * 0x800u;
            const std::uint32_t pack_size =
                static_cast<std::uint32_t>(donor->w2 + donor->w6) * 0x800u;
            if (pack_off + pack_size > pdat->size()) {
                LogWarn("Party P_DAT rebuild: donor pack OOB char=%u", char_id);
                continue;
            }
            const std::uint8_t* pack = pdat->data() + pack_off;
            std::uint32_t data_start = 0;
            std::uint32_t data_end = 0;
            const std::uint32_t* hdr = nullptr;
            if (!ExtractPdatCharSpan(pack, pack_size, donor_index, donor->count, &data_start,
                                     &data_end, &hdr)) {
                LogWarn("Party P_DAT rebuild: bad span char=%u", char_id);
                continue;
            }
            src = pack + data_start;
            span = data_end - data_start;
            for (int i = 0; i < 4; ++i) {
                hdr_rel[i] = hdr[i] - data_start;
            }
        }
        if (!src || span == 0) {
            LogWarn("Party P_DAT rebuild: no blob for char=%u (%s)", char_id, CharName(char_id));
            continue;
        }

        const std::uint32_t alloc_size = (span + 0xFu) & ~0xFu;
        void* mem = VirtualAlloc(nullptr, alloc_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (!mem) {
            LogWarn("Party P_DAT rebuild: VirtualAlloc failed slot=%u size=%u", slot, alloc_size);
            continue;
        }
        std::memset(mem, 0, alloc_size);
        std::memcpy(mem, src, span);
        g_pdat_slot_alloc[slot] = mem;
        g_pdat_slot_alloc_size[slot] = alloc_size;

        const std::uint32_t base_rel =
            static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(mem)) - c8a_abs;
        auto* dst = reinterpret_cast<std::uint32_t*>(c8a + static_cast<std::uint32_t>(slot) * 0x10u);
        for (int i = 0; i < 4; ++i) {
            dst[i] = base_rel + hdr_rel[i];
        }

        ++built;
    }
    if (n < 4u) {
        auto* term = reinterpret_cast<std::uint32_t*>(c8a + static_cast<std::uint32_t>(n) * 0x10u);
        term[0] = term[1] = term[2] = term[3] = 0x40u;
    }

}

}  // namespace grandia_mod
