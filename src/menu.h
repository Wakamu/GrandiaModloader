#pragma once

namespace grandia_mod {

bool InstallMenuHooks();
void RemoveMenuHooks();
void MenuOnTick();

}  // namespace grandia_mod

extern "C" const char* ModMenuLookupOverride(const char* key);
