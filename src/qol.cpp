#include "qol.h"

#include "catalog.h"
#include "clr_host.h"
#include "d3d_hud.h"
#include "game.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>
#include <Xinput.h>

#include <atomic>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <vector>

extern "C" int ModFlagGet(unsigned event_id);
extern "C" int ModFlagSet(unsigned event_id, int value);

#if defined(_M_IX86)
extern "C" {
void* g_mod_orig_pad_refresh = nullptr;
void ModAfterPadRefresh();
void ModPadFillDetour();
}
#endif

namespace grandia_mod {

void RaiseTick();

namespace {

constexpr int kMaxSpeedLevel = 5;
constexpr unsigned kEncounterFlagId = 0x08FDu;
constexpr std::uintptr_t kDebugFlagRva = 0x23F00Eu;
constexpr unsigned kDebugFlagOff = 0x30303030u;
constexpr unsigned kDebugFlagOn = 0x30303034u;
constexpr DWORD kMaxScaledSleepMs = 50;
constexpr std::uintptr_t kPadFillFnRva = 0x58F0u;
constexpr std::size_t kPadFillStolen = 12;
constexpr std::uintptr_t kPadObjRva = 0x319440u;

using QueryPerformanceCounter_t = BOOL(WINAPI*)(LARGE_INTEGER*);
using GetTickCount_t = DWORD(WINAPI*)();
using GetTickCount64_t = ULONGLONG(WINAPI*)();
using TimeGetTime_t = DWORD(WINAPI*)();
using Sleep_t = void(WINAPI*)(DWORD);
using SleepEx_t = DWORD(WINAPI*)(DWORD, BOOL);
using XInputGetState_t = DWORD(WINAPI*)(DWORD, XINPUT_STATE*);

QueryPerformanceCounter_t g_orig_qpc = nullptr;
GetTickCount_t g_orig_tick = nullptr;
GetTickCount64_t g_orig_tick64 = nullptr;
TimeGetTime_t g_orig_time_get_time = nullptr;
Sleep_t g_orig_sleep = nullptr;
SleepEx_t g_orig_sleep_ex = nullptr;
XInputGetState_t g_xinput_get_state = nullptr;
bool g_xinput_resolved = false;

std::mutex g_warp_mu;
double g_speed = 1.0;
bool g_qpc_init = false;
bool g_tick_init = false;
bool g_tick64_init = false;
bool g_tgt_init = false;
LONGLONG g_last_real_qpc = 0;
LONGLONG g_last_fake_qpc = 0;
DWORD g_last_real_tick = 0;
DWORD g_last_fake_tick = 0;
ULONGLONG g_last_real_tick64 = 0;
ULONGLONG g_last_fake_tick64 = 0;
DWORD g_last_real_tgt = 0;
DWORD g_last_fake_tgt = 0;

std::atomic<int> g_effective_level{0};
std::atomic<int> g_speed_level{0};
std::atomic<int> g_speed_override{0};
bool g_turbo_installed = false;

struct IatPatch {
    void** slot = nullptr;
    void* original = nullptr;
};

std::vector<IatPatch> g_patches;

HANDLE g_tick_thread = nullptr;
std::atomic<bool> g_tick_run{false};
std::atomic<bool> g_block_game_pad{false};
std::atomic<DWORD> g_last_pad_hook_ms{0};
std::atomic<DWORD> g_last_pad_raise_ms{0};
void* g_pad_site = nullptr;
void* g_pad_tramp_mem = nullptr;
std::uint8_t g_pad_original[16]{};

int ClampSpeedLevel(int level) {
    if (level <= 0) {
        return 0;
    }
    if (level < 2) {
        return 2;
    }
    if (level > kMaxSpeedLevel) {
        return kMaxSpeedLevel;
    }
    return level;
}

void SetSpeedUnlocked(double speed) {
    if (speed < 0.1) {
        speed = 0.1;
    }
    if (speed > 10.0) {
        speed = 10.0;
    }
    g_speed = speed;
}

void ApplyEffectiveSpeed() {
    const int override_level = ClampSpeedLevel(g_speed_override.load(std::memory_order_relaxed));
    const int latched = ClampSpeedLevel(g_speed_level.load(std::memory_order_relaxed));
    const int effective = override_level > 0 ? override_level : latched;
    {
        std::lock_guard<std::mutex> lock(g_warp_mu);
        SetSpeedUnlocked(effective > 0 ? static_cast<double>(effective) : 1.0);
    }
    g_effective_level.store(effective, std::memory_order_relaxed);
}

LONGLONG AdvanceQpc(LONGLONG real_now) {
    std::lock_guard<std::mutex> lock(g_warp_mu);
    if (!g_qpc_init) {
        g_last_real_qpc = real_now;
        g_last_fake_qpc = real_now;
        g_qpc_init = true;
        return real_now;
    }
    const LONGLONG delta = real_now - g_last_real_qpc;
    g_last_real_qpc = real_now;
    g_last_fake_qpc += static_cast<LONGLONG>(static_cast<double>(delta) * g_speed);
    return g_last_fake_qpc;
}

DWORD AdvanceMs(DWORD real_now, bool* inited, DWORD* last_real, DWORD* last_fake) {
    std::lock_guard<std::mutex> lock(g_warp_mu);
    if (!*inited) {
        *last_real = real_now;
        *last_fake = real_now;
        *inited = true;
        return real_now;
    }
    const DWORD delta = real_now - *last_real;
    *last_real = real_now;
    *last_fake += static_cast<DWORD>(static_cast<double>(delta) * g_speed);
    return *last_fake;
}

ULONGLONG AdvanceMs64(ULONGLONG real_now) {
    std::lock_guard<std::mutex> lock(g_warp_mu);
    if (!g_tick64_init) {
        g_last_real_tick64 = real_now;
        g_last_fake_tick64 = real_now;
        g_tick64_init = true;
        return real_now;
    }
    const ULONGLONG delta = real_now - g_last_real_tick64;
    g_last_real_tick64 = real_now;
    g_last_fake_tick64 += static_cast<ULONGLONG>(static_cast<double>(delta) * g_speed);
    return g_last_fake_tick64;
}

DWORD ScaleSleepMs(DWORD ms) {
    if (ms == 0 || ms > kMaxScaledSleepMs) {
        return ms;
    }
    const int level = g_effective_level.load(std::memory_order_relaxed);
    if (level <= 1) {
        return ms;
    }
    const double scaled = static_cast<double>(ms) / static_cast<double>(level);
    if (scaled < 1.0) {
        return 0;
    }
    return static_cast<DWORD>(scaled + 0.5);
}

BOOL WINAPI HookQueryPerformanceCounter(LARGE_INTEGER* counter) {
    LARGE_INTEGER real{};
    if (!g_orig_qpc || !g_orig_qpc(&real)) {
        return FALSE;
    }
    if (counter) {
        counter->QuadPart = AdvanceQpc(real.QuadPart);
    }
    return TRUE;
}

DWORD WINAPI HookGetTickCount() {
    return AdvanceMs(g_orig_tick(), &g_tick_init, &g_last_real_tick, &g_last_fake_tick);
}

ULONGLONG WINAPI HookGetTickCount64() {
    return AdvanceMs64(g_orig_tick64());
}

DWORD WINAPI HookTimeGetTime() {
    return AdvanceMs(g_orig_time_get_time(), &g_tgt_init, &g_last_real_tgt, &g_last_fake_tgt);
}

void WINAPI HookSleep(DWORD ms) {
    if (g_orig_sleep) {
        g_orig_sleep(ScaleSleepMs(ms));
    }
}

DWORD WINAPI HookSleepEx(DWORD ms, BOOL alertable) {
    if (!g_orig_sleep_ex) {
        return 0;
    }
    return g_orig_sleep_ex(ScaleSleepMs(ms), alertable);
}

bool EqualsIgnoreCase(const char* a, const char* b) {
    if (!a || !b) {
        return false;
    }
    while (*a && *b) {
        const char ca = (*a >= 'A' && *a <= 'Z') ? static_cast<char>(*a - 'A' + 'a') : *a;
        const char cb = (*b >= 'A' && *b <= 'Z') ? static_cast<char>(*b - 'A' + 'a') : *b;
        if (ca != cb) {
            return false;
        }
        ++a;
        ++b;
    }
    return *a == *b;
}

bool IsTimeDll(const char* name) {
    return EqualsIgnoreCase(name, "kernel32.dll") || EqualsIgnoreCase(name, "kernelbase.dll") ||
           EqualsIgnoreCase(name, "winmm.dll");
}

void* HookForImport(const char* func_name) {
    if (EqualsIgnoreCase(func_name, "QueryPerformanceCounter")) {
        return reinterpret_cast<void*>(&HookQueryPerformanceCounter);
    }
    if (EqualsIgnoreCase(func_name, "GetTickCount")) {
        return reinterpret_cast<void*>(&HookGetTickCount);
    }
    if (EqualsIgnoreCase(func_name, "GetTickCount64")) {
        return reinterpret_cast<void*>(&HookGetTickCount64);
    }
    if (EqualsIgnoreCase(func_name, "timeGetTime")) {
        return reinterpret_cast<void*>(&HookTimeGetTime);
    }
    if (EqualsIgnoreCase(func_name, "Sleep")) {
        return reinterpret_cast<void*>(&HookSleep);
    }
    if (EqualsIgnoreCase(func_name, "SleepEx")) {
        return reinterpret_cast<void*>(&HookSleepEx);
    }
    return nullptr;
}

void CaptureOriginal(const char* func_name, void* original) {
    if (!original) {
        return;
    }
    if (EqualsIgnoreCase(func_name, "QueryPerformanceCounter") && !g_orig_qpc) {
        g_orig_qpc = reinterpret_cast<QueryPerformanceCounter_t>(original);
    } else if (EqualsIgnoreCase(func_name, "GetTickCount") && !g_orig_tick) {
        g_orig_tick = reinterpret_cast<GetTickCount_t>(original);
    } else if (EqualsIgnoreCase(func_name, "GetTickCount64") && !g_orig_tick64) {
        g_orig_tick64 = reinterpret_cast<GetTickCount64_t>(original);
    } else if (EqualsIgnoreCase(func_name, "timeGetTime") && !g_orig_time_get_time) {
        g_orig_time_get_time = reinterpret_cast<TimeGetTime_t>(original);
    } else if (EqualsIgnoreCase(func_name, "Sleep") && !g_orig_sleep) {
        g_orig_sleep = reinterpret_cast<Sleep_t>(original);
    } else if (EqualsIgnoreCase(func_name, "SleepEx") && !g_orig_sleep_ex) {
        g_orig_sleep_ex = reinterpret_cast<SleepEx_t>(original);
    }
}

int PatchModuleIat(HMODULE module) {
    if (!module) {
        return 0;
    }
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(module);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
        return 0;
    }
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(reinterpret_cast<std::uint8_t*>(module) + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) {
        return 0;
    }
    const auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (dir.VirtualAddress == 0 || dir.Size == 0) {
        return 0;
    }
    auto* imp = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(reinterpret_cast<std::uint8_t*>(module) +
                                                           dir.VirtualAddress);
    int patched = 0;
    for (; imp->Name != 0; ++imp) {
        const char* dll_name =
            reinterpret_cast<const char*>(reinterpret_cast<std::uint8_t*>(module) + imp->Name);
        if (!IsTimeDll(dll_name)) {
            continue;
        }
        auto* thunk = reinterpret_cast<IMAGE_THUNK_DATA*>(
            reinterpret_cast<std::uint8_t*>(module) +
            (imp->OriginalFirstThunk ? imp->OriginalFirstThunk : imp->FirstThunk));
        auto* iat = reinterpret_cast<IMAGE_THUNK_DATA*>(reinterpret_cast<std::uint8_t*>(module) +
                                                        imp->FirstThunk);
        for (; thunk->u1.AddressOfData != 0; ++thunk, ++iat) {
            if (thunk->u1.Ordinal & IMAGE_ORDINAL_FLAG) {
                continue;
            }
            auto* ibn = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(reinterpret_cast<std::uint8_t*>(module) +
                                                               thunk->u1.AddressOfData);
            const char* func_name = reinterpret_cast<const char*>(ibn->Name);
            void* hook = HookForImport(func_name);
            if (!hook) {
                continue;
            }
            void** slot = reinterpret_cast<void**>(&iat->u1.Function);
            void* current = *slot;
            if (current == hook) {
                continue;
            }
            CaptureOriginal(func_name, current);
            DWORD old_protect = 0;
            if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &old_protect)) {
                continue;
            }
            *slot = hook;
            VirtualProtect(slot, sizeof(void*), old_protect, &old_protect);
            g_patches.push_back(IatPatch{slot, current});
            ++patched;
        }
    }
    return patched;
}

