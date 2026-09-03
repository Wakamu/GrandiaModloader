#pragma once

namespace grandia_mod {

bool InstallPartyHooks();
void RemovePartyHooks();
void TryRestoreFieldParty(bool field_map_fopen = false);
void RestoreFieldPartyForSave();
void RestoreShopPriceOverrides();

}  // namespace grandia_mod

extern "C" int ModPartyGet(int slot);
extern "C" int ModPartySetIds(int a, int b, int c, int d);
