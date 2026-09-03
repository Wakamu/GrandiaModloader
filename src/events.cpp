#include "events.h"

#include "script_redirect.h"

#include "clr_host.h"
#include "wm_picture.h"
#include "game.h"
#include "hook_util.h"
#include "log.h"
#include "party.h"
#include "qol.h"
#include "save.h"
#include "title.h"
#include "catalog.h"
#include "movie_skip.h"
#include "travel.h"

#include <Windows.h>

#include <atomic>
#include <cstdint>
#include <cstring>

namespace grandia_mod {
namespace {

// Map-travel / init callers — do not raise OnEventFlag (bulk 0x1D80–0x1DFF and friends).
constexpr std::uintptr_t kNoiseFlagCallerRvas[] = {
    0x61487u, 0x5AE35u, 0x51474u, 0x514A0u, 0x54244u, 0x59293u, 0x6156Du, 0x5922Fu, 0x55F4Cu,
};

constexpr std::uintptr_t kChestLootCallerRva = 0x53C45u;
constexpr std::uintptr_t kStoryScriptCallerRva = 0x6F03Fu;
constexpr std::uintptr_t kStoryScriptAltCallerRva = 0x7CB4Fu;

constexpr std::uintptr_t kAssignUiEntryRva = 0x1DC100u;
constexpr std::uintptr_t kAssignUiReturnRva = 0x61E0Du;
constexpr std::size_t kAssignUiEntryPatchSize = 6;
constexpr std::uint8_t kAssignUiEntryBytes[] = {0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8};

constexpr std::uintptr_t kFieldGoldAddRva = 0x7612Eu;
constexpr std::size_t kFieldGoldAddInsnSize = 3;
constexpr std::size_t kFieldGoldCallInsnSize = 5;
constexpr std::size_t kFieldGoldStolenSize = kFieldGoldAddInsnSize + kFieldGoldCallInsnSize;
constexpr std::size_t kFieldGoldPatchSize = 5;
constexpr std::uint8_t kFieldGoldAddBytes[] = {0x01, 0x50, 0x04};
constexpr std::uint8_t kFieldGoldCallOpcode = 0xE8;

constexpr std::uintptr_t kChestFlagWriteRvaSteam = 0x70505u;
constexpr std::uintptr_t kChestFlagWriteRvaAlt = 0x90505u;
constexpr std::size_t kChestFlagWriteInsnSize = 3;
constexpr std::size_t kChestFlagPostMovSkip = 8;
constexpr std::uint8_t kChestFlagWriteBytes[] = {0x88, 0x04, 0x32};
constexpr std::uint8_t kChestFlagWritePostOpcode = 0xA1;

constexpr unsigned kBulkMapInitEventMin = 0x1D80u;
constexpr unsigned kBulkMapInitEventMax = 0x1DFFu;

constexpr int kKindOther = 0;
constexpr int kKindLoot = 1;
constexpr int kKindStory = 2;
constexpr int kKindMapInit = 3;

void* g_chest_flag_site = nullptr;
std::uint8_t g_chest_flag_original[8]{};
void* g_assign_ui_site = nullptr;
std::uint8_t g_assign_ui_original[8]{};
void* g_field_gold_site = nullptr;
std::uint8_t g_field_gold_original[8]{};
void* g_field_gold_trampoline_mem = nullptr;

std::atomic<unsigned> g_event_log_left{32};

volatile unsigned g_pending_loot_event = 0;
volatile int g_arm_skip_assign = 0;
volatile int g_arm_suppress_gold = 0;

bool IsLootCaller(std::uintptr_t rva) {
    return IsNearRva(rva, kChestLootCallerRva);
}

bool IsStoryCaller(std::uintptr_t rva) {
    return IsNearRva(rva, kStoryScriptCallerRva) || IsNearRva(rva, kStoryScriptAltCallerRva);
}

bool IsNoiseCaller(std::uintptr_t rva) {
    for (const std::uintptr_t noise : kNoiseFlagCallerRvas) {
        if (IsNearRva(rva, noise)) {
            return true;
        }
    }
    return false;
}

int ClassifyCaller(std::uintptr_t rva) {
    if (IsLootCaller(rva)) {
        return kKindLoot;
    }
    if (IsStoryCaller(rva)) {
        return kKindStory;
    }
    if (IsNoiseCaller(rva)) {
        return kKindMapInit;
    }
    return kKindOther;
}

bool ShouldRaiseEventFlag(unsigned event_id, unsigned mask, int kind) {
    if (kind == kKindMapInit) {
        return false;
    }
    if (event_id >= kBulkMapInitEventMin && event_id <= kBulkMapInitEventMax) {
        return false;
    }
    if (kind == kKindLoot) {
        return true;
    }
    return (mask & 0xFFu) != 0;
}

}  // namespace

void OnFlagWrite(unsigned event_id, unsigned flag_offset, unsigned flag_value, unsigned mask,
                 unsigned ecx_index, std::uintptr_t caller, std::uintptr_t save_base) {
    AdoptFlagBlob(save_base);
    const std::uintptr_t caller_rva = CallerRva(caller);
    const int kind = ClassifyCaller(caller_rva);
    if (!ShouldRaiseEventFlag(event_id, mask, kind)) {
        return;
    }

    EventFlagNative req{};
    req.event_id = event_id;
    req.flag_offset = flag_offset;
    req.flag_value = flag_value;
    req.mask = mask;
    req.ecx_index = ecx_index;
    req.caller_rva = static_cast<std::uint32_t>(caller_rva);
    req.kind = kind;
    req.suppress_loot = 0;
    req.suppress_gold = 0;

    if (RuntimeOnEventFlag(&req) != 0) {
        return;
    }

    if (kind == kKindLoot) {
        g_pending_loot_event = event_id;
        g_arm_skip_assign = req.suppress_loot;
        g_arm_suppress_gold = req.suppress_gold;
    }

    const unsigned left = g_event_log_left.load();
    if (left > 0 && g_event_log_left.fetch_sub(1) > 0) {
        LogInfo("OnEventFlag event=0x%04X kind=%d caller=+0x%X mask=0x%02X loot=%d gold=%d", event_id,
                kind, static_cast<unsigned>(caller_rva), mask & 0xFFu, req.suppress_loot,
                req.suppress_gold);
    } else if (req.suppress_loot || req.suppress_gold) {
        LogInfo("OnEventFlag event=0x%04X suppress loot=%d gold=%d", event_id, req.suppress_loot,
                req.suppress_gold);
    }
}

int OnAssignUi(std::uintptr_t return_addr) {
    const std::uintptr_t return_rva = CallerRva(return_addr);
    if (!IsNearRva(return_rva, kAssignUiReturnRva)) {
        return 0;
    }

    ItemAssignNative req{};
    req.event_id = g_pending_loot_event;
    req.return_rva = static_cast<std::uint32_t>(return_rva);
    req.skip_vanilla = g_arm_skip_assign;

    if (RuntimeOnItemAssignUi(&req) != 0) {
        return 0;
    }

    if (req.skip_vanilla) {
        LogInfo("OnItemAssignUi skip vanilla event=0x%04X", req.event_id);
        g_arm_suppress_gold = 0;
        g_pending_loot_event = 0;
        g_arm_skip_assign = 0;
        return 1;
    }

    g_arm_skip_assign = 0;
    return 0;
}

int OnFieldGold(int amount) {
    FieldGoldNative req{};
    req.event_id = g_pending_loot_event;
    req.amount = g_arm_suppress_gold ? 0 : amount;

    if (RuntimeOnFieldGoldAdd(&req) != 0) {
        return amount;
    }

    if (req.amount != amount) {
        LogInfo("OnFieldGoldAdd event=0x%04X amount %d -> %d", req.event_id, amount, req.amount);
    }

    if (g_pending_loot_event != 0 && g_arm_suppress_gold) {
        if (g_pending_loot_event == static_cast<unsigned>(req.event_id) || req.amount == 0) {
            g_pending_loot_event = 0;
        }
    }
    g_arm_suppress_gold = 0;
    return req.amount;
}

namespace {

bool IsChestFlagWriteSite(std::uintptr_t site) {
    if (!IsExecutableAddress(reinterpret_cast<void*>(site))) {
        return false;
    }
    const auto* bytes = reinterpret_cast<const std::uint8_t*>(site);
    if (!BytesMatch(bytes, kChestFlagWriteBytes, sizeof(kChestFlagWriteBytes))) {
        return false;
    }
    return bytes[kChestFlagWriteInsnSize] == kChestFlagWritePostOpcode;
}

bool PatternMatch(const std::uint8_t* data, const int* pat, std::size_t n) {
    for (std::size_t i = 0; i < n; ++i) {
        if (pat[i] >= 0 && data[i] != static_cast<std::uint8_t>(pat[i])) {
            return false;
        }
    }
    return true;
}

std::uintptr_t ScanChestFlagSite(HMODULE module) {
    const int pat[] = {0x88, 0x04, 0x32, 0xA1, 0x18, 0x24, -1, 0x00};
    constexpr std::size_t n = 8;
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(module);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
        return 0;
    }
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(reinterpret_cast<std::uint8_t*>(module) + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) {
        return 0;
    }
    auto* base = reinterpret_cast<std::uint8_t*>(module);
    auto* section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
        if ((section[i].Characteristics & IMAGE_SCN_MEM_EXECUTE) == 0) {
            continue;
        }
        const auto* start = base + section[i].VirtualAddress;
        const std::size_t size = section[i].Misc.VirtualSize;
        if (size < n) {
            continue;
        }
        for (std::size_t offset = 0; offset + n <= size; ++offset) {
            if (PatternMatch(start + offset, pat, n)) {
                const auto site = reinterpret_cast<std::uintptr_t>(start + offset);
                if (IsChestFlagWriteSite(site)) {
                    return site;
                }
            }
        }
    }
    return 0;
}

