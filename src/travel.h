#pragma once

namespace grandia_mod {

bool InstallWorldMapHook();
void RemoveWorldMapHook();
bool InstallMapTravelHook();
void RemoveMapTravelHook();
int QueueMapTravel(unsigned dest, unsigned spawn, unsigned aux9 = 1, unsigned auxA = 30);
void TryApplyPendingTravel();

}  // namespace grandia_mod