bool ResolveFallbacks() {
    HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    HMODULE winmm = LoadLibraryW(L"winmm.dll");
    if (!kernel32) {
        return false;
    }
    if (!g_orig_qpc) {
        g_orig_qpc = reinterpret_cast<QueryPerformanceCounter_t>(
            GetProcAddress(kernel32, "QueryPerformanceCounter"));
    }
    if (!g_orig_tick) {
        g_orig_tick = reinterpret_cast<GetTickCount_t>(GetProcAddress(kernel32, "GetTickCount"));
    }
    if (!g_orig_tick64) {
        g_orig_tick64 = reinterpret_cast<GetTickCount64_t>(GetProcAddress(kernel32, "GetTickCount64"));
    }
    if (!g_orig_sleep) {
        g_orig_sleep = reinterpret_cast<Sleep_t>(GetProcAddress(kernel32, "Sleep"));
    }
    if (!g_orig_sleep_ex) {
        g_orig_sleep_ex = reinterpret_cast<SleepEx_t>(GetProcAddress(kernel32, "SleepEx"));
    }
    if (!g_orig_time_get_time && winmm) {
        g_orig_time_get_time = reinterpret_cast<TimeGetTime_t>(GetProcAddress(winmm, "timeGetTime"));
    }
    return g_orig_qpc != nullptr;
}

