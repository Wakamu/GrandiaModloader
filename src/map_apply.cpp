#include "map_apply.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>

namespace grandia_mod {

extern "C" std::uint32_t g_select_p28 = 0;

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
// call +0x2A70 after sec[29] word-copy; apply before mixer bind sees the table.
constexpr std::uintptr_t kSfxBindCallRva = 0x5B0B6u;
constexpr std::size_t kSfxBindCallPatch = 5u;
constexpr std::uintptr_t kSfxBindFnRva = 0x2A70u;
constexpr std::uintptr_t kSfxBindResumeRva = 0x5B0BBu;
// call +0x72C0 after the field-setup sec[8] 4 KiB word-copy at +0x61140.
constexpr std::uintptr_t kSec8CopyDoneRva = 0x61170u;
constexpr std::size_t kSec8CopyDonePatch = 5u;
constexpr std::uintptr_t kSec8CopyDoneFnRva = 0x72C0u;
constexpr std::uintptr_t kSec8CopyDoneResumeRva = 0x61175u;

constexpr std::uintptr_t kHeapSec7PtrRva = 0x23FA6Cu;  // VA 0x63FA6C @ image 0x400000
constexpr std::uintptr_t kCopySrcPtrRva = 0x319700u;   // VA 0x719700
constexpr std::uintptr_t kMdpSec7PtrRva = 0x31A714u;   // VA 0x71A714
constexpr std::uintptr_t kScnPtrRva = 0x31CD30u;       // VA 0x71CD30
constexpr std::uintptr_t kOfsPtrRva = 0x31CD4Cu;       // VA 0x71CD4C
constexpr std::uintptr_t kSfxHeapPtrRva = 0x31A280u;   // VA 0x71A280 — sec[29] heap copy
constexpr std::uintptr_t kSfxTablePtrRva = 0x240E4Cu;  // VA 0x640E4C — copy+8
constexpr std::uintptr_t kHeapSec8PtrRva = 0x23FA4Cu;  // VA 0x63FA4C — sec[8] 4 KiB copy
constexpr std::uintptr_t kAnimDirPtrRva = 0x31CAE0u;   // VA 0x71CAE0 — sec[21] directory
constexpr std::uintptr_t kCamDirPtrRva = 0x31A644u;    // VA 0x71A644 — sec[15] directory
constexpr std::uintptr_t kCamIpRva = 0x319934u;        // VA 0x719934 — walker IP
constexpr std::uintptr_t kFieldParamsPtrRva = 0x23FA9Cu;  // VA 0x63FA9C — sec[10] field object
constexpr std::uintptr_t kSelectPanXHiRva = 0x313F3Eu;    // VA 0x713F3E
constexpr std::uintptr_t kSelectPanZHiRva = 0x313F40u;    // VA 0x713F40
constexpr std::uintptr_t kSelectPanZLoRva = 0x313F42u;    // VA 0x713F42
constexpr std::uintptr_t kSelectPanXLoRva = 0x313F44u;    // VA 0x713F44
constexpr std::uintptr_t kPitchRva = 0x319304u;           // VA 0x719304
constexpr std::uintptr_t kProjARva = 0x31A9D8u;           // VA 0x71A9D8
constexpr std::uintptr_t kProjBRva = 0x31C1A0u;           // VA 0x71C1A0
constexpr std::uintptr_t kProjCRva = 0x319708u;           // VA 0x719708
constexpr std::uintptr_t kClipSpanRva = 0x31C1B0u;        // VA 0x71C1B0 — |hi−lo|
constexpr std::uintptr_t kClipScaleRva = 0x3196F0u;        // VA 0x7196F0 — +0xEE
// Select enter (+0x7CF30, SM==1) writes p28 as BE to script 0 +0x20.
// Stock is `mov ecx, 0x4000` at +0x7D028; tiny AABB can overwrite it.
// Hook both the immediate and [ebp-4] after the tiny-box branch.
constexpr std::uintptr_t kSelectP28ImmRva = 0x7D028u;
constexpr std::size_t kSelectP28ImmPatch = 5u;
constexpr std::uintptr_t kSelectP28ImmResumeRva = 0x7D02Du;
constexpr std::uint8_t kSelectP28ImmBytes[] = {0xB9, 0x00, 0x40, 0x00, 0x00};
constexpr std::uintptr_t kSelectZoomRva = 0x7D0C9u;
constexpr std::size_t kSelectZoomPatch = 6u;
constexpr std::uintptr_t kSelectZoomResumeRva = 0x7D0CFu;
constexpr std::uintptr_t kSelectCamPtrRva = 0x31CD54u;  // VA 0x71CD54
constexpr std::uintptr_t kSelectP28LiveRva = 0x319334u;  // VA 0x719334
constexpr std::uintptr_t kPgmdtBlobPtrRva = 0x23FA48u;   // VA 0x63FA48
constexpr std::uintptr_t kAnimSlotRva = 0x31A740u;     // VA 0x71A740 — 32 × 20-byte slots
constexpr std::uintptr_t kAnimLatchRva = 0x7A1A0u;
constexpr std::size_t kAnimLatchPatch = 6u;
constexpr std::uint8_t kAnimLatchBytes[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08};
// Common tail of the latch polyline table (+0x756B1): movsx eax, cx /
// lea ecx, [eax+eax*4] then [71A74C] and call +0x1F7830. Every baked
// map/anim/talk row lands here; replace the pushed sample count (or the
// reverse start in edx) with the live slot Flags.
constexpr std::uintptr_t kAnimPolyTailRva = 0x75892u;
constexpr std::size_t kAnimPolyTailPatch = 6u;
constexpr std::uint8_t kAnimPolyTailBytes[] = {0x0F, 0xBF, 0xC1, 0x8D, 0x0C, 0x80};
constexpr std::uintptr_t kAnimSlotFlagsRva = 0x31A746u;
constexpr std::uintptr_t kMallocIatRva = 0x1FE334u;    // VA 0x5FE334
constexpr std::uintptr_t kFreeIatRva = 0x1FE33Cu;      // VA 0x5FE33C
constexpr unsigned kSec7Budget = 0x4000u;
constexpr unsigned kSec29Budget = 0x400u;
constexpr unsigned kSec8Budget = 0x1000u;
constexpr unsigned kSec21Budget = 0x20000u;
constexpr unsigned kSec15Budget = 0x10000u;
constexpr unsigned kSec10Budget = 512u;
constexpr unsigned kAnimSlots = 32;
constexpr unsigned kAnimSlotStride = 20;
constexpr unsigned kAnimDirMax = 512;
constexpr unsigned kMaxGrownStreams = 32;

char g_stem[16]{};
void* g_sec7_copy_site = nullptr;
std::uint8_t g_sec7_copy_original[8]{};
void* g_sec7_relocate_site = nullptr;
std::uint8_t g_sec7_relocate_original[8]{};
void* g_scn_bind_site = nullptr;
std::uint8_t g_scn_bind_original[8]{};
void* g_sfx_bind_site = nullptr;
std::uint8_t g_sfx_bind_original[8]{};
void* g_sec8_copy_site = nullptr;
std::uint8_t g_sec8_copy_original[8]{};
void* g_grown_scn = nullptr;
void* g_grown_ofs = nullptr;
void* g_grown_sec21 = nullptr;
void* g_mdp_sec21 = nullptr;
void* g_grown_sec15 = nullptr;
unsigned g_sec15_len = 0;
void* g_grown_streams[kMaxGrownStreams]{};
unsigned g_grown_stream_n = 0;
char g_sec21_applied[16]{};
char g_sec15_applied[16]{};
void* g_anim_latch_site = nullptr;
void* g_anim_latch_tramp_mem = nullptr;
std::uint8_t g_anim_latch_original[8]{};
void* g_anim_poly_site = nullptr;
void* g_anim_poly_tramp_mem = nullptr;
std::uint8_t g_anim_poly_original[8]{};
void* g_select_zoom_site = nullptr;
std::uint8_t g_select_zoom_original[8]{};
void* g_select_p28_imm_site = nullptr;
std::uint8_t g_select_p28_imm_original[8]{};

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

bool WriteI16(std::uintptr_t address, std::int16_t value) {
    return SafeWriteByte(address, static_cast<std::uint8_t>(value & 0xFF)) &&
           SafeWriteByte(address + 1,
                         static_cast<std::uint8_t>((static_cast<std::uint16_t>(value) >> 8) & 0xFF));
}

bool WriteU16(std::uintptr_t address, std::uint16_t value) {
    return SafeWriteByte(address, static_cast<std::uint8_t>(value & 0xFF)) &&
           SafeWriteByte(address + 1, static_cast<std::uint8_t>((value >> 8) & 0xFF));
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
    // Do not free g_grown_sec21 / g_grown_sec15 here: [0x71CAE0] /
    // [0x71A644] / live IP may still point at them. ApplySec21 /
    // ApplySec15 free the previous map's buffer after bind has already
    // retargeted those globals to the new MDP.
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

}

void ApplySec29() {
    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0 || info.sec29 == 0 ||
        info.sec29_len <= 0) {
        return;
    }
    auto* heap = static_cast<std::uint8_t*>(ReadGlobal(kSfxHeapPtrRva));
    if (!heap) {
        auto* table = static_cast<std::uint8_t*>(ReadGlobal(kSfxTablePtrRva));
        if (table) {
            heap = table - 8;
        }
    }
    if (!heap) {
        LogWarn("map apply: sec[29] heap [0x71A280] is null");
        return;
    }
    const auto n = static_cast<unsigned>(info.sec29_len) < kSec29Budget
                       ? static_cast<unsigned>(info.sec29_len)
                       : kSec29Budget;
    if (!WriteRam(heap, reinterpret_cast<const void*>(info.sec29), n, kSec29Budget)) {
        LogWarn("map apply: sec[29] heap write failed");
        return;
    }

}