std::uintptr_t ResolveChestFlagWriteSite() {
    HMODULE module = GetModuleHandleW(nullptr);
    if (!module) {
        return 0;
    }
    const auto base = reinterpret_cast<std::uintptr_t>(module);
    const auto scanned = ScanChestFlagSite(module);
    if (scanned) {
        LogInfo("flag write site via AOB at grandia.exe+0x%X", static_cast<unsigned>(scanned - base));
        return scanned;
    }
    const std::uintptr_t fallbacks[] = {kChestFlagWriteRvaSteam, kChestFlagWriteRvaAlt};
    for (const std::uintptr_t rva : fallbacks) {
        const auto site = base + rva;
        if (IsChestFlagWriteSite(site)) {
            LogInfo("flag write site via RVA grandia.exe+0x%X", static_cast<unsigned>(rva));
            return site;
        }
    }
    LogWarn("flag write hook pattern not found — OnEventFlag disabled");
    return 0;
}

}  // namespace
}  // namespace grandia_mod

extern "C" {
void* g_mod_chest_flag_return = nullptr;
std::uint32_t g_mod_chest_flag_eax_src = 0;
void* g_mod_assign_return = nullptr;
void* g_mod_field_gold_trampoline = nullptr;
volatile unsigned g_mod_flag_event_id = 0;
volatile unsigned g_mod_flag_offset = 0;
volatile unsigned g_mod_flag_value = 0;
volatile unsigned g_mod_flag_mask = 0;
volatile unsigned g_mod_flag_ecx = 0;
volatile std::uintptr_t g_mod_flag_caller = 0;
volatile std::uintptr_t g_mod_flag_save_base = 0;
volatile std::uint8_t g_mod_flag_value_byte = 0;
volatile std::uintptr_t g_mod_assign_return_addr = 0;
volatile int g_mod_field_gold_amount = 0;
volatile std::uintptr_t g_mod_field_gold_ptr = 0;
}

