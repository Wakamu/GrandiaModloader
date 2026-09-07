#pragma once

namespace grandia_mod {

bool InstallCatalogHooks();
void RemoveCatalogHooks();
void PollCatalog();
void ResetCharacterSession();
void RaiseCharactersFromLoad();
void RaiseMagicCatalog();
void RaiseMagicCatalogOnSpawn();
bool TryCatalogSellGold(int item_id, int* gold);
void NotifyWindtItemsReady();

}  // namespace grandia_mod
