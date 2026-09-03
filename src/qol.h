#pragma once

#include <cstdint>

namespace grandia_mod {

bool InstallQolHooks();
void RemoveQolHooks();

int GetSpeedTurboLevel();
void PaceSpeedTurboFrame();
void SwallowBlockedGamePad();

}  // namespace grandia_mod