void RemoveSpeedTurboIat() {
    {
        std::lock_guard<std::mutex> lock(g_warp_mu);
        SetSpeedUnlocked(1.0);
    }
    g_speed_override.store(0);
    g_speed_level.store(0);
    g_effective_level.store(0);
    for (auto it = g_patches.rbegin(); it != g_patches.rend(); ++it) {
        if (!it->slot) {
            continue;
        }
        DWORD old_protect = 0;
        if (VirtualProtect(it->slot, sizeof(void*), PAGE_READWRITE, &old_protect)) {
            *it->slot = it->original;
            VirtualProtect(it->slot, sizeof(void*), old_protect, &old_protect);
        }
    }
    g_patches.clear();
    g_orig_qpc = nullptr;
    g_orig_tick = nullptr;
    g_orig_tick64 = nullptr;
    g_orig_time_get_time = nullptr;
    g_orig_sleep = nullptr;
    g_orig_sleep_ex = nullptr;
    g_turbo_installed = false;
}

bool InstallSpeedTurboIat() {
#if !defined(_M_IX86)
    return false;
#else
    if (g_turbo_installed) {
        return true;
    }
    g_patches.clear();
    const wchar_t* targets[] = {L"grandia.exe", L"SDL2.dll", L"msvcp140.dll"};
    int total = 0;
    for (const wchar_t* name : targets) {
        HMODULE mod = GetModuleHandleW(name);
        if (!mod && _wcsicmp(name, L"grandia.exe") == 0) {
            const auto base = ModuleBase();
            mod = base ? reinterpret_cast<HMODULE>(base) : nullptr;
        }
        if (!mod) {
            continue;
        }
        total += PatchModuleIat(mod);
    }
    if (!ResolveFallbacks()) {
        RemoveSpeedTurboIat();
        LogWarn("QoL turbo: failed to resolve original QPC");
        return false;
    }
    if (total == 0) {
        LogWarn("QoL turbo: no IAT slots patched");
        return false;
    }
    {
        std::lock_guard<std::mutex> lock(g_warp_mu);
        SetSpeedUnlocked(1.0);
        g_qpc_init = false;
        g_tick_init = false;
        g_tick64_init = false;
        g_tgt_init = false;
    }
    g_turbo_installed = true;
    LogInfo("QoL turbo IAT ready (%d hooks, 0/2..5 via Game.Turbo)", total);
    return true;
#endif
}

