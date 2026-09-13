#include "game.h"

#include "hook_util.h"
#include "log.h"
#include "party.h"
#include "field_run.h"
#include "travel.h"
#include "virt_file.h"

extern "C" int ModTurboGet();
extern "C" int ModTurboSet(int level);
extern "C" int ModTurboOverrideGet();
extern "C" int ModTurboOverrideSet(int level);
extern "C" int ModEncountersGet();
extern "C" int ModEncountersSet(int off);
extern "C" int ModDebugGet();
extern "C" int ModDebugSet(int on);
extern "C" int ModKeyDown(int vk);
extern "C" int ModScanDown(int scan);
extern "C" int ModPadPoll();
extern "C" int ModOverlayReady();
extern "C" int ModOverlayToast(const char* message, int duration_ms, unsigned rgb);
extern "C" int ModOverlayClearToasts();
extern "C" int ModOverlaySetPanel(const char* joined, const unsigned* rgbs, int count);
extern "C" int ModOverlayClearPanel();
extern "C" int ModOverlayPanelActive();
extern "C" int ModMenuOpen(const char* title, const char* joined, int count);
extern "C" int ModMenuClose();
extern "C" int ModMenuIsOpen();
extern "C" int ModMenuCursor();
extern "C" int ModMenuTakeChoice();
extern "C" int ModMenuTakeCancel();
extern "C" int ModMenuOption();
extern "C" int ModGameStatus();
extern "C" int ModWarpTo(int dest, int spawn, int aux9, int auxA);
extern "C" int ModRunField(int kind, int id, int table, const void* bytes, int len);
extern "C" int ModQuit();
extern "C" int ModXpGet(int kind);
extern "C" int ModXpSet(int kind, int multiplier);
extern "C" int ModOverlayInputOpen(const char* title, const char* initial, int max_len);
extern "C" int ModOverlayInputClose();
extern "C" int ModOverlayInputActive();
extern "C" const char* ModOverlayInputText();
extern "C" const char* ModOverlayInputTake();
extern "C" int ModOverlayInputTakeCancel();
extern "C" int ModSfxEmitterCount();
extern "C" int ModSfxEmitterRange();
extern "C" int ModSfxEmitterGet(int index, int* rec_id, int* sfx, int* flags, int* x, int* y,
                                int* z, int* muted);
extern "C" int ModSfxEmitterMove(int index, int x, int y, int z);
extern "C" int ModSfxEmitterMute(int index, int muted);
extern "C" int ModSfxEmitterSetSfx(int index, int sfx);
extern "C" int ModSfxEmitterAdd(int rec_id, int sfx, int flags, int x, int y, int z, int kind,
                               int period, int bias);
extern "C" int ModSfxEmitterRemove(int index);
extern "C" int ModSfxEmitterSetFlags(int index, int flags);
extern "C" int ModCameraWalkGet(int* x, int* y, int* z);
extern "C" int ModHashPs1Sprite(int tpage, int u, int v, int width, int height, unsigned* key);
extern "C" int ModReadPs1Vram(int which, void* dest, int dest_len);

#include <Windows.h>

#include <cstdint>
#include <cstring>
#include <mutex>

extern "C" {
std::uintptr_t g_mod_gold_base = 0;
void* g_mod_gold_ptr_trampoline = nullptr;
}

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kStashArrayGlobalRva = 0x307FC4u;
constexpr std::uintptr_t kStashHeapBlockGlobalRva = 0x240E64u;
constexpr std::uintptr_t kStashArrayOffsetInHeap = 0x21A0u;
constexpr std::uintptr_t kFlagBlobPtrRva = 0x318BD8u;
constexpr unsigned kGoldValueOffset = 4;
constexpr unsigned kGoldMax = 9999999u;
constexpr int kStashQtyCap = 99;
constexpr std::size_t kGoldHookPatchSize = 5;

std::uintptr_t g_stash_base = 0;
std::uintptr_t g_flag_blob = 0;
unsigned g_pending_gold = 0;
std::mutex g_gold_lock;

void* g_gold_hook_site = nullptr;
std::uint8_t g_gold_hook_original[8]{};
void* g_gold_trampoline_mem = nullptr;

bool IsPlausibleStashBase(std::uintptr_t candidate) {
    if (candidate < 0x10000) {
        return false;
    }
    std::uint8_t probe = 0;
    return SafeReadByte(candidate, &probe) && SafeReadByte(candidate + 0x1FF, &probe);
}

