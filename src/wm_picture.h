#pragma once

namespace grandia_mod {

bool InstallWorldMapPictureHook();
void RemoveWorldMapPictureHook();
void ClearWorldMapCustomPictures();
void SetWorldMapCustomPicture(int icon, int x, int y, const char* path, int width = 0,
                              int height = 0);
void WorldMapNotifyTravelStarted();

}  // namespace grandia_mod

extern "C" void WorldMapOnIconSubmit(int icon, int dest_x, int dest_y, int dest_w, int dest_h);
