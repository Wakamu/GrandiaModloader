#pragma once

#include <cstdint>

namespace grandia_mod {

// Rewrite battle_ctx+0xC8A00 headers to a privately allocated P_DAT pack for
// ids[0..n). Never memcpy the blob into the context (that stomps scratch).
// No-op if n==0, already rebuilt this fight, or P_DAT/charpack cannot be read.
void TrySplicePdatPlayables(const std::uint8_t* ids, unsigned n, const char* tag);

// Free external pack buffers. Call on field return, not mid-fight.
void ResetPdatBattlePack();

bool PdatPackRebuilt();

// Empty e6a0[1..n) pages so +12CC66 `mov edx,[edi]` is not a null deref when
// the pack loop runs extra ally slots. Freed by ResetPdatBattlePack.
void EnsureKeyedSlotBuffers(unsigned n);

// After the slot jump-table: rewrite combatant +0x98/+0x9C/+0x164 from c8a
// headers so +13C1D6 can deref +0x98. Uses uint32 wrap (alloc - c8a + off).
// c8a_slot is 0..3 from the JT (edx); 0xFF means derive from combatant+0x15A.
void FixupAllyAnimPointers(void* combatant, unsigned c8a_slot);

}  // namespace grandia_mod