void ApplySec8() {
    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0 || info.sec8 == 0 ||
        info.sec8_len <= 0) {
        return;
    }
    auto* heap = static_cast<std::uint8_t*>(ReadGlobal(kHeapSec8PtrRva));
    if (!heap) {
        LogWarn("map apply: sec[8] heap [0x63FA4C] is null");
        return;
    }
    const auto n = static_cast<unsigned>(info.sec8_len) < kSec8Budget
                       ? static_cast<unsigned>(info.sec8_len)
                       : kSec8Budget;
    if (!WriteRam(heap, reinterpret_cast<const void*>(info.sec8), n, kSec8Budget)) {
        LogWarn("map apply: sec[8] heap write failed");
        return;
    }

}

bool ReadDirRow(const std::uint8_t* base, unsigned i, std::uint16_t* id, std::uint16_t* flags,
                std::uint32_t* off_a, std::uint32_t* off_b) {
    if (!base || !id || !flags || !off_a || !off_b) {
        return false;
    }
    const auto* row = base + 4 + i * 12;
    std::memcpy(id, row, 2);
    std::memcpy(flags, row + 2, 2);
    std::memcpy(off_a, row + 4, 4);
    std::memcpy(off_b, row + 8, 4);
    return true;
}

bool FindClipByStreamA(const std::uint8_t* base, std::uint32_t abs, std::uint16_t* id,
                       std::uint16_t* flags) {
    if (!base || base == reinterpret_cast<const std::uint8_t*>(static_cast<std::uintptr_t>(-1))) {
        return false;
    }
    std::uint16_t n = 0;
    std::memcpy(&n, base, 2);
    if (n == 0 || n > kAnimDirMax) {
        return false;
    }
    for (unsigned i = 0; i < n; ++i) {
        std::uint16_t row_id = 0;
        std::uint16_t row_flags = 0;
        std::uint32_t off_a = 0;
        std::uint32_t off_b = 0;
        if (!ReadDirRow(base, i, &row_id, &row_flags, &off_a, &off_b) || off_a == 0) {
            continue;
        }
        const auto addr = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(base + off_a));
        if (addr == abs) {
            *id = row_id;
            *flags = row_flags;
            return true;
        }
    }
    return false;
}

bool FindClipById(const std::uint8_t* base, std::uint16_t id, std::uint16_t* flags,
                  std::uint32_t* off_a, std::uint32_t* off_b) {
    if (!base) {
        return false;
    }
    std::uint16_t n = 0;
    std::memcpy(&n, base, 2);
    if (n == 0 || n > kAnimDirMax) {
        return false;
    }
    for (unsigned i = 0; i < n; ++i) {
        std::uint16_t row_id = 0;
        std::uint16_t row_flags = 0;
        std::uint32_t row_a = 0;
        std::uint32_t row_b = 0;
        if (!ReadDirRow(base, i, &row_id, &row_flags, &row_a, &row_b) || row_id != id) {
            continue;
        }
        *flags = row_flags;
        *off_a = row_a;
        *off_b = row_b;
        return true;
    }
    return false;
}