bool AdoptStashBase(std::uintptr_t candidate, const char* reason) {
    if (!IsPlausibleStashBase(candidate)) {
        return false;
    }
    if (g_stash_base != candidate) {
        LogInfo("stash base 0x%08X (%s)%s", static_cast<unsigned>(candidate), reason,
                g_stash_base ? " [updated]" : "");
        g_stash_base = candidate;
    }
    return true;
}

bool EnsureStashBase() {
    const auto base = ModuleBase();
    if (base == 0) {
        return g_stash_base != 0 && IsPlausibleStashBase(g_stash_base);
    }

    void* published = nullptr;
    if (SafeReadPointer(base + kStashArrayGlobalRva, &published) && published) {
        if (AdoptStashBase(reinterpret_cast<std::uintptr_t>(published), "stash array global")) {
            return true;
        }
    }

    void* heap = nullptr;
    if (SafeReadPointer(base + kStashHeapBlockGlobalRva, &heap) && heap) {
        const auto derived = reinterpret_cast<std::uintptr_t>(heap) + kStashArrayOffsetInHeap;
        if (AdoptStashBase(derived, "stash heap+0x21A0")) {
            return true;
        }
    }
    return g_stash_base != 0 && IsPlausibleStashBase(g_stash_base);
}

bool EnsureFlagBlob() {
    if (g_flag_blob != 0) {
        return true;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    void* blob = nullptr;
    if (!SafeReadPointer(base + kFlagBlobPtrRva, &blob) || !blob) {
        return false;
    }
    g_flag_blob = reinterpret_cast<std::uintptr_t>(blob);

    return true;
}

void FlagAddr(unsigned event_id, std::uintptr_t* addr, std::uint8_t* mask) {
    *addr = g_flag_blob + (event_id >> 3);
    *mask = static_cast<std::uint8_t>(1u << (7 - (event_id & 7)));
}

bool ApplyGoldLocked(unsigned amount) {
    if (amount == 0) {
        return true;
    }
    if (g_mod_gold_base == 0) {
        return false;
    }
    const auto address = g_mod_gold_base + kGoldValueOffset;
    std::uint32_t current = 0;
    if (!SafeReadU32(address, &current)) {
        LogWarn("gold read failed at 0x%08X", static_cast<unsigned>(address));
        return false;
    }
    unsigned long long next = static_cast<unsigned long long>(current) + amount;
    if (next > kGoldMax) {
        next = kGoldMax;
    }
    if (!SafeWriteU32(address, static_cast<std::uint32_t>(next))) {
        LogWarn("gold write failed at 0x%08X", static_cast<unsigned>(address));
        return false;
    }
    return true;
}

}  // namespace

void FlushPendingGold() {
    std::lock_guard<std::mutex> lock(g_gold_lock);
    if (g_mod_gold_base == 0 || g_pending_gold == 0) {
        return;
    }
    const unsigned amount = g_pending_gold;
    g_pending_gold = 0;
    ApplyGoldLocked(amount);
}

}  // namespace grandia_mod

#if defined(_M_IX86)

extern "C" void ModOnGoldPtrCaptured() {
    grandia_mod::FlushPendingGold();
}

extern "C" __declspec(naked) void ModGoldPtrDetour() {
    __asm {
        mov dword ptr [g_mod_gold_base], esi
        pushad
        call ModOnGoldPtrCaptured
        popad
        jmp dword ptr [g_mod_gold_ptr_trampoline]
    }
}

#endif

