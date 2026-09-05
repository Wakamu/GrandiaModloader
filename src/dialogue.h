#pragma once

#include <cstdint>

namespace grandia_mod {

bool InstallDialogueHook();
void RemoveDialogueHook();
void NoteScriptArm(std::uint16_t script_id, std::uint32_t ip);

}  // namespace grandia_mod