void ResolveXInput() {
    if (g_xinput_resolved) {
        return;
    }
    g_xinput_resolved = true;
    const wchar_t* candidates[] = {L"xinput1_4.dll", L"xinput1_3.dll", L"xinput9_1_0.dll"};
    for (const wchar_t* name : candidates) {
        HMODULE mod = LoadLibraryW(name);
        if (!mod) {
            continue;
        }
        auto* fn = reinterpret_cast<XInputGetState_t>(GetProcAddress(mod, "XInputGetState"));
        if (fn) {
            g_xinput_get_state = fn;
            return;
        }
    }
}

void BlankGamePad() {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    const auto at = base + kPadObjRva;
    SafeWriteU32(at, 0);
    SafeWriteU32(at + 4, 0);
    SafeWriteU32(at + 8, 0);
    SafeWriteU32(at + 12, 0);
}

bool WriteCall(void* site, void* destination, std::uint8_t* original_out) {
    auto* bytes = reinterpret_cast<std::uint8_t*>(site);
    if (bytes[0] != 0xE8) {
        return false;
    }
    std::uint8_t patch[5] = {0xE8};
    const auto rel = static_cast<std::int32_t>(reinterpret_cast<std::uint8_t*>(destination) - (bytes + 5));
    std::memcpy(patch + 1, &rel, sizeof(rel));
    DWORD old_protect = 0;
    if (!VirtualProtect(site, 5, PAGE_EXECUTE_READWRITE, &old_protect)) {
        return false;
    }
    if (original_out) {
        std::memcpy(original_out, site, 5);
    }
    std::memcpy(site, patch, 5);
    VirtualProtect(site, 5, old_protect, &old_protect);
    FlushInstructionCache(GetCurrentProcess(), site, 5);
    return true;
}