namespace grandia_mod {
namespace {

bool InstallGoldPtrHook() {
#if !defined(_M_IX86)
    return false;
#else
    HMODULE module = GetModuleHandleW(nullptr);
    const int pat[] = {0x6A, 0x05, 0x8A, 0x46, -1, 0x88, 0x44, 0x24, -1};
    const auto site = ScanExecutable(module, pat, 9);
    if (!site) {
        LogWarn("GetGoldPtr AOB not found — Gold.Add waits for a field gold chest");
        return false;
    }
    const auto* bytes = reinterpret_cast<const std::uint8_t*>(site);
    if (bytes[0] != 0x6A || bytes[1] != 0x05 || bytes[2] != 0x8A || bytes[3] != 0x46) {
        LogWarn("GetGoldPtr bytes mismatch at 0x%08X", static_cast<unsigned>(site));
        return false;
    }

    g_gold_trampoline_mem = VirtualAlloc(nullptr, 32, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!g_gold_trampoline_mem) {
        return false;
    }
    auto* tramp = reinterpret_cast<std::uint8_t*>(g_gold_trampoline_mem);
    std::memcpy(tramp, bytes, kGoldHookPatchSize);
    tramp[kGoldHookPatchSize] = 0xE9;
    const auto resume = site + kGoldHookPatchSize;
    const auto rel =
        static_cast<std::int32_t>(resume - (reinterpret_cast<std::uintptr_t>(tramp) + kGoldHookPatchSize + 5));
    std::memcpy(tramp + kGoldHookPatchSize + 1, &rel, sizeof(rel));
    g_mod_gold_ptr_trampoline = g_gold_trampoline_mem;

    g_gold_hook_site = reinterpret_cast<void*>(site);
    if (!WriteJump(g_gold_hook_site, reinterpret_cast<void*>(&ModGoldPtrDetour), g_gold_hook_original,
                   kGoldHookPatchSize)) {
        VirtualFree(g_gold_trampoline_mem, 0, MEM_RELEASE);
        g_gold_trampoline_mem = nullptr;
        g_mod_gold_ptr_trampoline = nullptr;
        g_gold_hook_site = nullptr;
        LogWarn("failed to install GetGoldPtr hook");
        return false;
    }

    return true;
#endif
}

}  // namespace

void AdoptGoldBase(std::uintptr_t gold_ptr) {
    if (gold_ptr < 0x10000) {
        return;
    }
    {
        std::lock_guard<std::mutex> lock(g_gold_lock);
        if (g_mod_gold_base == 0) {
            g_mod_gold_base = gold_ptr;
            LogInfo("gold base 0x%08X (field gold EAX)", static_cast<unsigned>(gold_ptr));
        }
    }
    FlushPendingGold();
}

void AdoptFlagBlob(std::uintptr_t blob) {
    if (blob < 0x10000) {
        return;
    }
    if (g_flag_blob == 0) {
        g_flag_blob = blob;

    }
}

void ResetLiveSavePointers() {
    {
        std::lock_guard<std::mutex> lock(g_gold_lock);
        g_mod_gold_base = 0;
    }
    g_stash_base = 0;

}

extern "C" int ModStashAdd(int item_id, int delta) {
    if (item_id <= 0 || delta == 0) {
        return 0;
    }
    if (!EnsureStashBase()) {
        LogWarn("Stash.Add item=%d — stash not ready (load a save)", item_id);
        return 0;
    }
    const auto addr = g_stash_base + static_cast<std::uintptr_t>(item_id - 1);
    std::uint8_t before = 0;
    if (!SafeReadByte(addr, &before)) {
        LogWarn("Stash.Add cannot read item=%d", item_id);
        return 0;
    }
    int next = static_cast<int>(before) + delta;
    if (next < 0) {
        next = 0;
    }
    if (next > kStashQtyCap) {
        next = kStashQtyCap;
    }
    if (next == before) {
        return 1;
    }
    if (!SafeWriteByte(addr, static_cast<std::uint8_t>(next))) {
        return 0;
    }
    LogInfo("Stash.Add item=%d qty %u -> %d", item_id, before, next);
    return 1;
}

extern "C" int ModStashGet(int item_id) {
    if (item_id <= 0 || !EnsureStashBase()) {
        return -1;
    }
    std::uint8_t qty = 0;
    if (!SafeReadByte(g_stash_base + static_cast<std::uintptr_t>(item_id - 1), &qty)) {
        return -1;
    }
    return qty;
}

extern "C" int ModGoldAdd(int amount) {
    if (amount <= 0) {
        return amount == 0 ? 1 : 0;
    }
    std::lock_guard<std::mutex> lock(g_gold_lock);
    if (g_mod_gold_base == 0) {
        const unsigned long long queued = static_cast<unsigned long long>(g_pending_gold) +
                                          static_cast<unsigned>(amount);
        g_pending_gold = queued > kGoldMax ? kGoldMax : static_cast<unsigned>(queued);
        LogInfo("Gold.Add +%d queued (open Status once); pending=%u", amount, g_pending_gold);
        return 1;
    }
    return ApplyGoldLocked(static_cast<unsigned>(amount)) ? 1 : 0;
}

extern "C" int ModGoldGet() {
    if (g_mod_gold_base == 0) {
        return -1;
    }
    std::uint32_t current = 0;
    if (!SafeReadU32(g_mod_gold_base + kGoldValueOffset, &current)) {
        return -1;
    }
    return static_cast<int>(current);
}

extern "C" int ModFlagGet(unsigned event_id) {
    if (event_id == 0 || !EnsureFlagBlob()) {
        return -1;
    }
    std::uintptr_t addr = 0;
    std::uint8_t mask = 0;
    FlagAddr(event_id, &addr, &mask);
    std::uint8_t value = 0;
    if (!SafeReadByte(addr, &value)) {
        return -1;
    }
    return (value & mask) != 0 ? 1 : 0;
}

extern "C" int ModFlagSet(unsigned event_id, int value) {
    if (event_id == 0 || !EnsureFlagBlob()) {
        return 0;
    }
    std::uintptr_t addr = 0;
    std::uint8_t mask = 0;
    FlagAddr(event_id, &addr, &mask);
    std::uint8_t current = 0;
    if (!SafeReadByte(addr, &current)) {
        return 0;
    }
    const std::uint8_t next = value ? static_cast<std::uint8_t>(current | mask)
                                    : static_cast<std::uint8_t>(current & static_cast<std::uint8_t>(~mask));
    if (next == current) {
        return 1;
    }
    if (!SafeWriteByte(addr, next)) {
        return 0;
    }

    return 1;
}

extern "C" int ModPartyWalkGet(int* x, int* y, int* z) {
    // Same walk XYZ +0x53320 uses for table-1 AABBs: actor =
    // [0x71CD28] + input[0x71CD1C]+2 * 0xA0, 16.16 at +0x68/+0x6C/+0x70.
    constexpr std::uintptr_t kInputPtrRva = 0x31CD1Cu;
    constexpr std::uintptr_t kActorBaseRva = 0x31CD28u;
    constexpr unsigned kActorStride = 0xA0u;
    constexpr unsigned kPosX = 0x68u;
    constexpr unsigned kMaxSlot = 15u;

    if (!x || !y || !z) {
        return 0;
    }
    *x = *y = *z = 0;
    const auto base = ModuleBase();
    if (base == 0) {
        return 0;
    }
    void* input = nullptr;
    void* actors = nullptr;
    if (!SafeReadPointer(base + kInputPtrRva, &input) || !input ||
        !SafeReadPointer(base + kActorBaseRva, &actors) || !actors) {
        return 0;
    }
    std::uint8_t slot = 0;
    if (!SafeReadByte(reinterpret_cast<std::uintptr_t>(input) + 2u, &slot) || slot > kMaxSlot) {
        return 0;
    }
    const auto actor = reinterpret_cast<std::uintptr_t>(actors) +
                       static_cast<std::uintptr_t>(slot) * kActorStride;

    auto walk_axis = [](std::uintptr_t addr, int* out) -> bool {
        std::uint32_t raw = 0;
        if (!SafeReadU32(addr, &raw)) {
            return false;
        }
        const auto rounded = static_cast<std::int32_t>(raw) + 0x8000;
        *out = static_cast<int>(static_cast<std::int16_t>(rounded >> 16));
        return true;
    };
    if (!walk_axis(actor + kPosX, x) || !walk_axis(actor + kPosX + 4u, y) ||
        !walk_axis(actor + kPosX + 8u, z)) {
        return 0;
    }
    return 1;
}

extern "C" int ModCameraWalkGet(int* x, int* y, int* z) {
    // Field camera look-at 16.16. Preferred image 0x400000 → VA
    // 0x71931C / 0x719320 / 0x719324. SFX tick +0x2CD0 shifts these
    // into listener 71E840 / 71E858 / 71E83C (plus a small look-ahead).
    constexpr std::uintptr_t kCamXRva = 0x31931Cu;
    constexpr std::uintptr_t kCamYRva = 0x319320u;
    constexpr std::uintptr_t kCamZRva = 0x319324u;
    if (!x || !y || !z) {
        return 0;
    }
    *x = *y = *z = 0;
    const auto base = ModuleBase();
    if (base == 0) {
        return 0;
    }
    auto axis = [](std::uintptr_t addr, int* out) -> bool {
        std::uint32_t raw = 0;
        if (!SafeReadU32(addr, &raw)) {
            return false;
        }
        const auto rounded = static_cast<std::int32_t>(raw) + 0x8000;
        *out = static_cast<int>(static_cast<std::int16_t>(rounded >> 16));
        return true;
    };
    if (!axis(base + kCamXRva, x) || !axis(base + kCamYRva, y) || !axis(base + kCamZRva, z)) {
        return 0;
    }
    return 1;
}

extern "C" int ModGameStatus() {
    constexpr std::uintptr_t kMenuModeRva = 0x31942Cu;
    constexpr std::uintptr_t kBattleModeRva = 0x31CD4Bu;
    const auto base = ModuleBase();
    if (base == 0) {
        return 0;
    }
    std::uint8_t battle = 0;
    if (SafeReadByte(base + kBattleModeRva, &battle) && (battle == 2 || battle == 3)) {
        return 3;
    }
    std::uint32_t menu = 0;
    if (SafeReadU32(base + kMenuModeRva, &menu) && menu == 3) {
        return 2;
    }
    if (menu == 2 || ModMenuIsOpen() != 0) {
        return 1;
    }
    return 0;
}

extern "C" int ModWarpTo(int dest, int spawn, int aux9, int auxA) {
    return QueueMapTravel(static_cast<unsigned>(dest), static_cast<unsigned>(spawn),
                          static_cast<unsigned>(aux9), static_cast<unsigned>(auxA));
}

extern "C" int ModRunField(int kind, int id, int table, const void* bytes, int len) {
    return QueueFieldRun(kind, id, table, bytes, len);
}

namespace {

// Live sec[29] emitter table. Mixer walks [0x640E4C] (copy+8). Preferred
// image base 0x400000 — never match on-disk immediates.
constexpr std::uintptr_t kSfxTablePtrRva = 0x240E4Cu;
constexpr unsigned kSfxRecSize = 0x10u;
constexpr int kSfxMaxEmitters = 250;
constexpr int kSfxHeapLiveMax = 62;  // (1024-8)/16 - terminator

bool ReadS16(std::uintptr_t addr, int* out) {
    std::uint8_t lo = 0;
    std::uint8_t hi = 0;
    if (!out || !SafeReadByte(addr, &lo) || !SafeReadByte(addr + 1, &hi)) {
        return false;
    }
    *out = static_cast<int>(static_cast<std::int16_t>(lo | (static_cast<unsigned>(hi) << 8)));
    return true;
}

bool WriteS16(std::uintptr_t addr, int value) {
    if (value < -32768) {
        value = -32768;
    }
    if (value > 32767) {
        value = 32767;
    }
    const auto w = static_cast<std::uint16_t>(static_cast<std::int16_t>(value));
    return SafeWriteByte(addr, static_cast<std::uint8_t>(w & 0xFF)) &&
           SafeWriteByte(addr + 1, static_cast<std::uint8_t>(w >> 8));
}

std::uintptr_t SfxTableBase() {
    const auto base = ModuleBase();
    if (base == 0) {
        return 0;
    }
    void* table = nullptr;
    if (!SafeReadPointer(base + kSfxTablePtrRva, &table) || !table) {
        return 0;
    }
    const auto addr = reinterpret_cast<std::uintptr_t>(table);
    if (addr < 0x10000) {
        return 0;
    }
    std::uint8_t probe = 0;
    if (!SafeReadByte(addr, &probe)) {
        return 0;
    }
    return addr;
}

std::uintptr_t SfxRecord(int index) {
    if (index < 0 || index >= kSfxMaxEmitters) {
        return 0;
    }
    const auto table = SfxTableBase();
    if (table == 0) {
        return 0;
    }
    const auto rec = table + static_cast<unsigned>(index) * kSfxRecSize;
    std::uint8_t rec_id = 0;
    if (!SafeReadByte(rec, &rec_id) || rec_id == 0xFF) {
        return 0;
    }
    return rec;
}

int SfxLiveCount() {
    const auto table = SfxTableBase();
    if (table == 0) {
        return 0;
    }
    int n = 0;
    for (; n < kSfxMaxEmitters; ++n) {
        std::uint8_t rec_id = 0;
        if (!SafeReadByte(table + static_cast<unsigned>(n) * kSfxRecSize, &rec_id) ||
            rec_id == 0xFF) {
            break;
        }
    }
    return n;
}

}  // namespace

extern "C" int ModSfxEmitterCount() {
    return SfxLiveCount();
}

extern "C" int ModSfxEmitterRange() {
    const auto table = SfxTableBase();
    if (table < 8) {
        return 0;
    }
    std::uint32_t range = 0;
    if (!SafeReadU32(table - 4, &range)) {
        return 0;
    }
    if (range > 0x10000u) {
        return 0;
    }
    return static_cast<int>(range);
}

extern "C" int ModSfxEmitterGet(int index, int* rec_id, int* sfx, int* flags, int* x, int* y,
                                int* z, int* muted) {
    const auto rec = SfxRecord(index);
    if (rec == 0) {
        return 0;
    }
    std::uint8_t idb = 0;
    std::uint8_t kind = 0;
    std::uint8_t sfxb = 0;
    std::uint8_t flg = 0;
    int px = 0;
    int py = 0;
    int pz = 0;
    if (!SafeReadByte(rec, &idb) || !SafeReadByte(rec + 2, &kind) ||
        !SafeReadByte(rec + 3, &sfxb) || !SafeReadByte(rec + 4, &flg) || !ReadS16(rec + 0xA, &px) ||
        !ReadS16(rec + 0xC, &py) || !ReadS16(rec + 0xE, &pz)) {
        return 0;
    }
    if (rec_id) {
        *rec_id = idb;
    }
    if (sfx) {
        *sfx = sfxb;
    }
    if (flags) {
        *flags = flg;
    }
    if (x) {
        *x = px;
    }
    if (y) {
        *y = py;
    }
    if (z) {
        *z = pz;
    }
    if (muted) {
        *muted = (kind & 0x80) == 0 ? 1 : 0;
    }
    return 1;
}

extern "C" int ModSfxEmitterMove(int index, int x, int y, int z) {
    const auto rec = SfxRecord(index);
    if (rec == 0) {
        return 0;
    }
    if (!WriteS16(rec + 0xA, x) || !WriteS16(rec + 0xC, y) || !WriteS16(rec + 0xE, z)) {
        return 0;
    }
    return 1;
}

extern "C" int ModSfxEmitterMute(int index, int muted) {
    const auto rec = SfxRecord(index);
    if (rec == 0) {
        return 0;
    }
    std::uint8_t kind = 0;
    if (!SafeReadByte(rec + 2, &kind)) {
        return 0;
    }
    const std::uint8_t next =
        muted ? static_cast<std::uint8_t>(kind & 0x7F) : static_cast<std::uint8_t>(kind | 0x80);
    if (next == kind) {
        return 1;
    }
    return SafeWriteByte(rec + 2, next) ? 1 : 0;
}

extern "C" int ModSfxEmitterSetSfx(int index, int sfx) {
    const auto rec = SfxRecord(index);
    if (rec == 0) {
        return 0;
    }
    if (sfx < 0) {
        sfx = 0;
    }
    if (sfx > 255) {
        sfx = 255;
    }
    return SafeWriteByte(rec + 3, static_cast<std::uint8_t>(sfx)) ? 1 : 0;
}

static int SfxClampU8(int value) {
    if (value < 0) {
        return 0;
    }
    if (value > 255) {
        return 255;
    }
    return value;
}

static int NextSfxRecId(std::uintptr_t table, int live) {
    bool used[256]{};
    used[0xF4] = true;
    used[0xFF] = true;
    for (int i = 0; i < live; ++i) {
        std::uint8_t id = 0;
        if (SafeReadByte(table + static_cast<unsigned>(i) * kSfxRecSize, &id)) {
            used[id] = true;
        }
    }
    for (int id = 0; id < 0xFF; ++id) {
        if (!used[id]) {
            return id;
        }
    }
    return 0;
}

extern "C" int ModSfxEmitterAdd(int rec_id, int sfx, int flags, int x, int y, int z, int kind,
                               int period, int bias) {
    const auto table = SfxTableBase();
    if (table == 0) {
        return -1;
    }
    const int n = SfxLiveCount();
    if (n < 0 || n >= kSfxHeapLiveMax) {
        return -1;
    }
    const auto rec = table + static_cast<unsigned>(n) * kSfxRecSize;
    int id = rec_id;
    if (id <= 0 || id >= 0xFF || id == 0xF4) {
        id = NextSfxRecId(table, n);
    }
    if (kind == 0) {
        kind = 0x81;
    }
    if (!SafeWriteByte(rec, static_cast<std::uint8_t>(SfxClampU8(id))) ||
        !SafeWriteByte(rec + 1, 0xFF) ||
        !SafeWriteByte(rec + 2, static_cast<std::uint8_t>(SfxClampU8(kind))) ||
        !SafeWriteByte(rec + 3, static_cast<std::uint8_t>(SfxClampU8(sfx))) ||
        !SafeWriteByte(rec + 4, static_cast<std::uint8_t>(SfxClampU8(flags))) ||
        !SafeWriteByte(rec + 5, static_cast<std::uint8_t>(SfxClampU8(period))) ||
        !SafeWriteByte(rec + 6, 0xFF) ||
        !SafeWriteByte(rec + 7, static_cast<std::uint8_t>(SfxClampU8(bias))) ||
        !WriteS16(rec + 0xA, x) || !WriteS16(rec + 0xC, y) || !WriteS16(rec + 0xE, z) ||
        !SafeWriteByte(rec + kSfxRecSize, 0xFF)) {
        return -1;
    }
    return n;
}

extern "C" int ModSfxEmitterRemove(int index) {
    const auto table = SfxTableBase();
    if (table == 0) {
        return 0;
    }
    const int n = SfxLiveCount();
    if (index < 0 || index >= n) {
        return 0;
    }
    for (int i = index; i < n - 1; ++i) {
        const auto dst = table + static_cast<unsigned>(i) * kSfxRecSize;
        const auto src = table + static_cast<unsigned>(i + 1) * kSfxRecSize;
        for (unsigned b = 0; b < kSfxRecSize; ++b) {
            std::uint8_t v = 0;
            if (!SafeReadByte(src + b, &v) || !SafeWriteByte(dst + b, v)) {
                return 0;
            }
        }
    }
    return SafeWriteByte(table + static_cast<unsigned>(n - 1) * kSfxRecSize, 0xFF) ? 1 : 0;
}

extern "C" int ModSfxEmitterSetFlags(int index, int flags) {
    const auto rec = SfxRecord(index);
    if (rec == 0) {
        return 0;
    }
    return SafeWriteByte(rec + 4, static_cast<std::uint8_t>(SfxClampU8(flags))) ? 1 : 0;
}

extern "C" int ModHashPs1Sprite(int tpage, int u, int v, int width, int height, unsigned* key) {
    if (!key || width <= 0 || height <= 0 || u < 0 || v < 0) {
        return 0;
    }
    *key = 0;

    const int bpp = (tpage & 0x180) == 0 ? 4 : 2;
    const int page_x = (tpage & 0xF) << 6;
    const int page_y = (tpage & 0x10) << 4;
    const int vram_x = page_x + u / bpp;
    const int vram_y = page_y + v;
    const int word_w = (width + 1) / bpp;
    if (word_w <= 0 || vram_x < 0 || vram_y < 0 || word_w > 256 || height > 256
        || vram_y + height > 512 || vram_x + word_w > 1024) {
        return 0;
    }

    void* vram = nullptr;
    if (!SafeReadPointer(ModuleBase() + 0x23F880u, &vram) || !vram) {
        return 0;
    }

    auto hash_buf = [&](void* buf) -> int {
        const auto base = reinterpret_cast<std::uintptr_t>(buf);
        constexpr unsigned kOff = 0x811C9DC5u;
        constexpr unsigned kPrime = 0x01000193u;
        unsigned hash = 0;
        bool any = false;
        for (int row = 0; row < height; ++row) {
            auto at = base + static_cast<std::uintptr_t>((vram_y + row) * 1024 + vram_x) * 2u;
            for (int col = 0; col < word_w; ++col) {
                std::uint8_t lo = 0;
                std::uint8_t hi = 0;
                if (!SafeReadByte(at, &lo) || !SafeReadByte(at + 1, &hi)) {
                    return 0;
                }
                at += 2;
                if (lo != 0 || hi != 0) {
                    any = true;
                }
                if (hash == 0) {
                    hash = kOff;
                }
                hash ^= lo;
                hash *= kPrime;
                if (hash == 0) {
                    hash = kOff;
                }
                hash ^= hi;
                hash *= kPrime;
            }
        }
        if (!any || hash == 0) {
            return 0;
        }
        *key = hash;
        return 1;
    };

    if (hash_buf(vram)) {
        return 1;
    }
    void* vram1 = nullptr;
    if (SafeReadPointer(ModuleBase() + 0x23F884u, &vram1) && vram1 && hash_buf(vram1)) {
        return 1;
    }
    return 0;
}

extern "C" int ModReadPs1Vram(int which, void* dest, int dest_len) {
    if (!dest || dest_len < 0x100000) {
        return 0;
    }
    void* vram = nullptr;
    const auto rva = which == 1 ? 0x23F884u : 0x23F880u;
    if (!SafeReadPointer(ModuleBase() + rva, &vram) || !vram) {
        return 0;
    }
    __try {
        std::memcpy(dest, vram, 0x100000);
        return 0x100000;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return 0;
    }
}

namespace {

BOOL CALLBACK PickProcessWindow(HWND hwnd, LPARAM lp) {
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != GetCurrentProcessId() || !IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER)) {
        return TRUE;
    }
    *reinterpret_cast<HWND*>(lp) = hwnd;
    return FALSE;
}

