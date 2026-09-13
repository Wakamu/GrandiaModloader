#pragma once

namespace grandia_mod {

bool InstallXpHooks();
void RemoveXpHooks();
int XpGet(int kind);
int XpSet(int kind, int multiplier);

}  // namespace grandia_mod