bool InstallPadBlockHook() {
#if !defined(_M_IX86)
    return false;
#else
    if (g_pad_site) {
        return true;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    auto* site = reinterpret_cast<std::uint8_t*>(base + kPadFillFnRva);
    // push ebp; mov ebp, esp; push ecx; movss xmm3, [imm32]
    const std::uint8_t expect[] = {0x55, 0x8B, 0xEC, 0x51, 0xF3, 0x0F, 0x10, 0x1D};
    if (!IsExecutableAddress(site) || !BytesMatch(site, expect, sizeof(expect)) ||
        site[12] != 0x8B || site[13] != 0xD1) {
        LogWarn("OnTick pad-fill +0x58F0 mismatch");
        return false;
    }
    g_pad_tramp_mem = MakeTrampoline(site, kPadFillStolen, site + kPadFillStolen);
    if (!g_pad_tramp_mem) {
        LogWarn("OnTick pad-fill trampoline alloc failed");
        return false;
    }
    g_mod_orig_pad_refresh = g_pad_tramp_mem;
    if (!WriteJump(site, reinterpret_cast<void*>(&ModPadFillDetour), g_pad_original,
                   kPadFillStolen)) {
        VirtualFree(g_pad_tramp_mem, 0, MEM_RELEASE);
        g_pad_tramp_mem = nullptr;
        g_mod_orig_pad_refresh = nullptr;
        LogWarn("OnTick pad-fill hook failed");
        return false;
    }
    g_pad_site = site;
    LogInfo("OnTick pad-fill hook at +0x58F0 (BlockGameInput, save/UI)");
    return true;
#endif
}

void RemovePadBlockHook() {
    if (g_pad_site) {
        RestoreBytes(g_pad_site, g_pad_original, kPadFillStolen);
        g_pad_site = nullptr;
    }
#if defined(_M_IX86)
    g_mod_orig_pad_refresh = nullptr;
#endif
    if (g_pad_tramp_mem) {
        VirtualFree(g_pad_tramp_mem, 0, MEM_RELEASE);
        g_pad_tramp_mem = nullptr;
    }
}

int PollPadPacked() {
    ResolveXInput();
    if (!g_xinput_get_state) {
        return 0;
    }
    for (DWORD i = 0; i < 4; ++i) {
        XINPUT_STATE state{};
        if (g_xinput_get_state(i, &state) != ERROR_SUCCESS) {
            continue;
        }
        const unsigned buttons = state.Gamepad.wButtons;
        const unsigned lt = state.Gamepad.bLeftTrigger;
        const unsigned rt = state.Gamepad.bRightTrigger;
        return static_cast<int>(buttons | (lt << 16) | (rt << 24));
    }
    return 0;
}

DWORD WINAPI QolTickThread(LPVOID) {
    while (g_tick_run.load(std::memory_order_relaxed)) {
        const DWORD last = g_last_pad_hook_ms.load(std::memory_order_relaxed);
        if (last == 0 || GetTickCount() - last >= 40) {
            RaiseTick();
        }
        Sleep(16);
    }
    return 0;
}

void StartTickThread() {
    if (g_tick_thread) {
        return;
    }
    g_tick_run.store(true, std::memory_order_relaxed);
    g_tick_thread = CreateThread(nullptr, 0, QolTickThread, nullptr, 0, nullptr);
    if (!g_tick_thread) {
        g_tick_run.store(false, std::memory_order_relaxed);
        LogWarn("QoL OnTick watcher thread failed");
        return;
    }
    LogInfo("QoL OnTick watcher ~60 Hz");
}

void StopTickThread() {
    g_tick_run.store(false, std::memory_order_relaxed);
    if (g_tick_thread) {
        WaitForSingleObject(g_tick_thread, 500);
        CloseHandle(g_tick_thread);
        g_tick_thread = nullptr;
    }
}

}  // namespace