// Slots latch table-relative off_a/off_b against the 71CAE0 base. An early
// anim N (stock clip already walking) keeps those pointers after we swap
// the directory; a second apply that frees the grown blob UAF's them.
void RetargetAnimSlots(const std::uint8_t* old_base, const std::uint8_t* new_base) {
    if (!old_base || !new_base ||
        old_base == reinterpret_cast<const std::uint8_t*>(static_cast<std::uintptr_t>(-1))) {
        return;
    }
    auto* slots = reinterpret_cast<std::uint8_t*>(ModuleBase() + kAnimSlotRva);
    for (unsigned i = 0; i < kAnimSlots; ++i) {
        auto* slot = slots + i * kAnimSlotStride;
        if (slot[0] == 0xFF) {
            continue;
        }
        std::uint32_t stream_a = 0;
        std::memcpy(&stream_a, slot + 0xC, 4);
        std::uint16_t id = 0;
        std::uint16_t old_flags = 0;
        if (!FindClipByStreamA(old_base, stream_a, &id, &old_flags)) {
            continue;
        }
        (void)old_flags;
        std::uint16_t new_flags = 0;
        std::uint32_t off_a = 0;
        std::uint32_t off_b = 0;
        if (!FindClipById(new_base, id, &new_flags, &off_a, &off_b) || off_a == 0) {
            slot[0] = 0xFF;
            continue;
        }
        const auto new_a =
            static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(new_base + off_a));
        const auto new_b =
            static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(new_base + off_b));
        std::uint16_t packed = 0;
        std::memcpy(&packed, slot + 6, 2);
        packed = static_cast<std::uint16_t>((packed & 0xF000) | (new_flags & 0x0FFF));
        std::memcpy(slot + 6, &packed, 2);
        std::memcpy(slot + 0xC, &new_a, 4);
        std::memcpy(slot + 0x10, &new_b, 4);
        if (stream_a != new_a) {
            std::uint16_t index = 0;
            std::memcpy(slot + 2, &index, 2);
        }
    }
}

unsigned DirCount(const std::uint8_t* base) {
    if (!base || base == reinterpret_cast<const std::uint8_t*>(static_cast<std::uintptr_t>(-1))) {
        return 0;
    }
    std::uint16_t n = 0;
    std::memcpy(&n, base, 2);
    return n > kAnimDirMax ? 0 : n;
}

unsigned StreamABytes(std::uint16_t flags) {
    return 2u + static_cast<unsigned>(flags & 0x0FFF) * 6u;
}

unsigned StreamSize(const std::uint8_t* emit, unsigned emit_len, unsigned emit_n, std::uint32_t off) {
    if (off == 0 || off >= emit_len) {
        return 0;
    }
    unsigned next = emit_len;
    for (unsigned j = 0; j < emit_n; ++j) {
        std::uint16_t id = 0;
        std::uint16_t flags = 0;
        std::uint32_t off_a = 0;
        std::uint32_t off_b = 0;
        ReadDirRow(emit, j, &id, &flags, &off_a, &off_b);
        if (off_a > off && off_a < next) {
            next = off_a;
        }
        if (off_b > off && off_b < next) {
            next = off_b;
        }
    }
    return next - off;
}

void PadLastXyz(std::uint8_t* dest, unsigned dest_len, const std::uint8_t* src, unsigned src_len) {
    if (!dest || dest_len <= src_len || src_len < 8) {
        return;
    }
    const auto* last = src + src_len - 6;
    for (unsigned o = src_len; o + 6 <= dest_len; o += 6) {
        std::memcpy(dest + o, last, 6);
    }
}

void FreeGrownStreams() {
    for (unsigned i = 0; i < g_grown_stream_n; ++i) {
        GameFree(g_grown_streams[i]);
        g_grown_streams[i] = nullptr;
    }
    g_grown_stream_n = 0;
}

bool TrackGrownStream(void* mem) {
    if (!mem || g_grown_stream_n >= kMaxGrownStreams) {
        return false;
    }
    g_grown_streams[g_grown_stream_n++] = mem;
    return true;
}

void UpdateSlotsAbs(std::uint32_t old_abs, std::uint32_t new_abs, std::uint16_t new_flags) {
    auto* slots = reinterpret_cast<std::uint8_t*>(ModuleBase() + kAnimSlotRva);
    for (unsigned i = 0; i < kAnimSlots; ++i) {
        auto* slot = slots + i * kAnimSlotStride;
        if (slot[0] == 0xFF) {
            continue;
        }
        std::uint32_t stream_a = 0;
        std::memcpy(&stream_a, slot + 0xC, 4);
        if (stream_a != old_abs && stream_a != new_abs) {
            continue;
        }
        std::uint16_t packed = 0;
        std::memcpy(&packed, slot + 6, 2);
        packed = static_cast<std::uint16_t>((packed & 0xF000) | (new_flags & 0x0FFF));
        std::memcpy(slot + 6, &packed, 2);
        std::memcpy(slot + 0xC, &new_abs, 4);
        std::uint16_t index = 0;
        std::memcpy(slot + 2, &index, 2);
    }
}

void WriteClipMeta(std::uint8_t* live, unsigned live_n, std::uint16_t id, std::uint16_t flags,
                   std::uint32_t off_a) {
    for (unsigned r = 0; r < live_n; ++r) {
        std::uint16_t rid = 0;
        std::uint16_t rf = 0;
        std::uint32_t ra = 0;
        std::uint32_t rb = 0;
        ReadDirRow(live, r, &rid, &rf, &ra, &rb);
        if (rid != id) {
            continue;
        }
        std::memcpy(live + 4 + r * 12 + 2, &flags, 2);
        std::memcpy(live + 4 + r * 12 + 4, &off_a, 4);
        return;
    }
}

