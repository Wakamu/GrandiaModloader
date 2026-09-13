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
    int(__cdecl* party_walk_get)(int* x, int* y, int* z);
    int(__cdecl* set_text1)(const void* data, int len);
    int(__cdecl* menu_open)(const char* title, const char* joined, int count);
    int(__cdecl* menu_close)();
    int(__cdecl* menu_is_open)();
    int(__cdecl* menu_cursor)();
    int(__cdecl* menu_take_choice)();
    int(__cdecl* menu_take_cancel)();
    int(__cdecl* menu_option)();
    int(__cdecl* status_get)();
    int(__cdecl* warp_to)(int dest, int spawn, int aux9, int auxA);
    int(__cdecl* overlay_input_open)(const char* title, const char* initial, int max_len);
    int(__cdecl* overlay_input_close)();
    int(__cdecl* overlay_input_active)();
    const char*(__cdecl* overlay_input_text)();
    const char*(__cdecl* overlay_input_take)();
    int(__cdecl* overlay_input_take_cancel)();
    int(__cdecl* sfx_emitter_count)();
    int(__cdecl* sfx_emitter_range)();
    int(__cdecl* sfx_emitter_get)(int index, int* rec_id, int* sfx, int* flags, int* x, int* y,
                                  int* z, int* muted);
    int(__cdecl* sfx_emitter_move)(int index, int x, int y, int z);
    int(__cdecl* sfx_emitter_mute)(int index, int muted);
    int(__cdecl* sfx_emitter_set_sfx)(int index, int sfx);
    int(__cdecl* sfx_emitter_add)(int rec_id, int sfx, int flags, int x, int y, int z, int kind,
                                 int period, int bias);
    int(__cdecl* sfx_emitter_remove)(int index);
    int(__cdecl* sfx_emitter_set_flags)(int index, int flags);
    int(__cdecl* camera_walk_get)(int* x, int* y, int* z);
    int(__cdecl* hash_ps1_sprite)(int tpage, int u, int v, int width, int height, unsigned* key);
    int(__cdecl* read_ps1_vram)(int which, void* dest, int dest_len);
    int(__cdecl* run_field)(int kind, int id, int table, const void* bytes, int len);
    int(__cdecl* quit)();
    int(__cdecl* xp_get)(int kind);
    int(__cdecl* xp_set)(int kind, int multiplier);
};

bool InstallGameServices();
void RemoveGameServices();
void FillHostApi(HostApiNative* api);
void AdoptGoldBase(std::uintptr_t gold_ptr);
void AdoptFlagBlob(std::uintptr_t blob);
void ResetLiveSavePointers();

}  // namespace grandia_mod