int GetSpeedTurboLevel() {
    return g_effective_level.load(std::memory_order_relaxed);
}

void SwallowBlockedGamePad() {
    if (g_block_game_pad.load(std::memory_order_relaxed)) {
        BlankGamePad();
    }
}

void PaceSpeedTurboFrame() {
    const int level = GetSpeedTurboLevel();
    if (level < 2) {
        return;
    }
    static LONGLONG freq = 0;
    static LONGLONG next_deadline = 0;
    LARGE_INTEGER qpc{};
    if (freq == 0) {
        LARGE_INTEGER f{};
        if (!QueryPerformanceFrequency(&f) || f.QuadPart <= 0) {
            return;
        }
        freq = f.QuadPart;
    }
    if (!QueryPerformanceCounter(&qpc)) {
        return;
    }
    const LONGLONG period = freq / (60LL * level);
    if (period <= 0) {
        return;
    }
    if (next_deadline == 0) {
        next_deadline = qpc.QuadPart + period;
        return;
    }
    if (qpc.QuadPart > next_deadline + period * 2) {
        next_deadline = qpc.QuadPart + period;
        return;
    }
    while (qpc.QuadPart < next_deadline) {
        const LONGLONG remain = next_deadline - qpc.QuadPart;
        const DWORD sleep_ms = static_cast<DWORD>((remain * 1000) / freq);
        if (sleep_ms > 1) {
            Sleep(sleep_ms - 1);
        }
        if (!QueryPerformanceCounter(&qpc)) {
            break;
        }
    }
    next_deadline += period;
}

bool InstallQolHooks() {
    const bool turbo = InstallSpeedTurboIat();
    if (!InstallD3dHud()) {
        LogWarn("D3D overlay / Present unlock not installed — turbo may cap near 2x");
    }
    if (!InstallPadBlockHook()) {
        LogWarn("OnTick pad-refresh hook not installed — BlockGameInput may miss a frame");
    }
    StartTickThread();
    return turbo;
}

void RemoveQolHooks() {
    StopTickThread();
    RemovePadBlockHook();
    RemoveD3dHud();
    RemoveSpeedTurboIat();
}

int TurboGet() {
    return g_speed_level.load(std::memory_order_relaxed);
}

int TurboSet(int level) {
    const int next = ClampSpeedLevel(level);
    g_speed_level.store(next, std::memory_order_relaxed);
    ApplyEffectiveSpeed();
    LogInfo("Game.Turbo.Level=%d", next);
    return 1;
}

int TurboOverrideGet() {
    return g_speed_override.load(std::memory_order_relaxed);
}

int TurboOverrideSet(int level) {
    g_speed_override.store(ClampSpeedLevel(level), std::memory_order_relaxed);
    ApplyEffectiveSpeed();
    return 1;
}

int EncountersGet() {
    const int v = ModFlagGet(kEncounterFlagId);
    return v > 0 ? 1 : 0;
}