void SyncSlotCounts(const std::uint8_t* base) {
    if (!base) {
        return;
    }
    auto* slots = reinterpret_cast<std::uint8_t*>(ModuleBase() + kAnimSlotRva);
    for (unsigned i = 0; i < kAnimSlots; ++i) {
        auto* slot = slots + i * kAnimSlotStride;
        if (slot[0] == 0xFF) {
            continue;
        }
        std::uint32_t stream_a = 0;
        std::memcpy(&stream_a, slot + 0xC, 4);
        std::uint16_t id = 0;
        std::uint16_t flags = 0;
        if (!FindClipByStreamA(base, stream_a, &id, &flags)) {
            continue;
        }
        (void)id;
        std::uint16_t packed = 0;
        std::memcpy(&packed, slot + 6, 2);
        packed = static_cast<std::uint16_t>((packed & 0xF000) | (flags & 0x0FFF));
        std::memcpy(slot + 6, &packed, 2);
    }
}

bool TryPatchOrGrow(std::uint8_t* live, const std::uint8_t* emit, unsigned emit_len) {
    const auto live_n = DirCount(live);
    const auto emit_n = DirCount(emit);
    if (live_n == 0 || emit_n == 0 || emit_len < 4) {
        return false;
    }
    for (unsigned i = 0; i < emit_n; ++i) {
        std::uint16_t id = 0;
        std::uint16_t new_flags = 0;
        std::uint32_t new_a = 0;
        std::uint32_t new_b = 0;
        if (!ReadDirRow(emit, i, &id, &new_flags, &new_a, &new_b)) {
            return false;
        }
        std::uint16_t old_flags = 0;
        std::uint32_t old_a = 0;
        std::uint32_t old_b = 0;
        if (!FindClipById(live, id, &old_flags, &old_a, &old_b) || old_a == 0) {
            return false;
        }
        if (StreamABytes(new_flags) > StreamABytes(old_flags)) {
            return false;
        }
    }

    for (unsigned i = 0; i < emit_n; ++i) {
        std::uint16_t id = 0;
        std::uint16_t new_flags = 0;
        std::uint32_t new_a = 0;
        std::uint32_t new_b = 0;
        ReadDirRow(emit, i, &id, &new_flags, &new_a, &new_b);
        std::uint16_t old_flags = 0;
        std::uint32_t old_a = 0;
        std::uint32_t old_b = 0;
        if (!FindClipById(live, id, &old_flags, &old_a, &old_b) || old_a == 0) {
            return false;
        }
        const auto old_bytes = StreamABytes(old_flags);
        const auto new_bytes = StreamABytes(new_flags);
        if (new_bytes > old_bytes) {
            return false;
        }
        if (new_a == 0 || new_a + new_bytes > emit_len) {
            return false;
        }
        const auto old_abs =
            static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(live + old_a));
        auto* dest = live + old_a;
        std::memcpy(dest, emit + new_a, new_bytes);
        PadLastXyz(dest, old_bytes, dest, new_bytes);
        WriteClipMeta(live, live_n, id, new_flags, old_a);
        UpdateSlotsAbs(old_abs, old_abs, new_flags);
        const auto b_bytes = StreamSize(emit, emit_len, emit_n, new_b);
        if (b_bytes > 0 && old_b != 0) {
            std::memcpy(live + old_b, emit + new_b, b_bytes);
        }
    }
    SyncSlotCounts(live);
    return true;
}

void DiscardStaleGrown(std::uint8_t* cur) {
    if (g_grown_sec21 && cur != g_grown_sec21) {
        GameFree(g_grown_sec21);
        g_grown_sec21 = nullptr;
    }
    g_mdp_sec21 = nullptr;
    FreeGrownStreams();
    std::memset(g_sec21_applied, 0, sizeof(g_sec21_applied));
}

void ApplySec21() {
    auto* cur = static_cast<std::uint8_t*>(ReadGlobal(kAnimDirPtrRva));
    const bool same_map =
        std::memcmp(g_sec21_applied, g_stem, sizeof(g_sec21_applied)) == 0 && g_stem[0];
    if (!same_map) {
        DiscardStaleGrown(cur);
        cur = static_cast<std::uint8_t*>(ReadGlobal(kAnimDirPtrRva));
    }

    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0 || info.sec21 == 0 ||
        info.sec21_len <= 0) {
        return;
    }
    const auto n = static_cast<unsigned>(info.sec21_len) < kSec21Budget
                       ? static_cast<unsigned>(info.sec21_len)
                       : kSec21Budget;
    auto* emit = reinterpret_cast<const std::uint8_t*>(info.sec21);
    if (TryPatchOrGrow(cur, emit, n)) {
        std::memcpy(g_sec21_applied, g_stem, sizeof(g_sec21_applied));

        return;
    }

    const auto emit_n = DirCount(emit);
    for (unsigned i = 0; i < emit_n; ++i) {
        std::uint16_t id = 0;
        std::uint16_t new_flags = 0;
        std::uint32_t new_a = 0;
        std::uint32_t new_b = 0;
        ReadDirRow(emit, i, &id, &new_flags, &new_a, &new_b);
        std::uint16_t old_flags = 0;
        std::uint32_t old_a = 0;
        std::uint32_t old_b = 0;
        if (cur && FindClipById(cur, id, &old_flags, &old_a, &old_b) &&
            (old_flags & 0x0FFF) != (new_flags & 0x0FFF)) {

        }
    }

    if (cur && cur != g_grown_sec21) {
        g_mdp_sec21 = cur;
    }

    // Growing a clip (or adding an id) needs a compact table. Same path as
    // AddAnim. Reuse the buffer on the sec[8] re-apply so we do not UAF a
    // slot that already walks the first swap.
    if (same_map && g_grown_sec21) {
        auto* grown = static_cast<std::uint8_t*>(g_grown_sec21);
        std::memcpy(grown, emit, n);
        if (cur != grown) {
            RetargetAnimSlots(cur, grown);
            SafeWriteU32(ModuleBase() + kAnimDirPtrRva,
                         static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(grown)));
        } else {
            RetargetAnimSlots(grown, grown);
        }

        return;
    }

    FreeGrownStreams();
    if (g_grown_sec21 && cur == g_grown_sec21) {
        return;
    }
    if (g_grown_sec21) {
        GameFree(g_grown_sec21);
        g_grown_sec21 = nullptr;
    }
    auto* grown = static_cast<std::uint8_t*>(
        GrowBuf(reinterpret_cast<const void*>(info.sec21), n, &g_grown_sec21));
    if (!grown) {
        LogWarn("map apply: sec[21] grow alloc failed");
        return;
    }
    RetargetAnimSlots(cur, grown);
    if (!SafeWriteU32(ModuleBase() + kAnimDirPtrRva,
                      static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(grown)))) {
        LogWarn("map apply: sec[21] [0x71CAE0] write failed");
        return;
    }
    std::memcpy(g_sec21_applied, g_stem, sizeof(g_sec21_applied));

}