extern "C" void ModChestEventNotify() {
    grandia_mod::OnFlagWrite(g_mod_flag_event_id, g_mod_flag_offset, g_mod_flag_value, g_mod_flag_mask,
                             g_mod_flag_ecx, g_mod_flag_caller, g_mod_flag_save_base);
}

extern "C" int ModAssignUiNotify() {
    return grandia_mod::OnAssignUi(g_mod_assign_return_addr);
}

extern "C" int ModFieldGoldNotify() {
    grandia_mod::AdoptGoldBase(g_mod_field_gold_ptr);
    return grandia_mod::OnFieldGold(g_mod_field_gold_amount);
}

#if defined(_M_IX86)

extern "C" __declspec(naked) void ModChestFlagWriteDetour() {
    __asm {
        mov byte ptr [g_mod_flag_value_byte], al

        test ebp, ebp
        jz flag_use_esp_caller
        mov eax, dword ptr [ebp+4]
        jmp flag_store_caller
    flag_use_esp_caller:
        mov eax, dword ptr [esp]
    flag_store_caller:
        mov dword ptr [g_mod_flag_caller], eax

        mov dword ptr [g_mod_flag_event_id], edi
        mov dword ptr [g_mod_flag_offset], edx
        mov dword ptr [g_mod_flag_mask], ebx
        mov dword ptr [g_mod_flag_ecx], ecx
        mov dword ptr [g_mod_flag_save_base], esi
        movzx eax, byte ptr [g_mod_flag_value_byte]
        mov dword ptr [g_mod_flag_value], eax
        pushad
        call ModChestEventNotify
        popad
        mov al, byte ptr [g_mod_flag_value_byte]
        mov byte ptr [edx+esi], al
        push ebx
        mov ebx, dword ptr [g_mod_chest_flag_eax_src]
        mov eax, dword ptr [ebx]
        pop ebx
        jmp dword ptr [g_mod_chest_flag_return]
    }
}

