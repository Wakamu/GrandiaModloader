#include "game.h"

#include "hook_util.h"
#include "log.h"
#include "party.h"
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
    if (g_stash_base == 0) {
        g_stash_base = candidate;
        LogInfo("stash base 0x%08X (%s)", static_cast<unsigned>(candidate), reason);
        return true;
    }
    return g_stash_base == candidate;
}

bool EnsureStashBase() {
    if (g_stash_base != 0) {
        return true;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
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
    return false;
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
    LogInfo("flag blob 0x%08X ([+0x318BD8])", static_cast<unsigned>(g_flag_blob));
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
    LogInfo("Gold.Add capture at grandia.exe+0x%X (open Status, or pick a gold chest)",
            static_cast<unsigned>(site - ModuleBase()));
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
        LogInfo("flag blob 0x%08X (flag-write ESI)", static_cast<unsigned>(blob));
    }
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
    LogInfo("Flags.Set event=0x%04X %s", event_id, value ? "on" : "off");
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
}

bool InstallGameServices() {
    EnsureFlagBlob();
    EnsureStashBase();
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