void RetargetCamIp(const std::uint8_t* old_base, const std::uint8_t* new_base, unsigned old_len) {
    if (!old_base || !new_base || old_base == new_base || old_len == 0) {
        return;
    }
    auto* ip = static_cast<std::uint8_t*>(ReadGlobal(kCamIpRva));
    if (!ip || ip < old_base || ip >= old_base + old_len) {
        return;
    }
    const auto delta = static_cast<std::intptr_t>(new_base - old_base);
    SafeWriteU32(ModuleBase() + kCamIpRva,
                 static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(ip + delta)));
}

void DiscardStaleGrownSec15(std::uint8_t* cur) {
    if (g_grown_sec15 && cur != g_grown_sec15) {
        GameFree(g_grown_sec15);
        g_grown_sec15 = nullptr;
    }
    g_sec15_len = 0;
    std::memset(g_sec15_applied, 0, sizeof(g_sec15_applied));
}

void ApplySec15() {
    auto* cur = static_cast<std::uint8_t*>(ReadGlobal(kCamDirPtrRva));
    const bool same_map =
        std::memcmp(g_sec15_applied, g_stem, sizeof(g_sec15_applied)) == 0 && g_stem[0];
    if (!same_map) {
        DiscardStaleGrownSec15(cur);
        cur = static_cast<std::uint8_t*>(ReadGlobal(kCamDirPtrRva));
    }

    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0 || info.sec15 == 0 ||
        info.sec15_len <= 0) {
        return;
    }
    const auto n = static_cast<unsigned>(info.sec15_len) < kSec15Budget
                       ? static_cast<unsigned>(info.sec15_len)
                       : kSec15Budget;
    auto* emit = reinterpret_cast<const std::uint8_t*>(info.sec15);
    const auto old_len = g_sec15_len > 0 ? g_sec15_len : kSec15Budget;

    if (same_map && g_grown_sec15) {
        auto* grown = static_cast<std::uint8_t*>(g_grown_sec15);
        std::memcpy(grown, emit, n);
        if (n < kSec15Budget) {
            std::memset(grown + n, 0, kSec15Budget - n);
        }
        if (cur != grown) {
            RetargetCamIp(cur, grown, old_len);
            SafeWriteU32(ModuleBase() + kCamDirPtrRva,
                         static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(grown)));
        }
        g_sec15_len = n;
        std::memcpy(g_sec15_applied, g_stem, sizeof(g_sec15_applied));
        return;
    }

    if (g_grown_sec15 && cur == g_grown_sec15) {
        auto* grown = static_cast<std::uint8_t*>(g_grown_sec15);
        std::memcpy(grown, emit, n);
        if (n < kSec15Budget) {
            std::memset(grown + n, 0, kSec15Budget - n);
        }
        g_sec15_len = n;
        std::memcpy(g_sec15_applied, g_stem, sizeof(g_sec15_applied));
        return;
    }

    if (g_grown_sec15) {
        GameFree(g_grown_sec15);
        g_grown_sec15 = nullptr;
    }
    auto* mem = GameMalloc(kSec15Budget);
    if (!mem) {
        LogWarn("map apply: sec[15] grow alloc failed");
        return;
    }
    std::memcpy(mem, emit, n);
    if (n < kSec15Budget) {
        std::memset(static_cast<std::uint8_t*>(mem) + n, 0, kSec15Budget - n);
    }
    g_grown_sec15 = mem;
    auto* grown = static_cast<std::uint8_t*>(mem);
    RetargetCamIp(cur, grown, old_len);
    if (!SafeWriteU32(ModuleBase() + kCamDirPtrRva,
                      static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(grown)))) {
        LogWarn("map apply: sec[15] [0x71A644] write failed");
        return;
    }
    g_sec15_len = n;
    std::memcpy(g_sec15_applied, g_stem, sizeof(g_sec15_applied));
}

void WriteLiveSelectP28(std::uint32_t raw) {
    if (raw == 0) {
        return;
    }
    const auto base = ModuleBase();
    SafeWriteU32(base + kSelectP28LiveRva, raw);
    auto* blob = static_cast<std::uint8_t*>(ReadGlobal(kPgmdtBlobPtrRva));
    if (!blob) {
        return;
    }
    std::uint32_t off = 0;
    std::memcpy(&off, blob, 4);
    if (off < 12 || off > 0x800) {
        return;
    }
    auto* dst = blob + off + 0x20;
    dst[0] = static_cast<std::uint8_t>(raw >> 24);
    dst[1] = static_cast<std::uint8_t>(raw >> 16);
    dst[2] = static_cast<std::uint8_t>(raw >> 8);
    dst[3] = static_cast<std::uint8_t>(raw);
}

void LatchSelectP28() {
    if (!g_stem[0]) {
        g_select_p28 = 0;
        return;
    }
    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0 || info.select_p28 == 0) {
        g_select_p28 = 0;
        return;
    }
    g_select_p28 = static_cast<std::uint32_t>(info.select_p28);
}

