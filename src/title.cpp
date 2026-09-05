#include "title.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"
#include "catalog.h"

#include <Windows.h>

#include <cstdint>

#if defined(_M_IX86)
extern "C" {
void* g_mod_title_screen_tramp = nullptr;
void ModTitleScreenDetour();
}

extern "C" void ModOnTitleScreen() {
    grandia_mod::ResetCharacterSession();
    grandia_mod::RuntimeOnTitleScreen();
}

extern "C" __declspec(naked) void ModTitleScreenDetour() {
    __asm {
        pushad
        call ModOnTitleScreen
        popad
        jmp dword ptr [g_mod_title_screen_tramp]
    }
}
#endif

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kTitleScreenRva = 0x7700u;
constexpr std::size_t kTitleScreenPatch = 6;
constexpr std::uint8_t kTitleScreenBytes[] = {0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8};

void* g_title_screen_site = nullptr;
void* g_title_screen_tramp_mem = nullptr;
std::uint8_t g_title_screen_original[8]{};

}  // namespace

bool InstallTitleScreenHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    auto* site = reinterpret_cast<std::uint8_t*>(base + kTitleScreenRva);
    if (!IsExecutableAddress(site) || !BytesMatch(site, kTitleScreenBytes, kTitleScreenPatch)) {
        LogWarn("OnTitleScreen +0x7700 site mismatch");
        return false;
    }
    g_title_screen_tramp_mem = MakeTrampoline(site, kTitleScreenPatch, site + kTitleScreenPatch);
    if (!g_title_screen_tramp_mem) {
        LogWarn("OnTitleScreen trampoline alloc failed");
        return false;
    }
    g_mod_title_screen_tramp = g_title_screen_tramp_mem;
    if (!WriteJump(site, reinterpret_cast<void*>(&ModTitleScreenDetour), g_title_screen_original,
                   kTitleScreenPatch)) {
        VirtualFree(g_title_screen_tramp_mem, 0, MEM_RELEASE);
        g_title_screen_tramp_mem = nullptr;
        g_mod_title_screen_tramp = nullptr;
        LogWarn("OnTitleScreen +0x7700 hook failed");
        return false;
    }
    g_title_screen_site = site;
    LogInfo("OnTitleScreen hook at +0x7700 (Press Start / TITLE.DAT)");
    return true;
#endif
}

void RemoveTitleScreenHook() {
    if (g_title_screen_site) {
        RestoreBytes(g_title_screen_site, g_title_screen_original, kTitleScreenPatch);
        g_title_screen_site = nullptr;
    }
    if (g_title_screen_tramp_mem) {
        VirtualFree(g_title_screen_tramp_mem, 0, MEM_RELEASE);
        g_title_screen_tramp_mem = nullptr;
#if defined(_M_IX86)
        g_mod_title_screen_tramp = nullptr;
#endif
    }
}

}  // namespace grandia_mod
