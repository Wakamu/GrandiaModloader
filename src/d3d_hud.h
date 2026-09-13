#pragma once

namespace grandia_mod {

bool InstallD3dHud();
void RemoveD3dHud();
bool IsD3dHudInstalled();

void OverlayToast(const char* utf8, unsigned duration_ms, unsigned rgb);
void OverlayClearToasts();
void OverlaySetPanel(const char* joined_utf8, const unsigned* rgbs, int count);
void OverlayClearPanel();
bool OverlayPanelActive();

int OverlayInputOpen(const char* title, const char* initial, int max_len);
void OverlayInputClose();
bool OverlayInputActive();
const char* OverlayInputText();
const char* OverlayInputTake();
int OverlayInputTakeCancel();
void OverlayInputOnTick(unsigned pad_packed);

}  // namespace grandia_mod