void ApplySec10() {
    LatchSelectP28();
    if (g_select_p28 != 0) {
        LogInfo("map apply: Select p28=0x%X on %s", g_select_p28, g_stem);
        WriteLiveSelectP28(g_select_p28);
    }
    MapPatchInfoNative info{};
    if (RuntimeMapPatchInfo(g_stem, &info) != 0 || info.dirty == 0) {
        return;
    }
    if (info.sec10 == 0 || info.sec10_len < static_cast<std::int32_t>(kSec10Budget)) {
        return;
    }
    auto* heap = static_cast<std::uint8_t*>(ReadGlobal(kFieldParamsPtrRva));
    if (!heap) {
        LogWarn("map apply: sec[10] heap [0x63FA9C] is null");
        return;
    }
    auto* src = reinterpret_cast<const std::uint8_t*>(info.sec10);
    // Camera words only — leave gold / shop pages / encounter tail alone.
    if (!WriteRam(heap + 0x04, src + 0x04, 0x18) || !WriteRam(heap + 0x84, src + 0x84, 0x14) ||
        !WriteRam(heap + 0xE4, src + 0xE4, 0x0C)) {
        LogWarn("map apply: sec[10] camera write failed");
        return;
    }

    const int b94 = src[0x94];
    const int b95 = src[0x95];
    const int b96 = src[0x96];
    const int b97 = src[0x97];
    const auto base = ModuleBase();
    WriteI16(base + kSelectPanXLoRva, static_cast<std::int16_t>((b94 - 128) << 4));
    WriteI16(base + kSelectPanXHiRva, static_cast<std::int16_t>((b96 - 128) << 4));
    WriteI16(base + kSelectPanZHiRva, static_cast<std::int16_t>((128 - b95) << 4));
    WriteI16(base + kSelectPanZLoRva, static_cast<std::int16_t>((128 - b97) << 4));

    std::uint32_t pitch = 0;
    std::memcpy(&pitch, src + 0x10, 4);
    SafeWriteU32(base + kPitchRva, pitch << 16);

    std::uint16_t proj_a = 0;
    std::uint16_t proj_b = 0;
    std::uint16_t proj_c = 0;
    std::memcpy(&proj_a, src + 0x84, 2);
    std::memcpy(&proj_b, src + 0x86, 2);
    std::memcpy(&proj_c, src + 0x88, 2);
    WriteU16(base + kProjARva, proj_a != 0 ? proj_a : 0x400);
    WriteU16(base + kProjBRva, proj_b != 0 ? proj_b : 0x180);
    WriteU16(base + kProjCRva, proj_c != 0 ? proj_c : 0x800);

    std::int16_t clip_lo = 0;
    std::int16_t clip_hi = 0;
    std::int16_t clip_scale = 0;
    std::memcpy(&clip_lo, src + 0xE4, 2);
    std::memcpy(&clip_hi, src + 0xE8, 2);
    std::memcpy(&clip_scale, src + 0xEE, 2);
    auto span = static_cast<int>(clip_hi) - static_cast<int>(clip_lo);
    if (span < 0) {
        span = -span;
    }
    if (span > 32767) {
        span = 32767;
    }
    WriteI16(base + kClipSpanRva, static_cast<std::int16_t>(span));
    // +0x60E4F: movsx EE; cdq; and edx,0xFF; add; sar 8; shl 16 → 7196F0.
    // Parm EE=256 → 0x10000. A raw i16 poke is ignored by SoftHD.
    const auto ee = static_cast<std::int32_t>(clip_scale);
    const auto adj = ee + ((ee >> 31) & 0xFF);
    SafeWriteU32(base + kClipScaleRva, static_cast<std::uint32_t>(adj >> 8) << 16);
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
        } else {
            LogWarn("map apply: OFS grow alloc failed");
        }
    }
}

void EncodeMovEcxAbs(std::uint8_t* out, std::uintptr_t abs_addr) {
    out[0] = 0x8B;
    out[1] = 0x0D;
    std::memcpy(out + 2, &abs_addr, 4);
}

void EncodeMovEbxAbs(std::uint8_t* out, std::uintptr_t abs_addr) {
    out[0] = 0x8B;
    out[1] = 0x1D;
    std::memcpy(out + 2, &abs_addr, 4);
}

void EncodeMovAbsEsi(std::uint8_t* out, std::uintptr_t abs_addr) {
    out[0] = 0x89;
    out[1] = 0x35;
    std::memcpy(out + 2, &abs_addr, 4);
}

void EncodeCallRel(std::uint8_t* out, std::uintptr_t site_rva, std::uintptr_t target_rva) {
    out[0] = 0xE8;
    const auto rel = static_cast<std::int32_t>(target_rva - (site_rva + 5u));
    std::memcpy(out + 1, &rel, 4);
}

}  // namespace

#if defined(_M_IX86)
extern "C" void* g_mod_sec7_copy_resume = nullptr;
extern "C" void* g_mod_sec7_relocate_resume = nullptr;
extern "C" void* g_mod_scn_bind_resume = nullptr;
extern "C" void* g_mod_sfx_bind_fn = nullptr;
extern "C" void* g_mod_sfx_bind_resume = nullptr;
extern "C" void* g_mod_sec8_copy_fn = nullptr;
extern "C" void* g_mod_sec8_copy_resume = nullptr;
extern "C" void* g_mod_scn_bank_abs = nullptr;
extern "C" void* g_mod_sec7_copy_src_abs = nullptr;
extern "C" void* g_mod_sec7_heap_abs = nullptr;
extern "C" void* g_mod_anim_latch_tramp = nullptr;
extern "C" void* g_mod_anim_poly_tramp = nullptr;
extern "C" void* g_mod_anim_slot_flags_abs = nullptr;
extern "C" void* g_mod_select_cam_abs = nullptr;
extern "C" void* g_mod_select_zoom_resume = nullptr;
extern "C" void* g_mod_select_p28_imm_resume = nullptr;

extern "C" std::uint32_t SelectP28Now() {
    LatchSelectP28();
    if (g_select_p28 != 0) {
        WriteLiveSelectP28(g_select_p28);
    }
    return g_select_p28;
}

extern "C" void ModFixPolyCount(std::uint32_t* pushed, std::uint32_t* start, std::uint32_t slot) {
    if (!pushed || !start || !g_mod_anim_slot_flags_abs || slot >= kAnimSlots || !g_stem[0] ||
        std::memcmp(g_sec21_applied, g_stem, sizeof(g_sec21_applied)) != 0) {
        return;
    }
    std::uint16_t packed = 0;
    std::memcpy(&packed, static_cast<std::uint8_t*>(g_mod_anim_slot_flags_abs) + slot * kAnimSlotStride,
                2);
    const auto count = static_cast<std::uint32_t>(packed & 0x0FFF);
    if (count == 0) {
        return;
    }
    if (*start == 0) {
        *pushed = count;
    } else if (*pushed == 0) {
        *start = count;
    }
}