extern "C" __declspec(naked) void ModAssignUiEntryDetour() {
    __asm {
        mov eax, dword ptr [esp]
        mov dword ptr [g_mod_assign_return_addr], eax

        pushad
        call ModAssignUiNotify
        test eax, eax
        popad
        jz assign_ui_pass_through

        mov eax, 1
        ret

    assign_ui_pass_through:
        push ebp
        mov ebp, esp
        and esp, 0FFFFFFF8h
        jmp dword ptr [g_mod_assign_return]
    }
}

extern "C" __declspec(naked) void ModFieldGoldAddDetour() {
    __asm {
        mov dword ptr [g_mod_field_gold_ptr], eax
        mov dword ptr [g_mod_field_gold_amount], edx
        pushad
        call ModFieldGoldNotify
        mov dword ptr [esp+14h], eax
        popad
        jmp dword ptr [g_mod_field_gold_trampoline]
    }
}

#endif

namespace grandia_mod {
namespace {

bool InstallChestFlagHook(std::uintptr_t site) {
#if !defined(_M_IX86)
    (void)site;
    return false;
#else
    if (!IsChestFlagWriteSite(site)) {
        LogWarn("refusing flag hook — unexpected bytes at 0x%08X", static_cast<unsigned>(site));
        return false;
    }
    constexpr std::size_t kPatchSize = 5;
    const auto* bytes = reinterpret_cast<const std::uint8_t*>(site);
    g_mod_chest_flag_eax_src = *reinterpret_cast<const std::uint32_t*>(bytes + 4);
    g_chest_flag_site = reinterpret_cast<void*>(site);
    g_mod_chest_flag_return = reinterpret_cast<void*>(site + kChestFlagPostMovSkip);
    if (!WriteJump(g_chest_flag_site, reinterpret_cast<void*>(&ModChestFlagWriteDetour),
                   g_chest_flag_original, kPatchSize)) {
        LogWarn("failed to install flag write hook");
        g_chest_flag_site = nullptr;
        g_mod_chest_flag_return = nullptr;
        g_mod_chest_flag_eax_src = 0;
        return false;
    }
    LogInfo("OnEventFlag hook at grandia.exe+0x%X", static_cast<unsigned>(site - ModuleBase()));
    return true;
#endif
}

bool InstallAssignUiHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto site = ModuleBase() + kAssignUiEntryRva;
    if (!IsExecutableAddress(reinterpret_cast<void*>(site)) ||
        !BytesMatch(reinterpret_cast<const std::uint8_t*>(site), kAssignUiEntryBytes,
                    sizeof(kAssignUiEntryBytes))) {
        LogWarn("assign UI bytes mismatch at +0x%X — OnItemAssignUi disabled",
                static_cast<unsigned>(kAssignUiEntryRva));
        return false;
    }
    g_assign_ui_site = reinterpret_cast<void*>(site);
    g_mod_assign_return = reinterpret_cast<void*>(site + kAssignUiEntryPatchSize);
    if (!WriteJump(g_assign_ui_site, reinterpret_cast<void*>(&ModAssignUiEntryDetour),
                   g_assign_ui_original, kAssignUiEntryPatchSize)) {
        LogWarn("failed to install assign UI hook");
        g_assign_ui_site = nullptr;
        g_mod_assign_return = nullptr;
        return false;
    }
    LogInfo("OnItemAssignUi hook at grandia.exe+0x%X", static_cast<unsigned>(kAssignUiEntryRva));
    return true;
#endif
}

bool InstallFieldGoldHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto site = ModuleBase() + kFieldGoldAddRva;
    if (!IsExecutableAddress(reinterpret_cast<void*>(site))) {
        LogWarn("field gold site not executable");
        return false;
    }
    const auto* bytes = reinterpret_cast<const std::uint8_t*>(site);
    if (!BytesMatch(bytes, kFieldGoldAddBytes, sizeof(kFieldGoldAddBytes))) {
        LogWarn("field gold bytes mismatch at +0x%X (expected 01 50 04)",
                static_cast<unsigned>(kFieldGoldAddRva));
        return false;
    }
    if (bytes[kFieldGoldAddInsnSize] != kFieldGoldCallOpcode) {
        LogWarn("field gold follow-up is not CALL at +0x%X",
                static_cast<unsigned>(kFieldGoldAddRva + kFieldGoldAddInsnSize));
        return false;
    }

