#pragma once

namespace grandia_mod {

bool InstallMapApplyHooks();
void RemoveMapApplyHooks();
void NoteMapStem(const char* stem);
const char* CurrentMapStem();

}  // namespace grandia_mod