extern "C" __declspec(naked) void ModAnimPolyTailDetour() {
    __asm {
        pushad
        lea eax, [esp + 32]
        lea ecx, [esp + 20]
        movzx edx, word ptr [esp + 24]
        push edx
        push ecx
        push eax
        call ModFixPolyCount
        add esp, 12
        popad
        jmp dword ptr [g_mod_anim_poly_tramp]
    }
}

extern "C" void ModBeforeAnimLatch(std::uint32_t* dir) {
    if (!dir || !g_grown_sec21 || !g_stem[0] ||
        std::memcmp(g_sec21_applied, g_stem, sizeof(g_sec21_applied)) != 0) {
        return;
    }
    auto* grown = static_cast<std::uint8_t*>(g_grown_sec21);
    if (g_mdp_sec21) {
        RetargetAnimSlots(static_cast<const std::uint8_t*>(g_mdp_sec21), grown);
    }
    *dir = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(grown));
}

extern "C" __declspec(naked) void ModSelectP28ImmDetour() {
    __asm {
        call SelectP28Now
        mov ecx, eax
        test ecx, ecx
        jne got_p28
        mov ecx, 0x4000
    got_p28:
        jmp dword ptr [g_mod_select_p28_imm_resume]
    }
}

extern "C" __declspec(naked) void ModSelectZoomDetour() {
    __asm {
        push eax
        call SelectP28Now
        test eax, eax
        je skip_p28
        mov dword ptr [ebp - 4], eax
    skip_p28:
        pop eax
        mov ebx, dword ptr [g_mod_select_cam_abs]
        mov ebx, dword ptr [ebx]
        jmp dword ptr [g_mod_select_zoom_resume]
    }
}

extern "C" __declspec(naked) void ModAnimLatchDetour() {
    __asm {
        pushad
        lea eax, [esp + 44]
        push eax
        call ModBeforeAnimLatch
        add esp, 4
        popad
        jmp dword ptr [g_mod_anim_latch_tramp]
    }
}

extern "C" void ModAfterSec7Copy() {
    ApplySec7();
}

extern "C" void ModAfterSec7Relocate() {
    // Image may rebase; apply here so relocate always reads the patched header
    // even if the post-copy hook missed. sec[21] bind (+0x54A9C / +0x54CB1)
    // is already done; latch at +0x7A1A0 does base+off from [0x71CAE0].
    ApplySec7();
    ApplySec21();
    ApplySec15();
}

extern "C" void ModAfterScnBind() {
    ApplyScripts();
}

extern "C" void ModBeforeSfxBind() {
    ApplySec29();
}

extern "C" void ModAfterSec8Copy() {
    ApplySec8();
    ApplySec21();
    ApplySec15();
    ApplySec10();
}

extern "C" __declspec(naked) void ModSec7CopyDetour() {
    __asm {
        pushad
        call ModAfterSec7Copy
        popad
        mov ecx, dword ptr [g_mod_sec7_copy_src_abs]
        mov ecx, dword ptr [ecx]
        jmp dword ptr [g_mod_sec7_copy_resume]
    }
}