DWORD WINAPI ExitProcessSoon(LPVOID) {
    Sleep(400);
    ExitProcess(0);
}

}  // namespace

extern "C" int ModQuit() {
    grandia_mod::LogInfo("Game.Quit");
    HWND wnd = nullptr;
    EnumWindows(PickProcessWindow, reinterpret_cast<LPARAM>(&wnd));
    if (wnd) {
        PostMessageW(wnd, WM_CLOSE, 0, 0);
    }
    if (HANDLE thread = CreateThread(nullptr, 0, ExitProcessSoon, nullptr, 0, nullptr)) {
        CloseHandle(thread);
    } else {
        ExitProcess(0);
    }
    return 1;
}

void FillHostApi(HostApiNative* api) {
    if (!api) {
        return;
    }
    api->stash_add = &ModStashAdd;
    api->stash_get = &ModStashGet;
    api->gold_add = &ModGoldAdd;
    api->gold_get = &ModGoldGet;
    api->flag_get = &ModFlagGet;
    api->flag_set = &ModFlagSet;
    api->party_get = &ModPartyGet;
    api->party_set_ids = &ModPartySetIds;
    api->turbo_get = &ModTurboGet;
    api->turbo_set = &ModTurboSet;
    api->turbo_override_get = &ModTurboOverrideGet;
    api->turbo_override_set = &ModTurboOverrideSet;
    api->encounters_get = &ModEncountersGet;
    api->encounters_set = &ModEncountersSet;
    api->debug_get = &ModDebugGet;
    api->debug_set = &ModDebugSet;
    api->key_down = &ModKeyDown;
    api->scan_down = &ModScanDown;
    api->pad_poll = &ModPadPoll;
    api->overlay_ready = &ModOverlayReady;
    api->overlay_toast = &ModOverlayToast;
    api->overlay_clear_toasts = &ModOverlayClearToasts;
    api->overlay_set_panel = &ModOverlaySetPanel;
    api->overlay_clear_panel = &ModOverlayClearPanel;
    api->overlay_panel_active = &ModOverlayPanelActive;
    api->party_walk_get = &ModPartyWalkGet;
    api->set_text1 = &ModSetText1;
    api->menu_open = &ModMenuOpen;
    api->menu_close = &ModMenuClose;
    api->menu_is_open = &ModMenuIsOpen;
    api->menu_cursor = &ModMenuCursor;
    api->menu_take_choice = &ModMenuTakeChoice;
    api->menu_take_cancel = &ModMenuTakeCancel;
    api->menu_option = &ModMenuOption;
    api->status_get = &ModGameStatus;
    api->warp_to = &ModWarpTo;
    api->overlay_input_open = &ModOverlayInputOpen;
    api->overlay_input_close = &ModOverlayInputClose;
    api->overlay_input_active = &ModOverlayInputActive;
    api->overlay_input_text = &ModOverlayInputText;
    api->overlay_input_take = &ModOverlayInputTake;
    api->overlay_input_take_cancel = &ModOverlayInputTakeCancel;
    api->sfx_emitter_count = &ModSfxEmitterCount;
    api->sfx_emitter_range = &ModSfxEmitterRange;
    api->sfx_emitter_get = &ModSfxEmitterGet;
    api->sfx_emitter_move = &ModSfxEmitterMove;
    api->sfx_emitter_mute = &ModSfxEmitterMute;
    api->sfx_emitter_set_sfx = &ModSfxEmitterSetSfx;
    api->sfx_emitter_add = &ModSfxEmitterAdd;
    api->sfx_emitter_remove = &ModSfxEmitterRemove;
    api->sfx_emitter_set_flags = &ModSfxEmitterSetFlags;
    api->camera_walk_get = &ModCameraWalkGet;
    api->hash_ps1_sprite = &ModHashPs1Sprite;
    api->read_ps1_vram = &ModReadPs1Vram;
    api->run_field = &ModRunField;
    api->quit = &ModQuit;
    api->xp_get = &ModXpGet;
    api->xp_set = &ModXpSet;
}

bool InstallGameServices() {
    EnsureFlagBlob();
    InstallGoldPtrHook();
    return true;
}

void RemoveGameServices() {
    if (g_gold_hook_site) {
        RestoreBytes(g_gold_hook_site, g_gold_hook_original, kGoldHookPatchSize);
        g_gold_hook_site = nullptr;
    }
    if (g_gold_trampoline_mem) {
        VirtualFree(g_gold_trampoline_mem, 0, MEM_RELEASE);
        g_gold_trampoline_mem = nullptr;
        g_mod_gold_ptr_trampoline = nullptr;
    }
}

}  // namespace grandia_mod