    const auto call_site = site + kFieldGoldAddInsnSize;
    const auto call_rel = *reinterpret_cast<const std::int32_t*>(bytes + kFieldGoldAddInsnSize + 1);
    const auto call_abs = call_site + kFieldGoldCallInsnSize + call_rel;
    const auto resume = site + kFieldGoldStolenSize;

    g_field_gold_trampoline_mem = VirtualAlloc(nullptr, 32, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!g_field_gold_trampoline_mem) {
        LogWarn("failed to allocate field-gold trampoline");
        return false;
    }

    auto* tramp = reinterpret_cast<std::uint8_t*>(g_field_gold_trampoline_mem);
    const auto tramp_base = reinterpret_cast<std::uintptr_t>(tramp);
    std::memcpy(tramp, bytes, kFieldGoldAddInsnSize);
    tramp[kFieldGoldAddInsnSize] = kFieldGoldCallOpcode;
    const auto tramp_call = tramp_base + kFieldGoldAddInsnSize;
    const auto relocated_call_rel =
        static_cast<std::int32_t>(call_abs - (tramp_call + kFieldGoldCallInsnSize));
    std::memcpy(tramp + kFieldGoldAddInsnSize + 1, &relocated_call_rel, sizeof(relocated_call_rel));
    tramp[kFieldGoldStolenSize] = 0xE9;
    const auto jmp_rel = static_cast<std::int32_t>(resume - (tramp_base + kFieldGoldStolenSize + 5));
    std::memcpy(tramp + kFieldGoldStolenSize + 1, &jmp_rel, sizeof(jmp_rel));
    g_mod_field_gold_trampoline = g_field_gold_trampoline_mem;