extern "C" __declspec(naked) void ModSec7RelocateDetour() {
    __asm {
        pushad
        call ModAfterSec7Relocate
        popad
        mov ecx, dword ptr [g_mod_sec7_heap_abs]
        mov ecx, dword ptr [ecx]
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

extern "C" __declspec(naked) void ModSfxBindDetour() {
    __asm {
        pushad
        call ModBeforeSfxBind
        popad
        mov eax, dword ptr [g_mod_sfx_bind_fn]
        call eax
        jmp dword ptr [g_mod_sfx_bind_resume]
    }
}

extern "C" __declspec(naked) void ModSec8CopyDetour() {
    __asm {
        pushad
        call ModAfterSec8Copy
        popad
        mov eax, dword ptr [g_mod_sec8_copy_fn]
        call eax
        jmp dword ptr [g_mod_sec8_copy_resume]
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
    LatchSelectP28();
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
    g_mod_sec7_copy_src_abs = reinterpret_cast<void*>(base + kCopySrcPtrRva);
    g_mod_sec7_heap_abs = reinterpret_cast<void*>(base + kHeapSec7PtrRva);

    auto install = [&](std::uintptr_t rva, std::size_t size, const std::uint8_t* expect,
                       void* detour, void** resume_slot, std::uintptr_t resume_rva,
                       std::uint8_t* original, void** site_out, const char* name) {
        auto* site = reinterpret_cast<std::uint8_t*>(base + rva);
        if (!IsExecutableAddress(site) || !BytesMatch(site, expect, size)) {
            std::uint8_t got[8]{};
            for (std::size_t i = 0; i < size && i < sizeof(got); ++i) {
                SafeReadByte(reinterpret_cast<std::uintptr_t>(site) + i, &got[i]);
            }
            LogWarn("map apply %s site mismatch at +0x%X exec=%d got=%02X%02X%02X%02X%02X%02X",
                    name, static_cast<unsigned>(rva), IsExecutableAddress(site) ? 1 : 0, got[0],
                    got[1], got[2], got[3], got[4], got[5]);
            return false;
        }
        *resume_slot = reinterpret_cast<void*>(base + resume_rva);
        if (!WriteJump(site, detour, original, size)) {
            LogWarn("map apply %s hook failed", name);
            return false;
        }
        *site_out = site;

        return true;
    };

    // Absolute addresses in these movs relocate with ASLR (DYNAMIC_BASE).
    std::uint8_t sec7_copy_expect[6]{};
    std::uint8_t sec7_rel_expect[6]{};
    std::uint8_t scn_expect[6]{};
    EncodeMovEcxAbs(sec7_copy_expect, base + kCopySrcPtrRva);
    EncodeMovEcxAbs(sec7_rel_expect, base + kHeapSec7PtrRva);
    EncodeMovAbsEsi(scn_expect, base + kScnPtrRva);
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
    std::uint8_t sfx_expect[5]{};
    EncodeCallRel(sfx_expect, kSfxBindCallRva, kSfxBindFnRva);
    g_mod_sfx_bind_fn = reinterpret_cast<void*>(base + kSfxBindFnRva);
    ok += install(kSfxBindCallRva, kSfxBindCallPatch, sfx_expect,
                  reinterpret_cast<void*>(&ModSfxBindDetour), &g_mod_sfx_bind_resume,
                  kSfxBindResumeRva, g_sfx_bind_original, &g_sfx_bind_site, "sfx-bind")
              ? 1
              : 0;
    std::uint8_t sec8_expect[5]{};
    EncodeCallRel(sec8_expect, kSec8CopyDoneRva, kSec8CopyDoneFnRva);
    g_mod_sec8_copy_fn = reinterpret_cast<void*>(base + kSec8CopyDoneFnRva);
    ok += install(kSec8CopyDoneRva, kSec8CopyDonePatch, sec8_expect,
                  reinterpret_cast<void*>(&ModSec8CopyDetour), &g_mod_sec8_copy_resume,
                  kSec8CopyDoneResumeRva, g_sec8_copy_original, &g_sec8_copy_site, "sec8-copy")
              ? 1
              : 0;
    auto* latch = reinterpret_cast<std::uint8_t*>(base + kAnimLatchRva);
    if (IsExecutableAddress(latch) && BytesMatch(latch, kAnimLatchBytes, kAnimLatchPatch)) {
        g_anim_latch_tramp_mem = MakeTrampoline(latch, kAnimLatchPatch, latch + kAnimLatchPatch);
        if (g_anim_latch_tramp_mem &&
            WriteJump(latch, reinterpret_cast<void*>(&ModAnimLatchDetour), g_anim_latch_original,
                      kAnimLatchPatch)) {
            g_mod_anim_latch_tramp = g_anim_latch_tramp_mem;
            g_anim_latch_site = latch;

            ++ok;
        } else {
            LogWarn("map apply anim-latch hook failed");
        }
    } else {
        LogWarn("map apply anim-latch site mismatch at +0x%X", static_cast<unsigned>(kAnimLatchRva));
    }
    auto* poly = reinterpret_cast<std::uint8_t*>(base + kAnimPolyTailRva);
    if (IsExecutableAddress(poly) && BytesMatch(poly, kAnimPolyTailBytes, kAnimPolyTailPatch)) {
        g_anim_poly_tramp_mem = MakeTrampoline(poly, kAnimPolyTailPatch, poly + kAnimPolyTailPatch);
        g_mod_anim_slot_flags_abs = reinterpret_cast<void*>(base + kAnimSlotFlagsRva);
        if (g_anim_poly_tramp_mem &&
            WriteJump(poly, reinterpret_cast<void*>(&ModAnimPolyTailDetour), g_anim_poly_original,
                      kAnimPolyTailPatch)) {
            g_mod_anim_poly_tramp = g_anim_poly_tramp_mem;
            g_anim_poly_site = poly;

            ++ok;
        } else {
            LogWarn("map apply anim polyline count hook failed");
        }
    } else {
        LogWarn("map apply anim polyline count site mismatch at +0x%X",
                static_cast<unsigned>(kAnimPolyTailRva));
    }
    ok += install(kSelectP28ImmRva, kSelectP28ImmPatch, kSelectP28ImmBytes,
                  reinterpret_cast<void*>(&ModSelectP28ImmDetour), &g_mod_select_p28_imm_resume,
                  kSelectP28ImmResumeRva, g_select_p28_imm_original, &g_select_p28_imm_site,
                  "select-p28-imm")
              ? 1
              : 0;
    std::uint8_t select_zoom_expect[6]{};
    EncodeMovEbxAbs(select_zoom_expect, base + kSelectCamPtrRva);
    g_mod_select_cam_abs = reinterpret_cast<void*>(base + kSelectCamPtrRva);
    ok += install(kSelectZoomRva, kSelectZoomPatch, select_zoom_expect,
                  reinterpret_cast<void*>(&ModSelectZoomDetour), &g_mod_select_zoom_resume,
                  kSelectZoomResumeRva, g_select_zoom_original, &g_select_zoom_site,
                  "select-p28")
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
    if (g_sfx_bind_site) {
        RestoreBytes(g_sfx_bind_site, g_sfx_bind_original, kSfxBindCallPatch);
        g_sfx_bind_site = nullptr;
    }
    if (g_sec8_copy_site) {
        RestoreBytes(g_sec8_copy_site, g_sec8_copy_original, kSec8CopyDonePatch);
        g_sec8_copy_site = nullptr;
    }
    if (g_anim_latch_site) {
        RestoreBytes(g_anim_latch_site, g_anim_latch_original, kAnimLatchPatch);
        g_anim_latch_site = nullptr;
    }
    if (g_anim_latch_tramp_mem) {
        VirtualFree(g_anim_latch_tramp_mem, 0, MEM_RELEASE);
        g_anim_latch_tramp_mem = nullptr;
#if defined(_M_IX86)
        g_mod_anim_latch_tramp = nullptr;
#endif
    }
    if (g_anim_poly_site) {
        RestoreBytes(g_anim_poly_site, g_anim_poly_original, kAnimPolyTailPatch);
        g_anim_poly_site = nullptr;
    }
    if (g_anim_poly_tramp_mem) {
        VirtualFree(g_anim_poly_tramp_mem, 0, MEM_RELEASE);
        g_anim_poly_tramp_mem = nullptr;
#if defined(_M_IX86)
        g_mod_anim_poly_tramp = nullptr;
        g_mod_anim_slot_flags_abs = nullptr;
#endif
    }
    if (g_select_zoom_site) {
        RestoreBytes(g_select_zoom_site, g_select_zoom_original, kSelectZoomPatch);
        g_select_zoom_site = nullptr;
    }
    if (g_select_p28_imm_site) {
        RestoreBytes(g_select_p28_imm_site, g_select_p28_imm_original, kSelectP28ImmPatch);
        g_select_p28_imm_site = nullptr;
    }
#if defined(_M_IX86)
    g_mod_select_cam_abs = nullptr;
    g_mod_select_zoom_resume = nullptr;
    g_mod_select_p28_imm_resume = nullptr;
#endif
    g_select_p28 = 0;
    GameFree(g_grown_sec21);
    g_grown_sec21 = nullptr;
    g_mdp_sec21 = nullptr;
    GameFree(g_grown_sec15);
    g_grown_sec15 = nullptr;
    g_sec15_len = 0;
    FreeGrownStreams();
    std::memset(g_sec21_applied, 0, sizeof(g_sec21_applied));
    std::memset(g_sec15_applied, 0, sizeof(g_sec15_applied));
    FreeGrown();
}

}  // namespace grandia_mod
