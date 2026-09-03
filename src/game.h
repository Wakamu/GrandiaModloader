#pragma once

#include <cstdint>

namespace grandia_mod {

struct HostApiNative {
    int(__cdecl* stash_add)(int item_id, int delta);
    int(__cdecl* stash_get)(int item_id);
    int(__cdecl* gold_add)(int amount);
    int(__cdecl* gold_get)();
    int(__cdecl* flag_get)(unsigned event_id);
    int(__cdecl* flag_set)(unsigned event_id, int value);
    int(__cdecl* party_get)(int slot);
    int(__cdecl* party_set_ids)(int a, int b, int c, int d);
    int(__cdecl* turbo_get)();
    int(__cdecl* turbo_set)(int level);
    int(__cdecl* turbo_override_get)();
    int(__cdecl* turbo_override_set)(int level);
    int(__cdecl* encounters_get)();
    int(__cdecl* encounters_set)(int off);
    int(__cdecl* debug_get)();
    int(__cdecl* debug_set)(int on);
    int(__cdecl* key_down)(int vk);
    int(__cdecl* scan_down)(int scan);
    int(__cdecl* pad_poll)();
    int(__cdecl* overlay_ready)();
    int(__cdecl* overlay_toast)(const char* message, int duration_ms, unsigned rgb);
    int(__cdecl* overlay_clear_toasts)();
    int(__cdecl* overlay_set_panel)(const char* joined, const unsigned* rgbs, int count);
    int(__cdecl* overlay_clear_panel)();
    int(__cdecl* overlay_panel_active)();
};

bool InstallGameServices();
void RemoveGameServices();
void FillHostApi(HostApiNative* api);
void AdoptGoldBase(std::uintptr_t gold_ptr);
void AdoptFlagBlob(std::uintptr_t blob);

}  // namespace grandia_mod
