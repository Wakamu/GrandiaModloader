#pragma once

namespace grandia_mod {

bool InstallOverlayHooks();
void RemoveOverlayHooks();
bool IsOverlayHookInstalled();
void SignalRuntimeReady();

}  // namespace grandia_mod