    g_field_gold_site = reinterpret_cast<void*>(site);
    if (!WriteJump(g_field_gold_site, reinterpret_cast<void*>(&ModFieldGoldAddDetour),
                   g_field_gold_original, kFieldGoldPatchSize)) {
        LogWarn("failed to install field gold hook");
        VirtualFree(g_field_gold_trampoline_mem, 0, MEM_RELEASE);
        g_field_gold_trampoline_mem = nullptr;
        g_mod_field_gold_trampoline = nullptr;
        g_field_gold_site = nullptr;
        return false;
    }
    LogInfo("OnFieldGoldAdd hook at grandia.exe+0x%X", static_cast<unsigned>(kFieldGoldAddRva));
    return true;
#endif
}

}  // namespace

bool InstallGameHooks() {
    int ok = 0;
    const auto flag_site = ResolveChestFlagWriteSite();
    if (flag_site && InstallChestFlagHook(flag_site)) {
        ++ok;
    }
    if (InstallAssignUiHook()) {
        ++ok;
    }
    if (InstallFieldGoldHook()) {
        ++ok;
    }
    if (InstallWorldMapHook()) {
        ++ok;
    }
    if (!InstallMapTravelHook()) {
        LogWarn("OnMapTravel hook not installed");
    }
    if (!InstallWorldMapPictureHook()) {
        LogWarn("world-map picture hook not installed (LoadImage +0x1F5A0)");
    }
    if (InstallSaveHooks()) {
        ++ok;
    }
    if (InstallPartyHooks()) {
        ++ok;
    }
    if (!InstallScriptRedirectHooks()) {
        LogWarn("OnScriptExecute / OnCallHook hooks not installed");
    }
    if (!InstallQolHooks()) {
        LogWarn("QoL turbo / OnTick hooks not installed");
    }
    if (!InstallTitleScreenHook()) {
        LogWarn("OnTitleScreen hook not installed");
    }
    if (!InstallCatalogHooks()) {
        LogWarn("OnCharacter / OnItem / OnMagic not installed");
    }
    if (!InstallMovieSkipHook()) {
        LogWarn("FMV Start skip not installed");
    }
    if (ok == 0) {
        LogWarn("no game hooks installed");
        return false;
    }
    LogInfo("game hooks ready (%d/6)", ok);
    return true;
}

void RemoveGameHooks() {
    RemoveMovieSkipHook();
    RemoveCatalogHooks();
    RemoveTitleScreenHook();
    RemoveQolHooks();
    RemoveScriptRedirectHooks();
    RemoveWorldMapPictureHook();
    RemovePartyHooks();
    RemoveSaveHooks();
    RemoveMapTravelHook();
    RemoveWorldMapHook();
    if (g_chest_flag_site) {
        RestoreBytes(g_chest_flag_site, g_chest_flag_original, 5);
        g_chest_flag_site = nullptr;
    }
    if (g_assign_ui_site) {
        RestoreBytes(g_assign_ui_site, g_assign_ui_original, kAssignUiEntryPatchSize);
        g_assign_ui_site = nullptr;
    }
    if (g_field_gold_site) {
        RestoreBytes(g_field_gold_site, g_field_gold_original, kFieldGoldPatchSize);
        g_field_gold_site = nullptr;
    }
    if (g_field_gold_trampoline_mem) {
        VirtualFree(g_field_gold_trampoline_mem, 0, MEM_RELEASE);
        g_field_gold_trampoline_mem = nullptr;
        g_mod_field_gold_trampoline = nullptr;
    }
}

}  // namespace grandia_mod
