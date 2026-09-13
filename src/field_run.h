#pragma once

namespace grandia_mod {

// kind 0 = script id, 1 = script bytes, 2 = hook id, 3 = hook row (20 bytes).
int QueueFieldRun(int kind, int id, int table, const void* bytes, int len);
void TryApplyPendingFieldRun();

}  // namespace grandia_mod
