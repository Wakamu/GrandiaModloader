#pragma once

namespace grandia_mod {

bool InstallOverlayHooks();
void RemoveOverlayHooks();
bool IsOverlayHookInstalled();
void SignalRuntimeReady();
void WaitRuntimeReady();
void PrepareText1File(const char* path);
void PrepareText1FromInstall();

}  // namespace grandia_mod