int EncountersSet(int off) {
    if (ModFlagSet(kEncounterFlagId, off ? 1 : 0) == 0) {
        return 0;
    }
    LogInfo("Game.Encounters.Disabled=%d", off ? 1 : 0);
    return 1;
}

int DebugGet() {
    const auto base = ModuleBase();
    if (base == 0) {
        return 0;
    }
    std::uint32_t word = 0;
    if (!SafeReadU32(base + kDebugFlagRva, &word)) {
        return 0;
    }
    return word == kDebugFlagOn ? 1 : 0;
}

int DebugSet(int on) {
    const auto base = ModuleBase();
    if (base == 0) {
        return 0;
    }
    const std::uint32_t word = on ? kDebugFlagOn : kDebugFlagOff;
    if (!SafeWriteU32(base + kDebugFlagRva, word)) {
        return 0;
    }
    LogInfo("Game.Debug.Enabled=%d", on ? 1 : 0);
    return 1;
}

int KeyDown(int vk) {
    if (vk <= 0) {
        return 0;
    }
    return (GetAsyncKeyState(vk) & 0x8000) != 0 ? 1 : 0;
}

int ScanDown(int scan) {
    if (scan <= 0) {
        return 0;
    }
    const UINT vk = MapVirtualKeyA(static_cast<UINT>(scan), MAPVK_VSC_TO_VK);
    if (vk == 0) {
        return 0;
    }
    return (GetAsyncKeyState(static_cast<int>(vk)) & 0x8000) != 0 ? 1 : 0;
}

int PadPoll() {
    return PollPadPacked();
}

void RaiseTick() {
    PollCatalog();
    TickNative req{};
    const unsigned packed = static_cast<unsigned>(PollPadPacked());
    req.buttons = static_cast<std::uint16_t>(packed & 0xFFFFu);
    req.left_trigger = static_cast<std::uint8_t>((packed >> 16) & 0xFFu);
    req.right_trigger = static_cast<std::uint8_t>((packed >> 24) & 0xFFu);
    req.block = 0;
    RuntimeOnTick(&req);
    const bool block = req.block != 0;
    g_block_game_pad.store(block, std::memory_order_relaxed);
    if (block) {
        BlankGamePad();
    }
}

void OnPadRefreshed() {
    const DWORD now = GetTickCount();
    g_last_pad_hook_ms.store(now, std::memory_order_relaxed);
    const DWORD last_raise = g_last_pad_raise_ms.load(std::memory_order_relaxed);
    if (now != last_raise) {
        g_last_pad_raise_ms.store(now, std::memory_order_relaxed);
        RaiseTick();
    }
    if (g_block_game_pad.load(std::memory_order_relaxed)) {
        BlankGamePad();
    }
}

}  // namespace grandia_mod

extern "C" int ModTurboGet() {
    return grandia_mod::TurboGet();
}

extern "C" int ModTurboSet(int level) {
    return grandia_mod::TurboSet(level);
}

extern "C" int ModTurboOverrideGet() {
    return grandia_mod::TurboOverrideGet();
}

extern "C" int ModTurboOverrideSet(int level) {
    return grandia_mod::TurboOverrideSet(level);
}

extern "C" int ModEncountersGet() {
    return grandia_mod::EncountersGet();
}

extern "C" int ModEncountersSet(int off) {
    return grandia_mod::EncountersSet(off);
}

extern "C" int ModDebugGet() {
    return grandia_mod::DebugGet();
}

extern "C" int ModDebugSet(int on) {
    return grandia_mod::DebugSet(on);
}

extern "C" int ModKeyDown(int vk) {
    return grandia_mod::KeyDown(vk);
}

extern "C" int ModScanDown(int scan) {
    return grandia_mod::ScanDown(scan);
}

extern "C" int ModPadPoll() {
    return grandia_mod::PadPoll();
}

#if defined(_M_IX86)
extern "C" void ModAfterPadRefresh() {
    grandia_mod::OnPadRefreshed();
}

extern "C" __declspec(naked) void ModPadFillDetour() {
    __asm {
        call dword ptr [g_mod_orig_pad_refresh]
        pushad
        call ModAfterPadRefresh
        popad
        ret
    }
}
#endif
