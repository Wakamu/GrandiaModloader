#include "party.h"

#include "catalog.h"
#include "clr_host.h"
#include "hook_util.h"
#include "log.h"
#include "party_pdat.h"

#include <Windows.h>

#include <cstdio>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace grandia_mod {

constexpr std::uintptr_t kMapObjPtrRva = 0x23FA94u;
constexpr std::uintptr_t kBattleModeRva = 0x31CD4Bu;
constexpr std::uintptr_t kBattleFormTablePtrRva = 0x313A78u;
constexpr std::uintptr_t kBattleEncObjPtrRva = 0x313B88u;
constexpr std::uintptr_t kBattleEncRowPtrRva = 0x313B84u;
constexpr std::uintptr_t kCharBlockOff = 0x10Cu;
constexpr std::uintptr_t kCharStride = 0x80u;
constexpr std::uintptr_t kCharEquipOff = 0x4Cu;
constexpr unsigned kCharEquipSlots = 6u;
constexpr std::uintptr_t kCharInvOff = 0x58u;  // 12×u16; first 0 terminates
constexpr unsigned kCharInvSlots = 12u;

constexpr std::uintptr_t kBattleSetupRva = 0x12B070u;
constexpr std::size_t kBattleSetupPatchSize = 5u;
constexpr std::uintptr_t kBattleLoadRva = 0x12BDD0u;
constexpr std::size_t kBattleLoadPatchSize = 6u;
constexpr std::uintptr_t kBattleAllyCountRva = 0x12C4CDu;
constexpr std::size_t kBattleAllyCountPatchSize = 5u;
constexpr std::uintptr_t kBattleAllyCountResumeRva = 0x12C4D6u;
constexpr std::uintptr_t kBattleAllySpawnGateRva = 0x12C4DEu;
constexpr std::size_t kBattleAllySpawnGatePatchSize = 6u;
constexpr std::uintptr_t kBattleAllySpawnGateResumeRva = 0x12C4E4u;
constexpr std::uintptr_t kBattleAllySpawnRva = 0x12C601u;
constexpr std::uintptr_t kBattleAllyCharIdRva = 0x13BD7Eu;
constexpr std::size_t kBattleAllyCharIdPatchSize = 9u;
constexpr std::uintptr_t kBattleAllyCharIdResumeRva = 0x13BD87u;

constexpr std::uintptr_t kCountLoadRva = 0x7E6ABu;
constexpr std::uintptr_t kCountResumeRva = 0x7E6B3u;
constexpr std::uintptr_t kCharIdLoadRva = 0x7E6D7u;
constexpr std::uintptr_t kCharIdResumeRva = 0x7E6DEu;
constexpr std::uintptr_t kPresetTableRva = 0x2015A0u;
constexpr std::uintptr_t kBattlePartyInitRva = 0x81B00u;
constexpr std::size_t kBattlePartyInitPatchSize = 8u;
constexpr std::uintptr_t kBattleAllySlotSkipRva = 0x56420u;
constexpr std::uintptr_t kBattleFa90EarlyExitJsRva = 0x7EDC1u;
constexpr std::size_t kBattleFa90EarlyExitJsSize = 6u;
constexpr std::uintptr_t kBattleModelBindRva = 0x12D730u;
constexpr std::size_t kBattleModelBindPatchSize = 8u;
constexpr std::uintptr_t kBattleAnimBindRva = 0x96020u;
constexpr std::size_t kBattleAnimBindPatchSize = 9u;
constexpr std::uintptr_t kBattlePostBindRva = 0x12CC80u;
constexpr std::size_t kBattlePostBindPatchSize = 6u;
constexpr std::uintptr_t kBattlePostBindResumeRva = 0x12CC86u;
constexpr std::uintptr_t kBattlePostBindSkipRva = 0x12CC91u;
constexpr std::uintptr_t kBattleAnimFixupRva = 0x13C0DAu;
constexpr std::size_t kBattleAnimFixupPatchSize = 6u;
constexpr std::uintptr_t kBattleAnimFixupResumeRva = 0x13C0E0u;
constexpr std::uintptr_t kBattleCtxPtrRva = 0x2D1A98u;
// Type-7 combatant slice of the 0x70F120 actor table (id at combatant+4).
constexpr std::uintptr_t kCombatantTableRva = 0x311FA0u;
constexpr unsigned kCombatantTableCount = 0xF8u;
constexpr std::uint16_t kAttachModelFlag = 0x5000u;
constexpr std::uint16_t kAttachBodyModelFlag = 0x400u;
constexpr std::uintptr_t kAttachBodyInitRva = 0xADFA0u;
// Encounter AI tables: 8 catalog slots of 0x40 (opcode 0 of slot 0 = ADFA0).
// Dispatcher at +0x1431CC uses SpeciesMap index-1.
constexpr std::uintptr_t kCtxScriptCatalogRva = 0x64A40u;
constexpr std::size_t kScriptSlotSize = 0x40u;
constexpr std::uintptr_t kSquidScriptTableRva = 0x218C28u;   // 0x96/97/98
constexpr std::uintptr_t kKrakenScriptTableRva = 0x21C2C8u;  // 0x99/9A/9B
constexpr std::uintptr_t kScriptCaseMapRva = 0x95F6Cu;
constexpr std::uintptr_t kScriptJumpTableRva = 0x95E28u;
constexpr std::uintptr_t kRetStubRva = 0x48090u;
constexpr std::uintptr_t kSocketHelperRva = 0x140D00u;
constexpr std::uintptr_t kUtf8DecRva = 0x163E0u;
constexpr std::size_t kUtf8DecPatchSize = 6u;
constexpr std::uintptr_t kSkillNameInternRva = 0x16220u;
constexpr std::size_t kSkillNameInternPatchSize = 6u;
constexpr std::uintptr_t kSkillScriptParseRva = 0x9C460u;
constexpr std::size_t kSkillScriptParsePatchSize = 6u;
constexpr std::uintptr_t kEnemyModelCopyRva = 0x142F90u;
constexpr std::size_t kEnemyModelCopyPatchSize = 5u;
// 142B00 / 142D40: stored at actor+0x34, run after spawn wrote +15A. 6-byte prologue.
constexpr std::uintptr_t kEnemyLoadedRva = 0x142B00u;
constexpr std::uintptr_t kEnemyLoadedAltRva = 0x142D40u;
constexpr std::size_t kEnemyLoadedPatchSize = 6u;
// Shop UI init: cdecl arg = shop rec, dl = kind (1 item shop, 5 Mana Egg).
// Stock is field-params +0x188.
constexpr std::uintptr_t kShopOpenRva = 0x1E93A0u;
constexpr std::size_t kShopOpenPatchSize = 6u;
constexpr std::uintptr_t kShopSellOpenRva = 0x1ECA00u;
constexpr std::size_t kShopSellOpenPatchSize = 6u;
constexpr std::uintptr_t kShopSellListPriceRva = 0x1ECDDFu;
constexpr std::size_t kShopSellListPricePatchSize = 6u;
constexpr std::uintptr_t kShopSellListPriceResumeRva = 0x1ECDE5u;
constexpr std::uintptr_t kShopSellSelectPriceRva = 0x1ED002u;
constexpr std::size_t kShopSellSelectPricePatchSize = 5u;
constexpr std::uintptr_t kShopSellSelectPriceResumeRva = 0x1ED010u;
constexpr std::uintptr_t kFieldParamsPtrRva = 0x23FA9Cu;
constexpr std::uintptr_t kShopStockOff = 0x188u;
constexpr std::uintptr_t kAttachParentLookupRva = 0x13FD6Cu;
constexpr std::size_t kAttachParentLookupPatchSize = 7u;
constexpr std::uintptr_t kAttachParentLookupResumeRva = 0x13FD73u;

constexpr std::uintptr_t kMenuFillLoopRva = 0x1C3B56u;
constexpr std::size_t kMenuFillPatchSize = 6u;
constexpr std::uintptr_t kMenuFaceFinalizeRva = 0x1C3C8Cu;
constexpr std::uintptr_t kStashFillLoopRva = 0x1E6821u;
constexpr std::uintptr_t kStashFaceFinalizeRva = 0x1E695Cu;
constexpr std::uintptr_t kItemFillLoopRva = 0x1DBE66u;
constexpr std::uintptr_t kItemFaceFinalizeRva = 0x1DBF9Cu;
// Status/equip confirm: dest.equip[slot] = bag item, then `mov word [bag_slot], old_dest`.
// Vanilla never unequips, so old_dest is never 0. Empty dest (left member, seeded block)
// plants a mid-list 0 and the bag UI hides everything after it.
constexpr std::uintptr_t kEmptyEquipReturnRva = 0x1D024Au;
constexpr std::size_t kEmptyEquipReturnPatchSize = 7u;
constexpr std::uintptr_t kEmptyEquipReturnResumeRva = 0x1D0251u;
constexpr std::uintptr_t kMenuPartyCacheRva = 0x30CFF8u;
constexpr std::uintptr_t kStashPartyCacheRva = 0x307FD0u;
constexpr std::uintptr_t kItemPartyCacheRva = 0x309C68u;

constexpr std::uintptr_t kFaceLoadRva = 0x56FF0u;
constexpr std::uintptr_t kFaceBankLoadRva = 0x57010u;
constexpr std::size_t kFaceBankLoadPatchSize = 6u;
constexpr std::uintptr_t kFaceBankLoadResumeRva = 0x57016u;
constexpr std::uintptr_t kFaceBankPtrRva = 0x240E6Cu;
constexpr std::uintptr_t kFaceDestPtrRva = 0x240E68u;
constexpr std::uintptr_t kCostumeStatusTableRva = 0x201D2Fu;
constexpr std::uintptr_t kStatusFaceHandleRva = 0x30B34Eu;
constexpr std::uintptr_t kStashFaceHandleRva = 0x30719Eu;
constexpr std::uintptr_t kItemFaceHandleRva = 0x308E2Eu;
constexpr std::size_t kFcPayloadOff = 0xA3000u;
constexpr unsigned kMaxFcRow = 40u;

constexpr int kMinCharId = 1;
constexpr int kMaxCharId = 8;

struct PreferredFace {
    std::uint8_t fc_row;
    std::uint8_t face_i;
};
constexpr PreferredFace kPreferredFace[9] = {
    {0, 0}, {1, 0}, {4, 24}, {1, 11}, {10, 37}, {15, 22}, {15, 29}, {15, 34}, {15, 39},
};

std::uint8_t g_override_ids[4]{};
int g_override_count = 0;
bool g_override_on = false;

std::uint8_t g_seed_ids[4]{};
int g_seed_count = 0;
bool g_seed_on = false;

std::uint8_t g_saved_field0a[4]{};
bool g_field0a_saved = false;
bool g_staged = false;
bool g_saw_fight_spawn = false;
std::uint8_t g_last_battle_mode = 0xFFu;
bool g_spawn_hooks_ok = false;
bool g_cull_on = false;
bool g_attach_linked = false;
bool g_attach_body_inited = false;
bool g_attach_scripts_copied = false;
bool g_catalog_scripts_logged = false;
std::uint8_t g_attach_parent_id = 0;
void* g_attach_parent_ptr = nullptr;
void* g_attach_part_ptr[4]{};
unsigned g_attach_part_n = 0;
void* g_attach_lookup_site = nullptr;
std::uint8_t g_attach_lookup_original[8]{};
std::uint32_t g_attach_inited_168 = 0;
HANDLE g_party_poll_thread = nullptr;
volatile LONG g_party_poll_stop = 0;

void* g_battle_setup_site = nullptr;
std::uint8_t g_battle_setup_original[8]{};
void* g_battle_setup_trampoline_mem = nullptr;
void* g_battle_load_site = nullptr;
std::uint8_t g_battle_load_original[8]{};
void* g_battle_load_trampoline_mem = nullptr;
void* g_ally_count_site = nullptr;
std::uint8_t g_ally_count_original[8]{};
void* g_ally_gate_site = nullptr;
std::uint8_t g_ally_gate_original[8]{};
void* g_ally_char_site = nullptr;
std::uint8_t g_ally_char_original[16]{};

void* g_count_site = nullptr;
std::uint8_t g_count_original[8]{};
void* g_char_site = nullptr;
std::uint8_t g_char_original[8]{};
void* g_init_site = nullptr;
std::uint8_t g_init_original[8]{};
void* g_init_trampoline_mem = nullptr;
void* g_model_bind_site = nullptr;
std::uint8_t g_model_bind_original[8]{};
void* g_model_bind_trampoline_mem = nullptr;
void* g_anim_bind_site = nullptr;
std::uint8_t g_anim_bind_original[16]{};
void* g_anim_bind_trampoline_mem = nullptr;
void* g_post_bind_site = nullptr;
std::uint8_t g_post_bind_original[8]{};
void* g_anim_fixup_site = nullptr;
std::uint8_t g_anim_fixup_original[8]{};
void* g_anim_fixup_trampoline_mem = nullptr;
void* g_enemy_model_copy_site = nullptr;
std::uint8_t g_enemy_model_copy_original[8]{};
void* g_enemy_model_copy_trampoline_mem = nullptr;
void* g_enemy_loaded_site = nullptr;
std::uint8_t g_enemy_loaded_original[8]{};
void* g_enemy_loaded_trampoline_mem = nullptr;
void* g_enemy_loaded_alt_site = nullptr;
std::uint8_t g_enemy_loaded_alt_original[8]{};
void* g_enemy_loaded_alt_trampoline_mem = nullptr;
void* g_shop_open_site = nullptr;
std::uint8_t g_shop_open_original[8]{};
void* g_shop_open_trampoline_mem = nullptr;
void* g_shop_sell_open_site = nullptr;
std::uint8_t g_shop_sell_open_original[8]{};
void* g_shop_sell_open_trampoline_mem = nullptr;
void* g_shop_sell_list_site = nullptr;
std::uint8_t g_shop_sell_list_original[8]{};
void* g_shop_sell_sel_site = nullptr;
std::uint8_t g_shop_sell_sel_original[8]{};
void* g_utf8_dec_site = nullptr;
std::uint8_t g_utf8_dec_original[8]{};
void* g_utf8_dec_trampoline_mem = nullptr;
void* g_skill_name_site = nullptr;
std::uint8_t g_skill_name_original[8]{};
void* g_skill_name_trampoline_mem = nullptr;
void* g_skill_parse_site = nullptr;
std::uint8_t g_skill_parse_original[8]{};
void* g_skill_parse_trampoline_mem = nullptr;
void* g_menu_fill_site = nullptr;
std::uint8_t g_menu_fill_original[8]{};
void* g_menu_fill_trampoline_mem = nullptr;
void* g_stash_fill_site = nullptr;
std::uint8_t g_stash_fill_original[8]{};
void* g_stash_fill_trampoline_mem = nullptr;
void* g_item_fill_site = nullptr;
std::uint8_t g_item_fill_original[8]{};
void* g_item_fill_trampoline_mem = nullptr;
void* g_empty_equip_site = nullptr;
std::uint8_t g_empty_equip_original[8]{};
void* g_face_bank_site = nullptr;
std::uint8_t g_face_bank_original[8]{};
void* g_cull_jne_site = nullptr;
std::uint8_t g_cull_jne_original[2]{};
void* g_cull_js_site = nullptr;
std::uint8_t g_cull_js_original[8]{};

std::uint8_t* g_fc_arenas[kMaxFcRow]{};

std::uint8_t* MapObject();
std::uint8_t* CharBlock(std::uint8_t* map_obj, std::uint8_t char_id);
bool ReadFieldParty(std::uint8_t* out);
int CountIds(const std::uint8_t* ids);
int PackPartyIds(std::uint8_t* ids);
bool IsBattleMode(std::uint8_t mode);
void WriteUiPartyCache(std::uintptr_t cache_rva, const std::uint8_t* ids, int n);
void SetBattleCullPatches(bool enable);
void LinkAttachedEnemies(bool game_thread);
void KeepAttachParent();
void ClearAttachParent();
void MarkAttachBody();
void TryInitAttachBody();
void CopyAttachScriptTables(std::uint8_t* ctx, const std::uint8_t* old_species = nullptr);
void* ScriptTablePtrForRow(std::uintptr_t base, std::uint8_t row);
bool Opcode0IsSocketInit(std::uint32_t fn_va);
void FillAttachModelCopies();
bool FillActorModelCopy(std::uint8_t* actor, std::uint8_t* ctx);
bool PtrReadable(const void* p, std::size_t bytes);
void ObserveBattleMode();
void PollPartyRestore(bool field_map_fopen = false);
void CommitFieldPartyRestore();
void ClearFightOverride();
void FillBattleLoadIdentity(BattleLoadNative* req);
void WriteBattleLoadEncounter(const BattleLoadNative* req);
void WriteBattleSpeciesMap(const BattleLoadNative* req);
void WriteBattleSetupEncounter(const BattleLoadNative* req, bool write_table, bool write_approach,
                               bool write_count, bool write_slot);
void StartPartyPoll();
void StopPartyPoll();

std::uint8_t* MapObject() {
    void* ptr = nullptr;
    if (!SafeReadPointer(ModuleBase() + kMapObjPtrRva, &ptr) || !ptr) {
        return nullptr;
    }
    if (reinterpret_cast<std::uintptr_t>(ptr) < 0x10000) {
        return nullptr;
    }
    return static_cast<std::uint8_t*>(ptr);
}

bool ReadFieldParty(std::uint8_t* out) {
    auto* map_obj = MapObject();
    if (!map_obj || !out) {
        return false;
    }
    __try {
        for (int i = 0; i < 4; ++i) {
            out[i] = map_obj[0x0A + i];
        }
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool WriteFieldParty(const std::uint8_t* ids) {
    auto* map_obj = MapObject();
    if (!map_obj || !ids) {
        return false;
    }
    __try {
        for (int i = 0; i < 4; ++i) {
            map_obj[0x0A + i] = ids[i];
        }
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

std::uint8_t* CharBlock(std::uint8_t* map_obj, std::uint8_t char_id) {
    if (!map_obj || char_id < kMinCharId || char_id > kMaxCharId) {
        return nullptr;
    }
    return map_obj + kCharBlockOff + static_cast<std::uintptr_t>(char_id - 1) * kCharStride;
}

int CountIds(const std::uint8_t* ids) {
    int n = 0;
    for (int i = 0; i < 4; ++i) {
        if (ids[i] == 0) {
            break;
        }
        ++n;
    }
    return n;
}

int PackPartyIds(std::uint8_t* ids) {
    if (!ids) {
        return 0;
    }
    std::uint8_t packed[4]{};
    int n = 0;
    for (int i = 0; i < 4; ++i) {
        const std::uint8_t id = ids[i];
        if (id >= kMinCharId && id <= kMaxCharId) {
            packed[n++] = id;
        }
    }
    std::memcpy(ids, packed, 4);
    return n;
}

bool SameParty(const std::uint8_t* a, const std::uint8_t* b) {
    return std::memcmp(a, b, 4) == 0;
}

bool IsBattleMode(std::uint8_t mode) {
    return mode == 2 || mode == 3;
}

bool BattleOverrideActive() {
    if (g_staged) {
        return true;
    }
    if (!g_override_on) {
        return false;
    }
    std::uint8_t mode = 0;
    return SafeReadByte(ModuleBase() + kBattleModeRva, &mode) && IsBattleMode(mode);
}

void ObserveBattleMode() {
    PollPartyRestore(false);
}

void CommitFieldPartyRestore() {
    if (!g_staged && !g_field0a_saved) {
        return;
    }
    SetBattleCullPatches(false);
    ResetPdatBattlePack();
    if (g_field0a_saved) {
        std::uint8_t cur[4]{};
        const bool dirty = !ReadFieldParty(cur) || !SameParty(cur, g_saved_field0a);
        WriteFieldParty(g_saved_field0a);
        if (g_staged || dirty) {
            LogInfo("Party: restored field MapObj+0A to %u,%u,%u,%u", g_saved_field0a[0],
                    g_saved_field0a[1], g_saved_field0a[2], g_saved_field0a[3]);
        }
    }
    ClearAttachParent();
    g_staged = false;
    g_saw_fight_spawn = false;
    g_field0a_saved = false;
    ClearFightOverride();
}

void ClearFightOverride() {
    g_override_on = false;
    g_override_count = 0;
    std::memset(g_override_ids, 0, 4);
}

unsigned EncounterLiveSlots(const std::uint8_t* enc) {
    if (!enc) {
        return 0;
    }
    const unsigned n = enc[6];
    return n > kBattleEncounterMaxSlots ? kBattleEncounterMaxSlots : n;
}

void CopyEncounterRow(BattleLoadNative* req, const std::uint8_t* row) {
    if (!req || !row) {
        return;
    }
    __try {
        if (PtrReadable(row, kBattleEncounterDump)) {
            std::memcpy(req->encounter, row, kBattleEncounterDump);
            return;
        }
        unsigned n = 1;
        if (PtrReadable(row, 7)) {
            n = EncounterLiveSlots(row);
        }
        const unsigned bytes = kBattleEncounterHeader + n * kBattleEncounterSlotSize;
        if (PtrReadable(row, bytes)) {
            std::memcpy(req->encounter, row, bytes);
        } else if (PtrReadable(row, kBattleEncounterHeader)) {
            std::memcpy(req->encounter, row, kBattleEncounterHeader);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
    }
}

bool WriteEncounterSlots(std::uint8_t* row, const BattleLoadNative* req) {
    if (!row || !req) {
        return false;
    }
    const unsigned n = EncounterLiveSlots(req->encounter);
    const unsigned bytes = n * kBattleEncounterSlotSize;
    const unsigned need = kBattleEncounterHeader + bytes;
    if (!PtrReadable(row, need < kBattleEncounterHeader ? kBattleEncounterHeader : need)) {
        return false;
    }
    row[6] = req->encounter[6];
    if (bytes > 0) {
        std::memcpy(row + kBattleEncounterHeader, req->encounter + kBattleEncounterHeader, bytes);
    }
    return true;
}

void FillBattleLoadIdentity(BattleLoadNative* req) {
    if (!req) {
        return;
    }

    const auto base = ModuleBase();
    SafeReadByte(base + kBattleModeRva, &req->battle_mode);

    if (auto* map_obj = MapObject()) {
        __try {
            req->map = *reinterpret_cast<std::uint16_t*>(map_obj + 8);
            req->dest = *reinterpret_cast<std::uint16_t*>(map_obj + 0x5B2);
            req->spawn = *reinterpret_cast<std::uint16_t*>(map_obj + 0x5B4);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
        }
    }

    void* ctxp = nullptr;
    if (SafeReadPointer(base + kBattleCtxPtrRva, &ctxp) && ctxp && PtrReadable(ctxp, 0x244u)) {
        auto* ctx = static_cast<std::uint8_t*>(ctxp);
        req->formation = ctx[0x243];

        if (PtrReadable(ctx + 0x64a07u, 16)) {
            auto* m = ctx + 0x64a07u;
            std::memcpy(req->species, m, 16);
        }
        if (PtrReadable(ctx + 0x7A200u, 4)) {
            const std::uint32_t rel = *reinterpret_cast<std::uint32_t*>(ctx + 0x7A200u);
            auto* pack = ctx + 0x7A200u + rel + static_cast<std::uintptr_t>(req->formation) * 8u;
            if (PtrReadable(pack, 8)) {
                const auto* w = reinterpret_cast<const std::uint16_t*>(pack);
                req->pack_w0 = w[0];
                req->pack_w2 = w[1];
                req->pack_w4 = w[2];
                req->pack_w6 = w[3];
            }
        }
    }

    void* obj = nullptr;
    if (SafeReadPointer(base + kBattleEncObjPtrRva, &obj) && obj) {
        req->enc_obj = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(obj));
    }

    void* row = nullptr;
    if (SafeReadPointer(base + kBattleEncRowPtrRva, &row) && row) {
        req->enc_row = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(row));
    } else if (obj && PtrReadable(static_cast<std::uint8_t*>(obj) + 4, 4)) {
        void* from_obj = nullptr;
        if (SafeReadPointer(reinterpret_cast<std::uintptr_t>(obj) + 4u, &from_obj) && from_obj) {
            row = from_obj;
            req->enc_row = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(row));
        }
    }
    if (row) {
        CopyEncounterRow(req, static_cast<const std::uint8_t*>(row));
    }
}

void WriteBattleLoadEncounter(const BattleLoadNative* req) {
    if (!req || req->enc_row == 0) {
        return;
    }
    auto* row = static_cast<std::uint8_t*>(
        reinterpret_cast<void*>(static_cast<std::uintptr_t>(req->enc_row)));
    const unsigned n = EncounterLiveSlots(req->encounter);
    const unsigned need = kBattleEncounterHeader + n * kBattleEncounterSlotSize;
    if (!PtrReadable(row, need < kBattleEncounterHeader ? kBattleEncounterHeader : need)) {
        return;
    }
    __try {
        // Groups + group count. Byte 13 (EncounterRow) stays the field group.
        if (!WriteEncounterSlots(row, req)) {
            LogWarn("OnBattleLoad: failed to write encounter slots");
            return;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("OnBattleLoad: failed to write encounter slots");
        return;
    }
}

void WriteBattleSpeciesMap(const BattleLoadNative* req) {
    if (!req) {
        return;
    }
    void* ctxp = nullptr;
    if (!SafeReadPointer(ModuleBase() + kBattleCtxPtrRva, &ctxp) || !ctxp) {
        return;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    if (!PtrReadable(ctx + 0x64a07u, 16)) {
        return;
    }
    std::uint8_t old_species[16]{};
    __try {
        std::memcpy(old_species, ctx + 0x64a07u, 16);
        std::memcpy(ctx + 0x64a07u, req->species, 16);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("OnBattleLoad: failed to write species map");
        return;
    }
    CopyAttachScriptTables(ctx, old_species);
}

void WriteBattleSetupEncounter(const BattleLoadNative* req, bool write_table, bool write_approach,
                               bool write_count, bool write_slot) {
    if (!req || req->enc_row == 0) {
        return;
    }
    auto* row = static_cast<std::uint8_t*>(
        reinterpret_cast<void*>(static_cast<std::uintptr_t>(req->enc_row)));
    const unsigned n = EncounterLiveSlots(req->encounter);
    const unsigned need = kBattleEncounterHeader + n * kBattleEncounterSlotSize;
    if (!PtrReadable(row, need < kBattleEncounterHeader ? kBattleEncounterHeader : need)) {
        return;
    }
    __try {
        if (write_table) {
            row[1] = req->encounter[1];
        }
        if (write_approach) {
            row[11] = req->encounter[11];
        }
        if (write_count || write_slot) {
            if (!WriteEncounterSlots(row, req)) {
                LogWarn("OnBattleSetup: failed to write encounter");
                return;
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("OnBattleSetup: failed to write encounter");
        return;
    }
}

void PollPartyRestore(bool field_map_fopen) {
    std::uint8_t mode = 0;
    if (!SafeReadByte(ModuleBase() + kBattleModeRva, &mode)) {
        return;
    }
    const bool in_battle = IsBattleMode(mode);
    const bool was_battle = IsBattleMode(g_last_battle_mode);
    if (in_battle) {
        LinkAttachedEnemies(false);
    }
    if (in_battle && g_staged) {
        SetBattleCullPatches(true);
    }
    bool restore = (was_battle && !in_battle) ||
                   (mode == 0 && g_last_battle_mode != 0 && g_last_battle_mode != 0xFFu);
    if (field_map_fopen && g_saw_fight_spawn && g_staged && !in_battle) {
        restore = true;
    }
    g_last_battle_mode = mode;
    if (restore) {
        CommitFieldPartyRestore();
    }
}

static DWORD WINAPI PartyPollThreadProc(LPVOID) {
    while (InterlockedCompareExchange(&g_party_poll_stop, 0, 0) == 0) {
        PollPartyRestore(false);
        Sleep(50);
    }
    return 0;
}

void StartPartyPoll() {
    if (g_party_poll_thread) {
        return;
    }
    InterlockedExchange(&g_party_poll_stop, 0);
    g_party_poll_thread = CreateThread(nullptr, 0, PartyPollThreadProc, nullptr, 0, nullptr);
    if (g_party_poll_thread) {
        LogInfo("Party field-restore poll started");
    } else {
        LogWarn("Party field-restore poll CreateThread failed %lu", GetLastError());
    }
}

void StopPartyPoll() {
    if (!g_party_poll_thread) {
        return;
    }
    InterlockedExchange(&g_party_poll_stop, 1);
    WaitForSingleObject(g_party_poll_thread, 250);
    CloseHandle(g_party_poll_thread);
    g_party_poll_thread = nullptr;
}

int SeedMissingCharacterBlocks(std::uint8_t* map_obj, const std::uint8_t* ids, int n) {
    if (!map_obj || !ids || n <= 0) {
        return 0;
    }
    const std::uint8_t* tmpl = nullptr;
    auto* justin = CharBlock(map_obj, 1);
    if (justin && justin[3] != 0) {
        tmpl = justin;
    }
    if (!tmpl) {
        for (int i = 0; i < n; ++i) {
            auto* blk = CharBlock(map_obj, ids[i]);
            if (blk && blk[3] != 0) {
                tmpl = blk;
                break;
            }
        }
    }
    int seeded = 0;
    for (int i = 0; i < n; ++i) {
        auto* blk = CharBlock(map_obj, ids[i]);
        if (!blk || blk[3] != 0) {
            continue;
        }
        if (tmpl && tmpl != blk) {
            std::memcpy(blk, tmpl, static_cast<std::size_t>(kCharStride));
            std::memset(blk + kCharEquipOff, 0, (kCharEquipSlots + kCharInvSlots) * sizeof(std::uint16_t));
        } else {
            std::memset(blk, 0, static_cast<std::size_t>(kCharStride));
            blk[3] = 1;
            *reinterpret_cast<std::uint16_t*>(blk + 0x0A) = 100;
            *reinterpret_cast<std::uint16_t*>(blk + 0x0C) = 100;
            *reinterpret_cast<std::uint16_t*>(blk + 0x16) = 50;
            *reinterpret_cast<std::uint16_t*>(blk + 0x18) = 50;
        }
        ++seeded;
        LogInfo("Party: seeded char block id=%u level=%u", ids[i], blk[3]);
    }
    return seeded;
}

void SnapshotFieldParty() {
    std::uint8_t cur[4]{};
    if (!ReadFieldParty(cur)) {
        return;
    }
    std::memcpy(g_saved_field0a, cur, 4);
    g_field0a_saved = true;
}

void PatchFormationTableForBattle() {
    void* ctxp = nullptr;
    void* tablep = nullptr;
    if (!SafeReadPointer(ModuleBase() + kBattleCtxPtrRva, &ctxp) || !ctxp) {
        return;
    }
    if (!SafeReadPointer(ModuleBase() + kBattleFormTablePtrRva, &tablep) || !tablep) {
        return;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    auto* table = static_cast<std::uint8_t*>(tablep);
    if (!PtrReadable(ctx, 0x244u) || !PtrReadable(table, 0x40u)) {
        return;
    }
    const std::uint8_t form = ctx[0x243];
    const std::uint8_t n = static_cast<std::uint8_t>(g_override_count);
    table[form] = n;
    for (std::uint8_t slot = 1; slot <= 4u; ++slot) {
        const std::uint8_t id = (slot <= n) ? g_override_ids[slot - 1u] : 0;
        table[static_cast<std::uint32_t>(form) * 4u + slot + 0x10u] = id;
    }
    LogInfo("Party formation form=%u count=%u ids=%u,%u,%u,%u", form, n, g_override_ids[0],
            g_override_ids[1], g_override_ids[2], g_override_ids[3]);
}

unsigned StockAllyCount(unsigned formation_index) {
    void* table = nullptr;
    if (!SafeReadPointer(ModuleBase() + kBattleFormTablePtrRva, &table) || !table) {
        return 0;
    }
    std::uint8_t count = 0;
    if (!SafeReadByte(reinterpret_cast<std::uintptr_t>(table) + (formation_index & 0xFFu), &count)) {
        return 0;
    }
    return count;
}

void WriteUiPartyCache(std::uintptr_t cache_rva, const std::uint8_t* ids, int n) {
    auto* cache = reinterpret_cast<std::uint8_t*>(ModuleBase() + cache_rva);
    std::memset(cache, 0, 4);
    if (!ids || n <= 0) {
        return;
    }
    std::uint8_t wrote = 0;
    for (int slot = 0; slot < n && wrote < 4; ++slot) {
        const std::uint8_t id = ids[slot];
        if (id < kMinCharId || id > kMaxCharId) {
            continue;
        }
        cache[wrote++] = id;
    }
}

void SetBattleCullPatches(bool enable) {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    if (enable == g_cull_on) {
        return;
    }

    auto* jne = reinterpret_cast<std::uint8_t*>(base + kBattleAllySlotSkipRva);
    if (!g_cull_jne_site) {
        if (jne[0] != 0x75 || jne[1] != 0x15) {
            LogWarn("Party cull JNE mismatch at +0x%X", static_cast<unsigned>(kBattleAllySlotSkipRva));
        } else {
            std::memcpy(g_cull_jne_original, jne, 2);
            g_cull_jne_site = jne;
        }
    }
    if (g_cull_jne_site) {
        const std::uint8_t nops[2] = {0x90, 0x90};
        RestoreBytes(g_cull_jne_site, enable ? nops : g_cull_jne_original, 2);
    }

    auto* js = reinterpret_cast<std::uint8_t*>(base + kBattleFa90EarlyExitJsRva);
    if (!g_cull_js_site) {
        if (js[0] != 0x0F || js[1] != 0x88) {
            LogWarn("Party cull JS mismatch at +0x%X", static_cast<unsigned>(kBattleFa90EarlyExitJsRva));
        } else {
            std::memcpy(g_cull_js_original, js, kBattleFa90EarlyExitJsSize);
            g_cull_js_site = js;
        }
    }
    if (g_cull_js_site) {
        std::uint8_t nops[6] = {0x90, 0x90, 0x90, 0x90, 0x90, 0x90};
        RestoreBytes(g_cull_js_site, enable ? nops : g_cull_js_original, kBattleFa90EarlyExitJsSize);
    }
    g_cull_on = enable;
}

bool IsAttachFormRow(std::uint8_t row) {
    switch (row) {
        case 0x97:
        case 0x98:
        case 0x9A:
        case 0x9B:
        case 0xDD:
        case 0xDF:
        case 0xE0:
        case 0xF0:
        case 0xFB:
        case 0xFC:
            return true;
        default:
            return false;
    }
}

std::uintptr_t AttachScriptSliceRva(std::uint8_t row) {
    switch (row) {
        case 0x96:
            return kSquidScriptTableRva;
        case 0x97:
            return kSquidScriptTableRva + kScriptSlotSize;
        case 0x98:
            return kSquidScriptTableRva + kScriptSlotSize * 2u;
        case 0x99:
            return kKrakenScriptTableRva;
        case 0x9A:
            return kKrakenScriptTableRva + kScriptSlotSize;
        case 0x9B:
            return kKrakenScriptTableRva + kScriptSlotSize * 2u;
        default:
            return 0;
    }
}

void* ScriptTablePtrForRow(std::uintptr_t base, std::uint8_t row) {
    if (base == 0 || row == 0) {
        return nullptr;
    }
    const std::uintptr_t slice = AttachScriptSliceRva(row);
    if (slice != 0) {
        auto* src = reinterpret_cast<void*>(base + slice);
        return PtrReadable(src, kScriptSlotSize) ? src : nullptr;
    }
    if (row > 0x7Du) {
        return nullptr;
    }
    auto* cases = reinterpret_cast<std::uint8_t*>(base + kScriptCaseMapRva);
    auto* jumps = reinterpret_cast<std::uint8_t*>(base + kScriptJumpTableRva);
    if (!PtrReadable(cases, 0x7Eu) || !PtrReadable(jumps, 0x80u * 4u)) {
        return nullptr;
    }
    const std::uint8_t cse = cases[row];
    std::uint32_t stub_va = 0;
    std::memcpy(&stub_va, jumps + static_cast<unsigned>(cse) * 4u, 4);
    auto* stub = reinterpret_cast<std::uint8_t*>(static_cast<std::uintptr_t>(stub_va));
    if (!PtrReadable(stub, 12u) || stub[0] != 0xB8 || stub[5] != 0xB9) {
        return nullptr;
    }
    std::uint32_t script_va = 0;
    std::memcpy(&script_va, stub + 6, 4);
    auto* src = reinterpret_cast<void*>(static_cast<std::uintptr_t>(script_va));
    return PtrReadable(src, kScriptSlotSize) ? src : nullptr;
}

bool Opcode0IsSocketInit(std::uint32_t fn_va) {
    if (fn_va < 0x10000u) {
        return false;
    }
    auto* p = reinterpret_cast<const std::uint8_t*>(static_cast<std::uintptr_t>(fn_va));
    constexpr std::size_t kScan = 0xC0u;
    if (!PtrReadable(p, kScan) || p[0] != 0x55 || p[1] != 0x8B || p[2] != 0xEC) {
        return false;
    }
    const auto want = ModuleBase() + kSocketHelperRva;
    unsigned n = 0;
    for (std::size_t i = 0; i + 5u <= kScan; ++i) {
        if (p[i] != 0xE8) {
            continue;
        }
        std::int32_t rel = 0;
        std::memcpy(&rel, p + i + 1u, 4);
        const auto dest = static_cast<std::uintptr_t>(fn_va) + i + 5u +
                          static_cast<std::uintptr_t>(rel);
        if (dest == want) {
            ++n;
        }
    }
    return n >= 2u;
}

void CopyAttachScriptTables(std::uint8_t* ctx, const std::uint8_t* old_species) {
    if (!ctx || !PtrReadable(ctx + 0x64a07u, 16) ||
        !PtrReadable(ctx + kCtxScriptCatalogRva, kScriptSlotSize * 8u)) {
        return;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    const bool log = !g_catalog_scripts_logged;
    unsigned n = 0;
    unsigned attach_n = 0;
    __try {
        for (unsigned cat = 1; cat <= 8u; ++cat) {
            const std::uint8_t row = ctx[0x64a07u + cat];
            const bool attach = AttachScriptSliceRva(row) != 0;
            const bool remapped = old_species && old_species[cat] != row;
            if (!attach && !remapped) {
                continue;
            }
            void* src = ScriptTablePtrForRow(base, row);
            if (!src) {
                continue;
            }
            auto* dest = ctx + kCtxScriptCatalogRva + (cat - 1u) * kScriptSlotSize;
            if (!PtrReadable(dest, kScriptSlotSize)) {
                continue;
            }
            std::uint32_t src_op0 = 0;
            std::memcpy(&src_op0, src, 4);
            std::memcpy(dest, src, kScriptSlotSize);
            const char* op0_how = "copy";
            if (attach) {
                op0_how = "replace";
            } else if (Opcode0IsSocketInit(src_op0)) {
                // 109FA0/ADFA0 call 140D00 at spawn and AV on a foreign map.
                // Do not run them, and do not keep the stolen slot's opcode 0
                // (Centipede F4970 writes +0xB0/+0x14A onto the new mesh).
                const auto ret_va = static_cast<std::uint32_t>(base + kRetStubRva);
                std::memcpy(dest, &ret_va, 4);
                op0_how = "ret";
            }
            ++n;
            if (attach) {
                ++attach_n;
            }
            if (log) {
                LogInfo("Catalog scripts cat=%u row=%02X src=%p op0=%s", cat, row, src, op0_how);
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("Catalog scripts copy failed");
        return;
    }
    if (n > 0) {
        g_catalog_scripts_logged = true;
    }
    if (attach_n > 0) {
        g_attach_scripts_copied = true;
    }
}

void ClearAttachParent() {
    g_attach_linked = false;
    g_attach_body_inited = false;
    g_attach_scripts_copied = false;
    g_catalog_scripts_logged = false;
    g_attach_parent_id = 0;
    g_attach_parent_ptr = nullptr;
    g_attach_part_n = 0;
    std::memset(g_attach_part_ptr, 0, sizeof(g_attach_part_ptr));
    g_attach_inited_168 = 0;
}

void MarkAttachBody() {
    if (!g_attach_parent_ptr || !PtrReadable(g_attach_parent_ptr, 0x17Cu)) {
        return;
    }
    auto* body = static_cast<std::uint8_t*>(g_attach_parent_ptr);
    std::uint32_t model_raw = 0;
    std::memcpy(&model_raw, body + 0x9C, 4);
    if (model_raw < 0x10000u) {
        return;
    }
    auto* model = reinterpret_cast<std::uint8_t*>(static_cast<std::uintptr_t>(model_raw));
    if (!PtrReadable(model, 0x38u)) {
        return;
    }
    std::uint16_t flags = 0;
    std::memcpy(&flags, model + 0x36, 2);
    flags = static_cast<std::uint16_t>(flags | kAttachBodyModelFlag);
    std::memcpy(model + 0x36, &flags, 2);
}

bool FillActorModelCopy(std::uint8_t* actor, std::uint8_t* ctx) {
    if (!actor || !ctx || !PtrReadable(actor, 0x16Cu) || !PtrReadable(ctx, 0xec48u + 16u * 4u)) {
        return false;
    }
    const std::uint8_t idx = actor[0x15A];
    if (idx == 0 || idx > 16u) {
        return false;
    }
    std::uint32_t blob_raw = 0;
    std::memcpy(&blob_raw, ctx + 0xE69Cu + static_cast<unsigned>(idx) * 4u, 4);
    if (blob_raw < 0x10000u) {
        return false;
    }
    auto* blob = reinterpret_cast<std::uint8_t*>(static_cast<std::uintptr_t>(blob_raw));
    if (!PtrReadable(blob, 0x10u)) {
        return false;
    }
    std::uint32_t off4 = 0;
    std::uint32_t off8 = 0;
    std::uint32_t offc = 0;
    std::memcpy(&off4, blob + 4, 4);
    std::memcpy(&off8, blob + 8, 4);
    std::memcpy(&offc, blob + 0xC, 4);
    const std::uint32_t p98 = blob_raw + off4;
    const std::uint32_t p168 = blob_raw + off8;
    const std::uint32_t p9c = blob_raw + offc;
    if (off8 == 0 || offc == 0 || p168 == p9c) {
        return false;
    }
    std::memcpy(actor + 0x98, &p98, 4);
    std::memcpy(actor + 0x168, &p168, 4);
    std::memcpy(actor + 0x164, &blob_raw, 4);
    std::memcpy(actor + 0x9C, &p9c, 4);
    std::uint32_t a0 = 0;
    std::memcpy(&a0, ctx + 0xEC48u + static_cast<unsigned>(idx) * 4u, 4);
    std::memcpy(actor + 0xA0, &a0, 4);
    if (PtrReadable(reinterpret_cast<void*>(static_cast<std::uintptr_t>(p9c)), 0x38u)) {
        std::uint16_t flags = 0;
        std::memcpy(&flags, reinterpret_cast<void*>(static_cast<std::uintptr_t>(p9c) + 0x36u), 2);
        if ((flags & kAttachModelFlag) == kAttachModelFlag) {
            const std::uint16_t h = static_cast<std::uint16_t>(actor[4] + 0x300u);
            std::memcpy(actor + 0x10, &h, 2);
        }
    }
    return true;
}

void FillAttachModelCopies() {
    void* ctxp = nullptr;
    if (!SafeReadPointer(ModuleBase() + kBattleCtxPtrRva, &ctxp) || !ctxp) {
        return;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    if (g_attach_parent_ptr && PtrReadable(g_attach_parent_ptr, 0x16Cu)) {
        FillActorModelCopy(static_cast<std::uint8_t*>(g_attach_parent_ptr), ctx);
    }
    for (unsigned i = 0; i < g_attach_part_n; ++i) {
        if (g_attach_part_ptr[i] && PtrReadable(g_attach_part_ptr[i], 0x16Cu)) {
            FillActorModelCopy(static_cast<std::uint8_t*>(g_attach_part_ptr[i]), ctx);
        }
    }
}

void TryInitAttachBody() {
    if (g_attach_scripts_copied) {
        return;
    }
    if (!g_attach_parent_ptr || !PtrReadable(g_attach_parent_ptr, 0x16Cu)) {
        return;
    }
    auto* body = static_cast<std::uint8_t*>(g_attach_parent_ptr);
    std::uint32_t p168 = 0;
    std::uint32_t p9c = 0;
    std::memcpy(&p168, body + 0x168, 4);
    std::memcpy(&p9c, body + 0x9C, 4);
    if (g_attach_body_inited && p168 == g_attach_inited_168 && g_attach_inited_168 != 0) {
        return;
    }
    void* ctxp = nullptr;
    std::uint32_t a878 = 0;
    if (SafeReadPointer(ModuleBase() + kBattleCtxPtrRva, &ctxp) && ctxp &&
        PtrReadable(ctxp, 0xA87Cu)) {
        std::memcpy(&a878, static_cast<std::uint8_t*>(ctxp) + 0xA878u, 4);
    }
    if (p168 < 0x10000u || p9c < 0x10000u || p168 == p9c || a878 < 0x10000u ||
        !PtrReadable(reinterpret_cast<void*>(static_cast<std::uintptr_t>(p168)), 4u) ||
        !PtrReadable(reinterpret_cast<void*>(static_cast<std::uintptr_t>(p9c)), 0x38u) ||
        !PtrReadable(reinterpret_cast<void*>(static_cast<std::uintptr_t>(a878)), 8u)) {
        return;
    }
    __try {
        using AttachBodyFn = void(__cdecl*)(void*);
        const auto fn = reinterpret_cast<AttachBodyFn>(ModuleBase() + kAttachBodyInitRva);
        fn(g_attach_parent_ptr);
        g_attach_body_inited = true;
        g_attach_inited_168 = p168;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("Attach body init failed");
    }
}

void AssignAttachPartParents() {
    if (!g_attach_parent_id) {
        return;
    }
    for (unsigned i = 0; i < g_attach_part_n; ++i) {
        if (!g_attach_part_ptr[i] || !PtrReadable(g_attach_part_ptr[i], 8u)) {
            continue;
        }
        static_cast<std::uint8_t*>(g_attach_part_ptr[i])[5] = g_attach_parent_id;
        if (g_attach_parent_ptr && PtrReadable(g_attach_parent_ptr, 0x119u) &&
            PtrReadable(g_attach_part_ptr[i], 0x119u)) {
            static_cast<std::uint8_t*>(g_attach_part_ptr[i])[0x118] =
                static_cast<std::uint8_t*>(g_attach_parent_ptr)[0x118];
        }
    }
}

void CopyAttachSkeletons() {
    if (!g_attach_parent_ptr || !PtrReadable(g_attach_parent_ptr, 0x70u)) {
        return;
    }
    auto* body = static_cast<std::uint8_t*>(g_attach_parent_ptr);
    std::uint32_t p64 = 0;
    std::uint32_t p6c = 0;
    std::memcpy(&p64, body + 0x64, 4);
    std::memcpy(&p6c, body + 0x6C, 4);
    if (p64 < 0x10000u) {
        return;
    }
    for (unsigned i = 0; i < g_attach_part_n; ++i) {
        if (!g_attach_part_ptr[i] || !PtrReadable(g_attach_part_ptr[i], 0x70u)) {
            continue;
        }
        auto* part = static_cast<std::uint8_t*>(g_attach_part_ptr[i]);
        std::memcpy(part + 0x64, &p64, 4);
        std::memcpy(part + 0x6C, &p6c, 4);
    }
}

void KeepAttachParent() {
    if (!g_attach_parent_ptr) {
        return;
    }
    void* ctxp = nullptr;
    if (SafeReadPointer(ModuleBase() + kBattleCtxPtrRva, &ctxp) && ctxp &&
        PtrReadable(ctxp, 0x251u)) {
        static_cast<std::uint8_t*>(ctxp)[0x250] = g_attach_parent_id;
    }
    const auto base = ModuleBase();
    auto* table = reinterpret_cast<std::uint8_t*>(base + kCombatantTableRva);
    if (PtrReadable(table, (static_cast<unsigned>(g_attach_parent_id) + 1u) * 8u)) {
        table[static_cast<unsigned>(g_attach_parent_id) * 8u] = 1;
        const auto raw = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(g_attach_parent_ptr));
        std::memcpy(table + static_cast<unsigned>(g_attach_parent_id) * 8u + 4u, &raw, 4);
    }
    AssignAttachPartParents();
    CopyAttachSkeletons();
    MarkAttachBody();
}

void LinkAttachedEnemies(bool game_thread) {
    if (g_attach_linked) {
        KeepAttachParent();
        if (game_thread) {
            TryInitAttachBody();
        }
        return;
    }

    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    void* ctxp = nullptr;
    if (!SafeReadPointer(base + kBattleCtxPtrRva, &ctxp) || !ctxp ||
        !PtrReadable(ctxp, 0x64a07u + 16u)) {
        return;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    CopyAttachScriptTables(ctx);
    auto* table = reinterpret_cast<std::uint8_t*>(base + kCombatantTableRva);
    if (!PtrReadable(table, kCombatantTableCount * 8u)) {
        return;
    }

    bool map_has_attach = false;
    for (unsigned i = 1; i < 16u; ++i) {
        if (IsAttachFormRow(ctx[0x64a07u + i])) {
            map_has_attach = true;
            break;
        }
    }

    void* found[8]{};
    std::uint8_t found_id[8]{};
    bool found_attach[8]{};
    unsigned occ = 0;
    __try {
        for (unsigned id = 0; id < kCombatantTableCount && occ < 8u; ++id) {
            if (table[id * 8u] == 0) {
                continue;
            }
            std::uint32_t raw = 0;
            std::memcpy(&raw, table + id * 8u + 4u, 4);
            if (raw < 0x10000u) {
                continue;
            }
            auto* actor = reinterpret_cast<std::uint8_t*>(static_cast<std::uintptr_t>(raw));
            if (!PtrReadable(actor, 0x190u)) {
                continue;
            }
            bool attach = false;
            std::uint32_t model_raw = 0;
            std::memcpy(&model_raw, actor + 0x9C, 4);
            if (model_raw >= 0x10000u) {
                auto* model = reinterpret_cast<std::uint8_t*>(static_cast<std::uintptr_t>(model_raw));
                if (PtrReadable(model, 0x38u)) {
                    std::uint16_t flags = 0;
                    std::memcpy(&flags, model + 0x36, 2);
                    attach = (flags & kAttachModelFlag) == kAttachModelFlag;
                }
            }
            std::uint8_t catalog = actor[0x189];
            if (catalog == 0) {
                catalog = actor[0x25];
            }
            if (!attach && catalog < 16u) {
                attach = IsAttachFormRow(ctx[0x64a07u + catalog]);
            }
            found[occ] = actor;
            found_id[occ] = static_cast<std::uint8_t>(id);
            found_attach[occ] = attach;
            ++occ;
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return;
    }

    if (occ == 0) {
        return;
    }
    if (!map_has_attach) {
        g_attach_linked = true;
        return;
    }

    unsigned attachments = 0;
    for (unsigned i = 0; i < occ; ++i) {
        if (found_attach[i]) {
            ++attachments;
        }
    }
    if (attachments == 0 && occ >= 2u) {
        found_attach[0] = false;
        for (unsigned i = 1; i < occ; ++i) {
            found_attach[i] = true;
            ++attachments;
        }
    }

    void* body = nullptr;
    std::uint8_t body_id = 0;
    g_attach_part_n = 0;
    for (unsigned i = 0; i < occ; ++i) {
        if (found_attach[i]) {
            if (g_attach_part_n < 4u) {
                g_attach_part_ptr[g_attach_part_n++] = found[i];
            }
            continue;
        }
        if (!body) {
            body = found[i];
            body_id = found_id[i];
        }
    }

    if (!body || attachments == 0) {
        return;
    }

    g_attach_linked = true;
    g_attach_parent_id = body_id;
    g_attach_parent_ptr = body;
    KeepAttachParent();
    if (game_thread) {
        TryInitAttachBody();
    }
}

bool FileExistsA(const char* path) {
    const DWORD attrs = GetFileAttributesA(path);
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

bool ReadFileAll(const char* path, std::vector<std::uint8_t>* out) {
    if (!path || !out) {
        return false;
    }
    FILE* f = nullptr;
    if (fopen_s(&f, path, "rb") != 0 || !f) {
        return false;
    }
    if (fseek(f, 0, SEEK_END) != 0) {
        fclose(f);
        return false;
    }
    const long sz = ftell(f);
    if (sz <= 0 || fseek(f, 0, SEEK_SET) != 0) {
        fclose(f);
        return false;
    }
    out->resize(static_cast<std::size_t>(sz));
    const std::size_t got = fread(out->data(), 1, out->size(), f);
    fclose(f);
    return got == out->size();
}

bool ResolveFcDatPath(std::uint8_t fc_row, char* out, std::size_t out_size) {
    if (fc_row == 0 || !out || out_size == 0) {
        return false;
    }
    char exe_path[MAX_PATH]{};
    if (!GetModuleFileNameA(GetModuleHandleW(nullptr), exe_path, MAX_PATH)) {
        return false;
    }
    std::string dir = exe_path;
    const auto slash = dir.find_last_of("\\/");
    dir = slash == std::string::npos ? "." : dir.substr(0, slash);
    char rel[64]{};
    const char* fmts[] = {"\\content\\FIELD\\FC%02u.DAT", "\\content\\FIELD\\fc%02u.dat",
                          "\\content\\field\\fc%02u.dat"};
    for (const char* fmt : fmts) {
        std::snprintf(rel, sizeof(rel), fmt, fc_row);
        if (std::snprintf(out, out_size, "%s%s", dir.c_str(), rel) <= 0) {
            continue;
        }
        if (FileExistsA(out)) {
            return true;
        }
    }
    return false;
}

std::uint8_t* EnsureFcBank(std::uint8_t fc_row) {
    if (fc_row == 0 || fc_row >= kMaxFcRow) {
        return nullptr;
    }
    if (g_fc_arenas[fc_row]) {
        return g_fc_arenas[fc_row];
    }
    char path[MAX_PATH]{};
    if (!ResolveFcDatPath(fc_row, path, sizeof(path))) {
        return nullptr;
    }
    std::vector<std::uint8_t> file;
    if (!ReadFileAll(path, &file) || file.empty()) {
        return nullptr;
    }
    const std::size_t alloc_size = kFcPayloadOff + file.size() + 0x1000u;
    auto* arena = static_cast<std::uint8_t*>(
        VirtualAlloc(nullptr, alloc_size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    if (!arena) {
        return nullptr;
    }
    std::memcpy(arena + kFcPayloadOff, file.data(), file.size());
    g_fc_arenas[fc_row] = arena;
    LogInfo("Party face: FC%02u bank %p", fc_row, arena);
    return arena;
}

void FreeFcBanks() {
    for (unsigned i = 0; i < kMaxFcRow; ++i) {
        if (g_fc_arenas[i]) {
            VirtualFree(g_fc_arenas[i], 0, MEM_RELEASE);
            g_fc_arenas[i] = nullptr;
        }
    }
}

void* EnsureFaceDest() {
    const auto base = ModuleBase();
    auto** slot = reinterpret_cast<std::uint8_t**>(base + kFaceDestPtrRva);
    if (*slot) {
        return *slot + 0x30000;
    }
    void* mem = VirtualAlloc(nullptr, 0x140000, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!mem) {
        return nullptr;
    }
    *slot = reinterpret_cast<std::uint8_t*>(mem) + 0x40000;
    return *slot + 0x30000;
}

bool PickFace(std::uint8_t char_id, std::uint8_t* out_fc, std::uint8_t* out_face_i) {
    if (!out_fc || !out_face_i || char_id < 1 || char_id > 8) {
        return false;
    }
    const auto base = ModuleBase();
    if (auto* map_obj = MapObject()) {
        const std::uint8_t row = map_obj[0x51];
        if (row > 0 && row < kMaxFcRow) {
            const auto* table = reinterpret_cast<const std::uint8_t*>(base + kCostumeStatusTableRva);
            const std::uint8_t variant =
                table[static_cast<std::size_t>(char_id) + static_cast<std::size_t>(row) * 8u];
            if (variant != 0) {
                *out_fc = row;
                *out_face_i = static_cast<std::uint8_t>(variant - 1u);
                return true;
            }
        }
    }
    *out_fc = kPreferredFace[char_id].fc_row;
    *out_face_i = kPreferredFace[char_id].face_i;
    return kPreferredFace[char_id].fc_row != 0;
}

void CallFaceLoad(void* face_fn, unsigned handle, unsigned face_i, void* dest) {
    if (!face_fn || !dest) {
        return;
    }
#if defined(_M_IX86)
    __asm {
        push ebx
        push esi
        push edi
        mov ecx, handle
        mov edx, face_i
        mov eax, face_fn
        push dest
        call eax
        add esp, 4
        pop edi
        pop esi
        pop ebx
    }
#else
    (void)handle;
    (void)face_i;
#endif
}

void RunUiFaces(std::uintptr_t cache_rva, std::uintptr_t handle_table_rva);

bool PtrReadable(const void* p, std::size_t bytes) {
    if (!p || bytes == 0) {
        return false;
    }
    MEMORY_BASIC_INFORMATION mbi{};
    if (VirtualQuery(p, &mbi, sizeof(mbi)) == 0) {
        return false;
    }
    if (mbi.State != MEM_COMMIT) {
        return false;
    }
    const DWORD prot = mbi.Protect & 0xFFu;
    if (prot == PAGE_NOACCESS || prot == PAGE_EXECUTE || (mbi.Protect & PAGE_GUARD) != 0) {
        return false;
    }
    const auto start = reinterpret_cast<const std::uint8_t*>(mbi.BaseAddress);
    const auto end = start + mbi.RegionSize;
    const auto ptr = reinterpret_cast<const std::uint8_t*>(p);
    return ptr >= start && ptr + bytes <= end;
}

bool ModelHeaderReadable(void* header) {
    if (!header || !PtrReadable(header, 8)) {
        return false;
    }
    const std::uint32_t rel = *reinterpret_cast<std::uint32_t*>(header);
    if (rel == 0 || rel > 0x200000u) {
        return false;
    }
    return PtrReadable(static_cast<std::uint8_t*>(header) + rel, 4);
}

void* LeaderModelHeader() {
    void* ctxp = nullptr;
    if (!SafeReadPointer(ModuleBase() + kBattleCtxPtrRva, &ctxp) || !ctxp) {
        return nullptr;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    if (!PtrReadable(ctx + 0xC8A00u, 4)) {
        return nullptr;
    }
    const std::uint32_t rel = *reinterpret_cast<std::uint32_t*>(ctx + 0xC8A00u);
    if (rel == 0) {
        return nullptr;
    }
    auto* header = ctx + 0xC8A00u + rel;
    return ModelHeaderReadable(header) ? header : nullptr;
}

}  // namespace grandia_mod

extern "C" {
void* g_mod_battle_setup_tramp = nullptr;
void* g_mod_battle_load_tramp = nullptr;
void* g_mod_ally_count_resume = nullptr;
void* g_mod_ally_gate_resume = nullptr;
void* g_mod_ally_spawn = nullptr;
void* g_mod_ally_char_resume = nullptr;
void* g_mod_party_count_resume = nullptr;
void* g_mod_party_char_resume = nullptr;
void* g_mod_battle_init_tramp = nullptr;
void* g_mod_model_bind_tramp = nullptr;
void* g_mod_anim_bind_tramp = nullptr;
void* g_mod_menu_fill_tramp = nullptr;
void* g_mod_menu_fill_skip = nullptr;
void* g_mod_stash_fill_tramp = nullptr;
void* g_mod_stash_fill_skip = nullptr;
void* g_mod_item_fill_tramp = nullptr;
void* g_mod_item_fill_skip = nullptr;
void* g_mod_empty_equip_resume = nullptr;
void* g_mod_face_bank_override = nullptr;
void* g_mod_face_bank_resume = nullptr;
std::uintptr_t g_mod_face_bank_slot_abs = 0;
std::uintptr_t g_mod_party_char_table_abs = 0;
std::uint8_t g_mod_party_staged = 0;
void* g_mod_post_bind_resume = nullptr;
void* g_mod_post_bind_skip = nullptr;
void* g_mod_anim_fixup_tramp = nullptr;
void* g_mod_attach_resume = nullptr;
void* g_mod_enemy_model_copy_tramp = nullptr;
void* g_mod_enemy_loaded_tramp = nullptr;
void* g_mod_enemy_loaded_alt_tramp = nullptr;
void* g_mod_shop_open_tramp = nullptr;
void* g_mod_shop_sell_open_tramp = nullptr;
void* g_mod_shop_sell_list_resume = nullptr;
void* g_mod_shop_sell_sel_resume = nullptr;
void* g_mod_utf8_dec_tramp = nullptr;
void* g_mod_skill_name_tramp = nullptr;
void* g_mod_skill_parse_tramp = nullptr;
}

extern "C" void ModOnEnemyPostBind() {
    grandia_mod::LinkAttachedEnemies(true);
}

extern "C" void ModOnEmptyEquipReturn(std::uint16_t* slot, std::uint8_t* chr) {
    if (!slot) {
        return;
    }
    std::uint16_t* bag = nullptr;
    auto in_bag = [&](std::uint8_t* blk) -> std::uint16_t* {
        if (!blk) {
            return nullptr;
        }
        auto* cand = reinterpret_cast<std::uint16_t*>(blk + grandia_mod::kCharInvOff);
        if (slot >= cand && slot < cand + grandia_mod::kCharInvSlots) {
            return cand;
        }
        return nullptr;
    };
    bag = in_bag(chr);
    if (!bag) {
        if (auto* map_obj = grandia_mod::MapObject()) {
            for (int id = grandia_mod::kMinCharId; id <= grandia_mod::kMaxCharId; ++id) {
                bag = in_bag(grandia_mod::CharBlock(map_obj, static_cast<std::uint8_t>(id)));
                if (bag) {
                    break;
                }
            }
        }
    }
    if (!bag) {
        *slot = 0;
        return;
    }
    const int index = static_cast<int>(slot - bag);
    for (int i = index; i + 1 < static_cast<int>(grandia_mod::kCharInvSlots); ++i) {
        bag[i] = bag[i + 1];
    }
    bag[grandia_mod::kCharInvSlots - 1] = 0;
    grandia_mod::LogInfo("Party: empty-slot equip compacted bag index %d", index);
}

extern "C" void ModOnUtf8Null(void* caller) {
    static bool logged = false;
    if (logged) {
        return;
    }
    logged = true;
    grandia_mod::LogWarn("163E0 null string caller=+0x%X",
                         static_cast<unsigned>(grandia_mod::CallerRva(
                             reinterpret_cast<std::uintptr_t>(caller))));
}

extern "C" void ModOnSkillScriptNull(void* caller) {
    static bool logged = false;
    if (logged) {
        return;
    }
    logged = true;
    grandia_mod::LogWarn("9C460 null script caller=+0x%X",
                         static_cast<unsigned>(grandia_mod::CallerRva(
                             reinterpret_cast<std::uintptr_t>(caller))));
}

namespace {

std::uint16_t Read16(const std::uint8_t* p) {
    std::uint16_t v = 0;
    std::memcpy(&v, p, 2);
    return v;
}

void Write16(std::uint8_t* p, int value) {
    auto v = static_cast<std::uint16_t>(value < 0 ? 0 : value > 0xFFFF ? 0xFFFF : value);
    std::memcpy(p, &v, 2);
}

std::int16_t ReadI16(const std::uint8_t* p) {
    std::int16_t v = 0;
    std::memcpy(&v, p, 2);
    return v;
}

void WriteI16(std::uint8_t* p, int value) {
    // Two's complement: -1 and 65535 both store 0xFFFF.
    auto v = static_cast<std::int16_t>(static_cast<std::uint16_t>(value));
    std::memcpy(p, &v, 2);
}

std::uint8_t ClampU8(int value) {
    if (value < 0) {
        return 0;
    }
    if (value > 255) {
        return 255;
    }
    return static_cast<std::uint8_t>(value);
}

std::uint8_t ClampNibble(int value) {
    if (value < 0) {
        return 0;
    }
    if (value > 15) {
        return 15;
    }
    return static_cast<std::uint8_t>(value);
}

bool SkipCString(std::uint8_t*& p, const std::uint8_t* end) {
    while (p < end && *p != 0) {
        ++p;
    }
    if (p >= end) {
        return false;
    }
    ++p;
    return true;
}

void ParseEnemySkills(std::uint8_t* model, std::size_t model_bytes, grandia_mod::EnemyLoadedNative& req,
                      std::uint8_t** hdrs) {
    req.skill_count = 0;
    std::memset(req.skill_name, 0, sizeof(req.skill_name));
    if (!model || model_bytes < 0x5Cu + 22u) {
        return;
    }
    auto* p = model + 0x5C;
    auto* end = model + model_bytes;
    for (unsigned i = 0; i < 8u && p + 22 <= end; ++i) {
        if (p[1] != 1) {
            break;
        }
        if (hdrs) {
            hdrs[i] = p;
        }
        req.skill_power[i] = ReadI16(p + 6);
        req.skill_speed[i] = p[0x13];
        req.skill_element[i] = p[0xA];
        req.skill_effect[i] = p[0xD];
        req.skill_mode[i] = p[0xE];
        req.skill_add[i] = p[0xF];
        req.skill_chance[i] = p[0x10];
        req.skill_add_level[i] = p[0x11];
        req.skill_uses_strength[i] = (Read16(p + 8) & 1u) ? 0 : 1;
        auto* q = p + 22;
        if (!SkipCString(q, end)) {
            break;
        }
        while (q < end && *q == 0) {
            ++q;
        }
        if (q < end && *q == 3) {
            ++q;
            unsigned n = 0;
            while (q < end && *q != 0 && n < 23u) {
                req.skill_name[i][n++] = static_cast<char>(*q++);
            }
            req.skill_name[i][n] = 0;
            if (q < end && *q == 0) {
                ++q;
            }
        }
        req.skill_count = static_cast<std::int32_t>(i + 1);
        p = q;
        while (p < end && *p == 0) {
            ++p;
        }
    }
}

int g_enemy_loaded_forms[16]{};
unsigned g_enemy_loaded_fired_n = 0;

void ResetEnemyLoadedFired() {
    g_enemy_loaded_fired_n = 0;
    std::memset(g_enemy_loaded_forms, 0, sizeof(g_enemy_loaded_forms));
}

bool EnemyLoadedFormFired(int form_row) {
    if (form_row == 0) {
        return false;
    }
    for (unsigned i = 0; i < g_enemy_loaded_fired_n; ++i) {
        if (g_enemy_loaded_forms[i] == form_row) {
            return true;
        }
    }
    return false;
}

void MarkEnemyLoadedForm(int form_row) {
    if (form_row == 0 || g_enemy_loaded_fired_n >= 16u || EnemyLoadedFormFired(form_row)) {
        return;
    }
    g_enemy_loaded_forms[g_enemy_loaded_fired_n++] = form_row;
}

// actor+0x15A / +0x189 are the 1..15 catalog slot, not the M_DAT species.
// Species is SpeciesMap[slot] at ctx+0x64a07 (Green Slime = 126, not slot 1).
int ReadMappedFormRow(std::uint8_t catalog, std::uint8_t slot_15a) {
    if (catalog == 0) {
        catalog = slot_15a;
    }
    if (catalog == 0 || catalog >= 16u) {
        return slot_15a > 16u ? static_cast<int>(slot_15a) : 0;
    }
    void* ctxp = nullptr;
    if (!grandia_mod::SafeReadPointer(grandia_mod::ModuleBase() + grandia_mod::kBattleCtxPtrRva,
                                     &ctxp) ||
        !ctxp) {
        return 0;
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    if (!grandia_mod::PtrReadable(ctx + 0x64a07u, 16)) {
        return 0;
    }
    return ctx[0x64a07u + catalog];
}

constexpr std::uintptr_t kWindtHeapRva = 0x240E68u;
constexpr std::uintptr_t kWindtFromHeap = 0x30000u;
constexpr unsigned kWindtRecSize = 28;
constexpr std::uintptr_t kWindtSec3AliasRvas[] = {
    0x300240u, 0x3015A0u, 0x302558u, 0x30B364u, 0x308E24u, 0x307FD8u,
};

struct ShopPriceRestore {
    std::uint16_t id;
    std::uint16_t cost;
    std::uint16_t override_cost;
};
ShopPriceRestore g_shop_price_restore[64]{};
unsigned g_shop_price_restore_n = 0;
std::uint16_t g_sell_gold[512]{};
std::uint8_t g_sell_set[512]{};

bool LooksLikeSec3(const std::uint8_t* sec3) {
    if (!sec3 || !grandia_mod::PtrReadable(sec3, kWindtRecSize)) {
        return false;
    }
    return Read16(sec3) == 1;
}

void AddSec3(std::uint8_t** out, unsigned* n, unsigned cap, std::uint8_t* p) {
    if (!LooksLikeSec3(p) || !out || !n) {
        return;
    }
    for (unsigned i = 0; i < *n; ++i) {
        if (out[i] == p) {
            return;
        }
    }
    if (*n < cap) {
        out[(*n)++] = p;
    }
}

unsigned CollectSec3(std::uint8_t** out, unsigned cap) {
    unsigned n = 0;
    void* heap = nullptr;
    if (grandia_mod::SafeReadPointer(grandia_mod::ModuleBase() + kWindtHeapRva, &heap) && heap) {
        auto* windt = static_cast<std::uint8_t*>(heap) + kWindtFromHeap;
        if (grandia_mod::PtrReadable(windt, 16)) {
            const auto off = *reinterpret_cast<std::uint32_t*>(windt + 12);
            if (off >= 16 && off < 0x40000) {
                AddSec3(out, &n, cap, windt + off);
            }
        }
    }
    for (auto rva : kWindtSec3AliasRvas) {
        void* p = nullptr;
        if (grandia_mod::SafeReadPointer(grandia_mod::ModuleBase() + rva, &p) && p) {
            AddSec3(out, &n, cap, static_cast<std::uint8_t*>(p));
        }
    }
    return n;
}

int ReadWindtCost(int item_id) {
    if (item_id < 1 || item_id > 511) {
        return 0;
    }
    std::uint8_t* secs[8]{};
    const unsigned n = CollectSec3(secs, 8);
    if (n == 0) {
        return 0;
    }
    return Read16(secs[0] + static_cast<unsigned>(item_id - 1) * kWindtRecSize + 4);
}

void WriteWindtCost(int item_id, int gold) {
    if (item_id < 1 || item_id > 511) {
        return;
    }
    if (gold < 0) {
        gold = 0;
    }
    if (gold > 0xFFFF) {
        gold = 0xFFFF;
    }
    std::uint8_t* secs[8]{};
    const unsigned n = CollectSec3(secs, 8);
    const auto off = static_cast<unsigned>(item_id - 1) * kWindtRecSize + 4;
    for (unsigned i = 0; i < n; ++i) {
        if (grandia_mod::PtrReadable(secs[i] + off, 2)) {
            Write16(secs[i] + off, gold);
        }
    }
}

void RememberShopPrice(int item_id, int old_cost, int want) {
    if (item_id < 1 || item_id > 511 || g_shop_price_restore_n >= 64u) {
        return;
    }
    if (want < 0) {
        want = 0;
    }
    if (want > 0xFFFF) {
        want = 0xFFFF;
    }
    for (unsigned i = 0; i < g_shop_price_restore_n; ++i) {
        if (g_shop_price_restore[i].id == static_cast<std::uint16_t>(item_id)) {
            g_shop_price_restore[i].override_cost = static_cast<std::uint16_t>(want);
            return;
        }
    }
    g_shop_price_restore[g_shop_price_restore_n].id = static_cast<std::uint16_t>(item_id);
    g_shop_price_restore[g_shop_price_restore_n].cost =
        static_cast<std::uint16_t>(old_cost < 0 ? 0 : old_cost > 0xFFFF ? 0xFFFF : old_cost);
    g_shop_price_restore[g_shop_price_restore_n].override_cost = static_cast<std::uint16_t>(want);
    ++g_shop_price_restore_n;
}

void ClearSellOverrides() {
    std::memset(g_sell_gold, 0, sizeof(g_sell_gold));
    std::memset(g_sell_set, 0, sizeof(g_sell_set));
}

void ApplySellOverrides(const grandia_mod::ShopOpenNative& req) {
    const auto n = req.sell_count < 0 ? 0 : req.sell_count > 64 ? 64 : req.sell_count;
    for (int i = 0; i < n; ++i) {
        const int id = req.sell_item[i];
        if (id < 1 || id > 511) {
            continue;
        }
        auto gold = req.sell_gold[i];
        if (gold < 0) {
            gold = 0;
        }
        if (gold > 0xFFFF) {
            gold = 0xFFFF;
        }
        g_sell_set[id] = 1;
        g_sell_gold[id] = static_cast<std::uint16_t>(gold);
    }
}

void RestoreShopPricesInternal() {
    for (unsigned i = 0; i < g_shop_price_restore_n; ++i) {
        WriteWindtCost(g_shop_price_restore[i].id, g_shop_price_restore[i].cost);
    }
    g_shop_price_restore_n = 0;
    ClearSellOverrides();
}

void ReapplyShopSessionPricesInternal() {
    for (unsigned i = 0; i < g_shop_price_restore_n; ++i) {
        WriteWindtCost(g_shop_price_restore[i].id, g_shop_price_restore[i].override_cost);
    }
}

}  // namespace

void grandia_mod::RestoreShopPriceOverrides() {
    RestoreShopPricesInternal();
}

void grandia_mod::ReapplyShopSessionPrices() {
    ReapplyShopSessionPricesInternal();
}

extern "C" void ModReapplyShopSessionPrices() {
    grandia_mod::ReapplyShopSessionPrices();
}

extern "C" int ModSellGoldFromRec(void* rec) {
    if (!rec || !grandia_mod::PtrReadable(rec, 6u)) {
        return 1;
    }
    const int id = Read16(static_cast<std::uint8_t*>(rec));
    if (id >= 1 && id <= 511 && g_sell_set[id]) {
        return g_sell_gold[id];
    }
    int catalog = 0;
    if (id >= 1 && id <= 511 && grandia_mod::TryCatalogSellGold(id, &catalog)) {
        return catalog;
    }
    const int cost = Read16(static_cast<std::uint8_t*>(rec) + 4);
    const int half = cost / 2;
    return half > 0 ? half : 1;
}

extern "C" void ModOnSellOpen() {
    grandia_mod::ShopOpenNative req{};
    req.kind = 2;
    void* params = nullptr;
    if (grandia_mod::SafeReadPointer(grandia_mod::ModuleBase() + grandia_mod::kFieldParamsPtrRva,
                                    &params) &&
        params && grandia_mod::PtrReadable(params, 2u)) {
        req.map = Read16(static_cast<std::uint8_t*>(params));
    }
    if (grandia_mod::RuntimeOnShopOpen(&req) != 0) {
        return;
    }
    ApplySellOverrides(req);
}

extern "C" void ModOnShopOpen(int kind) {
    if (kind == 2) {
        return;
    }
    // Do not FireItemCatalog here. OnShopOpen is the start of the shop
    // function — rewriting sec3 first (Rusty Knife → Lump of Coal) makes
    // the buy-list builder walk the mutated row and crash. OnItem runs at
    // WINDT finalize after the list exists. New stock without a catalog
    // snapshot is not gold 0 (ShopOpenEvent SeedPrice / -1 writeback).
    grandia_mod::LogInfo("OnShopOpen kind=%d", kind);
    void* params = nullptr;
    if (!grandia_mod::SafeReadPointer(grandia_mod::ModuleBase() + grandia_mod::kFieldParamsPtrRva,
                                     &params) ||
        !params) {
        return;
    }
    auto* p = static_cast<std::uint8_t*>(params);
    if (!grandia_mod::PtrReadable(p, 0x200u)) {
        return;
    }
    grandia_mod::ShopOpenNative req{};
    req.map = Read16(p);
    req.kind = kind;
    for (unsigned i = 0; i < grandia_mod::kShopPages * grandia_mod::kShopSlots; ++i) {
        req.items[i] = Read16(p + grandia_mod::kShopStockOff + i * 2u);
        req.prices[i] = req.items[i] > 0 ? ReadWindtCost(req.items[i]) : 0;
    }
    if (grandia_mod::RuntimeOnShopOpen(&req) != 0) {
        return;
    }
    for (unsigned i = 0; i < grandia_mod::kShopPages * grandia_mod::kShopSlots; ++i) {
        auto id = req.items[i];
        if (id < 0 || id > 511) {
            id = 0;
        }
        Write16(p + grandia_mod::kShopStockOff + i * 2u, id);
        if (id <= 0) {
            continue;
        }
        const int want = req.prices[i];
        if (want < 0) {
            continue;
        }
        const int now = ReadWindtCost(id);
        RememberShopPrice(id, now, want);
        WriteWindtCost(id, want);
    }
    ApplySellOverrides(req);
}

extern "C" void ModOnEnemyLoaded(void* actor) {
    grandia_mod::RaiseMagicCatalogOnSpawn();
    if (!actor || !grandia_mod::PtrReadable(actor, 0x190u)) {
        return;
    }
    auto* a = static_cast<std::uint8_t*>(actor);
    if (a[2] != 7) {
        return;
    }
    std::uint8_t catalog = a[0x189];
    if (catalog == 0) {
        catalog = a[0x25];
    }
    const int form_row = ReadMappedFormRow(catalog, a[0x15A]);
    if (EnemyLoadedFormFired(form_row)) {
        return;
    }
    grandia_mod::EnemyLoadedNative req{};
    req.actor_id = a[4];
    req.catalog = catalog;
    req.form_row = form_row;
    req.level = a[0x10E];
    req.hp = Read16(a + 0x100);
    req.max_hp = Read16(a + 0x102);
    if (req.hp == 0 && req.max_hp > 0) {
        req.hp = req.max_hp;
    }
    req.str = Read16(a + 0x106);
    req.vit = Read16(a + 0x108);
    req.wit = Read16(a + 0x10A);
    req.agi = Read16(a + 0x10C);
    req.exp = Read16(a + 0x17E);
    req.gold = Read16(a + 0x180);
    req.attack_count = a[0x18C];
    req.attack_range = a[0x18D];
    req.drop_item[0] = Read16(a + 0x182);
    req.drop_item[1] = Read16(a + 0x184);
    req.drop_rate[0] = a[0x187];
    req.drop_rate[1] = a[0x188];
    req.fire_resist = a[0x13E] >> 4;
    req.water_resist = a[0x13E] & 0xF;
    req.wind_resist = a[0x13F] >> 4;
    req.earth_resist = a[0x13F] & 0xF;
    std::uint32_t model_raw = 0;
    std::memcpy(&model_raw, a + 0x9C, 4);
    std::uint8_t* skill_hdrs[8]{};
    std::uint8_t* model = nullptr;
    if (model_raw >= 0x10000u &&
        grandia_mod::PtrReadable(reinterpret_cast<void*>(static_cast<std::uintptr_t>(model_raw)), 0x10u)) {
        model = reinterpret_cast<std::uint8_t*>(static_cast<std::uintptr_t>(model_raw));
        if (grandia_mod::PtrReadable(model, 0x180u)) {
            ParseEnemySkills(model, 0x180u, req, skill_hdrs);
        }
    }
    if (req.hp == 0 && req.max_hp == 0 && req.str == 0 && req.vit == 0 && req.agi == 0) {
        static void* waited[16]{};
        static unsigned waited_n = 0;
        for (unsigned i = 0; i < waited_n; ++i) {
            if (waited[i] == actor) {
                return;
            }
        }
        if (waited_n < 16u) {
            waited[waited_n++] = actor;
        }
        return;
    }
    grandia_mod::LogInfo("EnemyLoaded cat=%d form=%d lv=%d hp=%d/%d", req.catalog, req.form_row,
                         req.level, req.hp, req.max_hp);
    if (grandia_mod::RuntimeOnEnemyLoaded(&req) != 0) {
        return;
    }
    MarkEnemyLoadedForm(form_row);
    const auto level = static_cast<std::uint8_t>(req.level < 0 ? 0 : req.level > 255 ? 255 : req.level);
    a[0x10E] = level;
    Write16(a + 0x100, req.hp);
    Write16(a + 0x102, req.max_hp);
    Write16(a + 0x104, req.max_hp);
    Write16(a + 0x106, req.str);
    Write16(a + 0x108, req.vit);
    Write16(a + 0x10A, req.wit);
    Write16(a + 0x10C, req.agi);
    Write16(a + 0x17E, req.exp);
    Write16(a + 0x180, req.gold);
    a[0x18C] = ClampU8(req.attack_count);
    a[0x18D] = ClampU8(req.attack_range);
    Write16(a + 0x182, req.drop_item[0]);
    Write16(a + 0x184, req.drop_item[1]);
    a[0x187] = ClampU8(req.drop_rate[0]);
    a[0x188] = ClampU8(req.drop_rate[1]);
    a[0x13E] = static_cast<std::uint8_t>((ClampNibble(req.fire_resist) << 4) | ClampNibble(req.water_resist));
    a[0x13F] = static_cast<std::uint8_t>((ClampNibble(req.wind_resist) << 4) | ClampNibble(req.earth_resist));
    if (req.hp > req.max_hp && req.max_hp > 0) {
        Write16(a + 0x100, req.max_hp);
    }
    if (model && grandia_mod::PtrReadable(model, 0x12u)) {
        model[1] = level;
        Write16(model + 2, req.max_hp);
        Write16(model + 4, req.str);
        Write16(model + 6, req.vit);
        Write16(model + 8, req.wit);
        Write16(model + 0xA, req.agi);
        Write16(model + 0xE, req.exp);
        Write16(model + 0x10, req.gold);
        if (grandia_mod::PtrReadable(model, 0x33u)) {
            Write16(model + 0x14, req.drop_item[0]);
            Write16(model + 0x16, req.drop_item[1]);
            model[0x18] = ClampU8(req.drop_rate[0]);
            model[0x19] = ClampU8(req.drop_rate[1]);
            model[0x22] = ClampU8(req.attack_count);
            model[0x23] = ClampU8(req.attack_range);
            model[0x2C] = a[0x13E];
            model[0x2D] = a[0x13F];
        }
        const int write_skills = req.skill_count < 0 ? 0 : req.skill_count > 8 ? 8 : req.skill_count;
        for (int i = 0; i < write_skills; ++i) {
            auto* hdr = skill_hdrs[i];
            if (!hdr || !grandia_mod::PtrReadable(hdr, 22u)) {
                continue;
            }
            WriteI16(hdr + 6, req.skill_power[i]);
            hdr[0xA] = ClampU8(req.skill_element[i]);
            hdr[0xD] = ClampU8(req.skill_effect[i]);
            hdr[0xE] = ClampU8(req.skill_mode[i]);
            hdr[0xF] = ClampU8(req.skill_add[i]);
            hdr[0x10] = ClampU8(req.skill_chance[i]);
            hdr[0x11] = ClampU8(req.skill_add_level[i]);
            hdr[0x13] = ClampU8(req.skill_speed[i]);
            auto flags = Read16(hdr + 8);
            if (req.skill_uses_strength[i]) {
                flags = static_cast<std::uint16_t>(flags & ~1u);
            } else {
                flags = static_cast<std::uint16_t>(flags | 1u);
            }
            Write16(hdr + 8, flags);
        }
    }
}

extern "C" void ModAfterEnemyModelCopy(void* actor) {
    (void)actor;
    grandia_mod::LinkAttachedEnemies(true);
}

extern "C" void* ModAttachFixParent(void* ebx) {
    grandia_mod::LinkAttachedEnemies(true);
    if (ebx && grandia_mod::PtrReadable(ebx, 0x70u)) {
        return ebx;
    }
    if (grandia_mod::g_attach_parent_ptr &&
        grandia_mod::PtrReadable(grandia_mod::g_attach_parent_ptr, 0x70u)) {
        return grandia_mod::g_attach_parent_ptr;
    }
    return ebx;
}

extern "C" unsigned ModBattleAllyResolveCount(unsigned formation_index) {
    grandia_mod::LinkAttachedEnemies(true);
    grandia_mod::ObserveBattleMode();
    if (grandia_mod::g_staged) {
        grandia_mod::g_saw_fight_spawn = true;
        return static_cast<unsigned>(grandia_mod::CountIds(grandia_mod::g_override_ids));
    }
    return grandia_mod::StockAllyCount(formation_index);
}

extern "C" int ModBattleAllyShouldForceSpawn() {
    return grandia_mod::g_staged ? 1 : 0;
}

extern "C" unsigned ModBattleAllyResolveCharId(unsigned slot0, unsigned table_index,
                                              unsigned table_base) {
    if (grandia_mod::g_staged && slot0 < 4u) {
        return grandia_mod::g_override_ids[slot0];
    }
    if (table_base == 0) {
        return 0;
    }
    std::uint8_t id = 0;
    if (!grandia_mod::SafeReadByte(table_base + table_index, &id)) {
        return 0;
    }
    return id;
}

extern "C" unsigned ModBattleAllyResolveModelSlot(unsigned spawn_slot0, unsigned char_id) {
    if (grandia_mod::g_staged) {
        grandia_mod::TrySplicePdatPlayables(grandia_mod::g_override_ids,
                                           static_cast<unsigned>(grandia_mod::g_override_count),
                                           "ally-spawn");
    }
    (void)char_id;
    return spawn_slot0 < 4u ? spawn_slot0 : 0u;
}

extern "C" unsigned ModPartyResolveCount(unsigned group_index) {
    if (grandia_mod::BattleOverrideActive()) {
        return static_cast<unsigned>(grandia_mod::g_override_count);
    }
    const auto base = grandia_mod::ModuleBase();
    if (base == 0 || group_index > 0x10u) {
        return 0;
    }
    const auto* row =
        reinterpret_cast<const std::uint8_t*>(base + grandia_mod::kPresetTableRva + group_index * 5u);
    return row[0];
}

extern "C" unsigned ModPartyResolveCharId(unsigned slot) {
    if (!grandia_mod::BattleOverrideActive()) {
        return 0xFFu;
    }
    if (slot >= static_cast<unsigned>(grandia_mod::g_override_count) || slot >= 4u) {
        return 0;
    }
    return grandia_mod::g_override_ids[slot];
}

extern "C" int ModFillUiPartyCache(unsigned which) {
    std::uint8_t seed[4]{};
    if (grandia_mod::g_field0a_saved) {
        std::memcpy(seed, grandia_mod::g_saved_field0a, 4);
    } else if (!grandia_mod::ReadFieldParty(seed)) {
        return 0;
    }

    grandia_mod::MenuOpenNative req{};
    req.which = static_cast<std::int32_t>(which);
    std::memcpy(req.party, seed, 4);
    if (grandia_mod::RuntimeOnMenuOpen(&req) != 0) {
        return 0;
    }

    std::uint8_t want[4]{};
    std::memcpy(want, req.party, 4);
    const int n = grandia_mod::PackPartyIds(want);
    if (n == 0) {
        return 0;
    }

    std::uint8_t live[4]{};
    const bool live_ok = grandia_mod::ReadFieldParty(live);
    const bool mods_changed = !grandia_mod::SameParty(want, seed);
    const bool hide_staged =
        grandia_mod::g_field0a_saved && (!live_ok || !grandia_mod::SameParty(live, seed));
    if (!mods_changed && !hide_staged) {
        return 0;
    }

    std::uintptr_t cache = grandia_mod::kMenuPartyCacheRva;
    if (which == 1) {
        cache = grandia_mod::kStashPartyCacheRva;
    } else if (which == 2) {
        cache = grandia_mod::kItemPartyCacheRva;
    }
    grandia_mod::WriteUiPartyCache(cache, want, n);
    if (auto* map_obj = grandia_mod::MapObject()) {
        grandia_mod::SeedMissingCharacterBlocks(map_obj, want, n);
    }
    return 1;
}

extern "C" void ModRunUiFaces(unsigned which) {
    std::uintptr_t cache = grandia_mod::kMenuPartyCacheRva;
    std::uintptr_t handles = grandia_mod::kStatusFaceHandleRva;
    if (which == 1) {
        cache = grandia_mod::kStashPartyCacheRva;
        handles = grandia_mod::kStashFaceHandleRva;
    } else if (which == 2) {
        cache = grandia_mod::kItemPartyCacheRva;
        handles = grandia_mod::kItemFaceHandleRva;
    }
    grandia_mod::RunUiFaces(cache, handles);
}

extern "C" void ModOnBattlePartyInit() {
    grandia_mod::LinkAttachedEnemies(true);
    grandia_mod::ObserveBattleMode();
    if (!grandia_mod::g_staged && !grandia_mod::g_override_on) {
        return;
    }
    std::uint8_t mode = 0;
    if (!grandia_mod::SafeReadByte(grandia_mod::ModuleBase() + grandia_mod::kBattleModeRva, &mode) ||
        !grandia_mod::IsBattleMode(mode)) {
        return;
    }
    if (grandia_mod::g_staged) {
        grandia_mod::SetBattleCullPatches(true);
    }
}

extern "C" void* ModBattleModelBindResolve(void* header) {
    if (grandia_mod::g_staged && !grandia_mod::PdatPackRebuilt()) {
        return grandia_mod::LeaderModelHeader();
    }
    if (grandia_mod::ModelHeaderReadable(header)) {
        return header;
    }
    return grandia_mod::LeaderModelHeader();
}

extern "C" int ModBattleAnimBindResolve(unsigned bank) {
    if (grandia_mod::PdatPackRebuilt()) {
        return static_cast<int>(bank);
    }
    void* ctxp = nullptr;
    if (!grandia_mod::SafeReadPointer(grandia_mod::ModuleBase() + grandia_mod::kBattleCtxPtrRva, &ctxp) ||
        !ctxp) {
        return static_cast<int>(bank);
    }
    auto* ctx = static_cast<std::uint8_t*>(ctxp);
    auto bank_ok = [&](unsigned b) -> bool {
        if (b > 0x40u) {
            return false;
        }
        auto* slot = reinterpret_cast<std::uint32_t*>(ctx + static_cast<std::uintptr_t>(b) * 36u + 0xa87cu);
        if (!grandia_mod::PtrReadable(slot, 4)) {
            return false;
        }
        const std::uint32_t ptr = *slot;
        return ptr >= 0x10000u && grandia_mod::PtrReadable(reinterpret_cast<void*>(ptr), 2);
    };
    if (bank_ok(bank)) {
        return static_cast<int>(bank);
    }
    if (bank_ok(5u)) {
        return 5;
    }
    return -1;
}

namespace grandia_mod {

void RunUiFaces(std::uintptr_t cache_rva, std::uintptr_t handle_table_rva) {
    const auto base = ModuleBase();
    auto* cache = reinterpret_cast<std::uint8_t*>(base + cache_rva);
    auto* handles = reinterpret_cast<std::uint16_t*>(base + handle_table_rva);
    void* dest = EnsureFaceDest();
    if (!dest) {
        return;
    }
    void* face_fn = reinterpret_cast<void*>(base + kFaceLoadRva);
    for (unsigned slot = 0; slot < 4u; ++slot) {
        const std::uint8_t char_id = cache[slot];
        if (char_id == 0 || char_id > 8) {
            continue;
        }
        std::uint8_t fc_row = 0;
        std::uint8_t face_i = 0;
        if (!PickFace(char_id, &fc_row, &face_i)) {
            continue;
        }
        std::uint8_t* bank = EnsureFcBank(fc_row);
        if (!bank) {
            continue;
        }
        g_mod_face_bank_override = bank;
        const unsigned handle = slot | 0x8000u;
        CallFaceLoad(face_fn, handle, face_i, dest);
        g_mod_face_bank_override = nullptr;
        handles[char_id] = static_cast<std::uint16_t>(handle);
    }
}

void OnBattleSetup() {
    BattleLoadNative req{};
    FillBattleLoadIdentity(&req);
    if (req.enc_row == 0) {
        return;
    }

    const std::uint8_t table0 = req.encounter[1];
    const std::uint8_t approach0 = req.encounter[11];
    const std::uint8_t count0 = req.encounter[6];
    std::uint8_t slots0[kBattleEncounterDump - kBattleEncounterHeader]{};
    std::memcpy(slots0, req.encounter + kBattleEncounterHeader, sizeof(slots0));

    if (RuntimeOnBattleSetup(&req) != 0) {
        return;
    }

    const bool write_table = req.encounter[1] != table0;
    const bool write_approach = req.encounter[11] != approach0;
    const bool write_count = req.encounter[6] != count0;
    const bool write_slot =
        std::memcmp(slots0, req.encounter + kBattleEncounterHeader, sizeof(slots0)) != 0;
    if (write_table || write_approach || write_count || write_slot) {
        WriteBattleSetupEncounter(&req, write_table, write_approach, write_count, write_slot);
    }
}

void OnBattleLoad() {
    ClearAttachParent();
    grandia_mod::RaiseMagicCatalog();
    std::uint8_t live[4]{};
    if (!ReadFieldParty(live)) {
        LogWarn("OnBattleLoad: MapObj not ready");
        return;
    }

    std::uint8_t true_field[4]{};
    if (g_field0a_saved) {
        std::memcpy(true_field, g_saved_field0a, 4);
    } else {
        std::memcpy(true_field, live, 4);
    }

    BattleLoadNative req{};
    if (g_seed_on) {
        std::memcpy(req.party, g_seed_ids, 4);
    } else {
        std::memcpy(req.party, true_field, 4);
    }
    FillBattleLoadIdentity(&req);
    std::uint8_t slots_orig[kBattleEncounterDump - kBattleEncounterHeader]{};
    std::memcpy(slots_orig, req.encounter + kBattleEncounterHeader, sizeof(slots_orig));
    const std::uint8_t count0 = req.encounter[6];
    std::uint8_t spec_orig[16]{};
    std::memcpy(spec_orig, req.species, 16);

    if (RuntimeOnBattleLoad(&req) != 0) {
        return;
    }
    if (std::memcmp(slots_orig, req.encounter + kBattleEncounterHeader, sizeof(slots_orig)) != 0 ||
        req.encounter[6] != count0) {
        WriteBattleLoadEncounter(&req);
    }
    if (std::memcmp(spec_orig, req.species, 16) != 0) {
        WriteBattleSpeciesMap(&req);
    }

    std::uint8_t want[4]{};
    std::memcpy(want, req.party, 4);
    PackPartyIds(want);

    if (SameParty(want, true_field) || !g_spawn_hooks_ok) {
        if (g_field0a_saved && !SameParty(live, g_saved_field0a)) {
            CommitFieldPartyRestore();
        } else {
            g_staged = false;
            g_field0a_saved = false;
            SetBattleCullPatches(false);
            ResetPdatBattlePack();
            ClearFightOverride();
        }
        g_mod_party_staged = 0;
        return;
    }

    if (!g_staged) {
        ResetPdatBattlePack();
    }
    if (!g_field0a_saved) {
        SnapshotFieldParty();
    }
    const int n = CountIds(want);
    auto* map_obj = MapObject();
    SeedMissingCharacterBlocks(map_obj, want, n);
    if (!WriteFieldParty(want)) {
        LogWarn("OnBattleLoad: failed to stage MapObj+0A");
        return;
    }
    std::memcpy(g_override_ids, want, 4);
    g_override_count = n;
    g_override_on = n > 0;
    g_staged = true;
    g_mod_party_staged = 1;
    g_saw_fight_spawn = false;
    PatchFormationTableForBattle();
    SetBattleCullPatches(true);
}

void TryRestoreFieldParty(bool field_map_fopen) {
    PollPartyRestore(field_map_fopen);
}

void RestoreFieldPartyForSave() {
    if (!g_field0a_saved) {
        return;
    }
    WriteFieldParty(g_saved_field0a);
}

}  // namespace grandia_mod

extern "C" void ModOnBattleSetup() {
    grandia_mod::OnBattleSetup();
}

extern "C" void ModOnBattleLoad() {
    ResetEnemyLoadedFired();
    grandia_mod::OnBattleLoad();
}

extern "C" int ModPartyGet(int slot) {
    if (slot < 0 || slot > 3) {
        return -1;
    }
    std::uint8_t ids[4]{};
    if (!grandia_mod::ReadFieldParty(ids)) {
        return -1;
    }
    return ids[slot];
}

extern "C" int ModPartySetIds(int a, int b, int c, int d) {
    const int raw[4] = {a, b, c, d};
    std::uint8_t out[4]{};
    bool seen_zero = false;
    int n = 0;
    for (int i = 0; i < 4; ++i) {
        if (raw[i] == 0) {
            seen_zero = true;
            continue;
        }
        if (seen_zero || raw[i] < grandia_mod::kMinCharId || raw[i] > grandia_mod::kMaxCharId) {
            grandia_mod::LogWarn("Party.SetIds invalid roster %d,%d,%d,%d", a, b, c, d);
            return 0;
        }
        out[i] = static_cast<std::uint8_t>(raw[i]);
        ++n;
    }
    std::memcpy(grandia_mod::g_seed_ids, out, 4);
    grandia_mod::g_seed_count = n;
    grandia_mod::g_seed_on = n > 0;
    if (n == 0) {
        grandia_mod::LogInfo("Party.SetIds cleared override");
    } else {
        grandia_mod::LogInfo("Party.SetIds battle seed %u,%u,%u,%u", out[0], out[1], out[2],
                             out[3]);
    }
    return 1;
}

#if defined(_M_IX86)

extern "C" __declspec(naked) void ModBattleSetupDetour() {
    __asm {
        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnBattleSetup
        mov esp, dword ptr [esp]
        popad
        jmp dword ptr [g_mod_battle_setup_tramp]
    }
}

extern "C" __declspec(naked) void ModBattleLoadDetour() {
    __asm {
        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnBattleLoad
        mov esp, dword ptr [esp]
        popad
        jmp dword ptr [g_mod_battle_load_tramp]
    }
}

extern "C" __declspec(naked) void ModBattleAllyCountDetour() {
    __asm {
        push ecx
        push edx
        push ecx
        call ModBattleAllyResolveCount
        add esp, 4
        pop edx
        pop ecx
        jmp dword ptr [g_mod_ally_count_resume]
    }
}

extern "C" __declspec(naked) void ModEnemyPostBindDetour() {
    __asm {
        pushad
        call ModOnEnemyPostBind
        popad
        mov cl, byte ptr [eax + 0x92]
        jmp dword ptr [g_mod_post_bind_resume]
    }
}

extern "C" __declspec(naked) void ModEnemyLoadedDetour() {
    __asm {
        push dword ptr [esp + 4]
        call dword ptr [g_mod_enemy_loaded_tramp]
        add esp, 4
        push dword ptr [esp + 4]
        call ModOnEnemyLoaded
        add esp, 4
        ret
    }
}

extern "C" __declspec(naked) void ModEnemyLoadedAltDetour() {
    __asm {
        push dword ptr [esp + 4]
        call dword ptr [g_mod_enemy_loaded_alt_tramp]
        add esp, 4
        push dword ptr [esp + 4]
        call ModOnEnemyLoaded
        add esp, 4
        ret
    }
}

extern "C" __declspec(naked) void ModShopOpenDetour() {
    __asm {
        pushad
        movzx eax, dl
        push eax
        call ModOnShopOpen
        add esp, 4
        popad
        jmp dword ptr [g_mod_shop_open_tramp]
    }
}

extern "C" __declspec(naked) void ModShopSellOpenDetour() {
    __asm {
        pushad
        call ModOnSellOpen
        popad
        jmp dword ptr [g_mod_shop_sell_open_tramp]
    }
}

extern "C" __declspec(naked) void ModShopSellListPriceDetour() {
    __asm {
        push edx
        push ecx
        push eax
        call ModSellGoldFromRec
        add esp, 4
        pop ecx
        pop edx
        jmp dword ptr [g_mod_shop_sell_list_resume]
    }
}

extern "C" __declspec(naked) void ModShopSellSelectPriceDetour() {
    __asm {
        push eax
        call ModSellGoldFromRec
        add esp, 4
        mov ecx, eax
        jmp dword ptr [g_mod_shop_sell_sel_resume]
    }
}

extern "C" __declspec(naked) void ModEnemyModelCopyDetour() {
    __asm {
        push ecx
        call dword ptr [g_mod_enemy_model_copy_tramp]
        pop ecx
        pushad
        push ecx
        call ModAfterEnemyModelCopy
        add esp, 4
        popad
        ret
    }
}

extern "C" __declspec(naked) void ModUtf8DecDetour() {
    __asm {
        test ecx, ecx
        jnz go
        push dword ptr [esp]
        call ModOnUtf8Null
        add esp, 4
        xor eax, eax
        ret
    go:
        jmp dword ptr [g_mod_utf8_dec_tramp]
    }
}

extern "C" __declspec(naked) void ModSkillNameInternDetour() {
    __asm {
        push ecx
        call dword ptr [g_mod_skill_name_tramp]
        test eax, eax
        jnz intern_ok
        pop eax
        ret
    intern_ok:
        add esp, 4
        ret
    }
}

extern "C" __declspec(naked) void ModSkillScriptParseDetour() {
    __asm {
        cmp edx, 0x10000
        jb skip
        jmp dword ptr [g_mod_skill_parse_tramp]
    skip:
        push dword ptr [esp]
        call ModOnSkillScriptNull
        add esp, 4
        xor eax, eax
        ret
    }
}

extern "C" __declspec(naked) void ModAttachParentDetour() {
    __asm {
        push ecx
        push edx
        push ebx
        call ModAttachFixParent
        add esp, 4
        mov ebx, eax
        pop edx
        pop ecx
        cmp byte ptr [edi + 0x15B], 4
        jmp dword ptr [g_mod_attach_resume]
    }
}

extern "C" __declspec(naked) void ModBattleAllySpawnGateDetour() {
    __asm {
        pushad
        call ModBattleAllyShouldForceSpawn
        test eax, eax
        popad
        jnz force_spawn
        lea eax, [ebx - 1]
        cmp eax, 3
        jmp dword ptr [g_mod_ally_gate_resume]
    force_spawn:
        jmp dword ptr [g_mod_ally_spawn]
    }
}

extern "C" __declspec(naked) void ModBattleAllyCharIdDetour() {
    __asm {
        push edx
        push eax
        push ecx
        push edx
        call ModBattleAllyResolveCharId
        add esp, 12
        mov byte ptr [esi + 0x10F], al
        push eax
        push dword ptr [esp + 4]
        call ModBattleAllyResolveModelSlot
        add esp, 8
        mov edx, eax
        add esp, 4
        jmp dword ptr [g_mod_ally_char_resume]
    }
}

extern "C" __declspec(naked) void ModPartyCountDetour() {
    __asm {
        push ecx
        push edx
        push eax
        call ModPartyResolveCount
        add esp, 4
        pop edx
        pop ecx
        jmp dword ptr [g_mod_party_count_resume]
    }
}

extern "C" __declspec(naked) void ModPartyCharIdDetour() {
    __asm {
        push eax
        push ecx
        push edx
        mov eax, dword ptr [ebp - 4]
        push eax
        call ModPartyResolveCharId
        add esp, 4
        cmp al, 0FFh
        je use_original
        mov bl, al
        pop edx
        pop ecx
        pop eax
        jmp dword ptr [g_mod_party_char_resume]
    use_original:
        pop edx
        pop ecx
        pop eax
        push eax
        mov eax, dword ptr [g_mod_party_char_table_abs]
        add eax, edx
        add eax, ecx
        mov bl, byte ptr [eax]
        pop eax
        jmp dword ptr [g_mod_party_char_resume]
    }
}

extern "C" __declspec(naked) void ModBattlePartyInitDetour() {
    __asm {
        pushad
        call ModOnBattlePartyInit
        popad
        jmp dword ptr [g_mod_battle_init_tramp]
    }
}

extern "C" __declspec(naked) void ModBattleModelBindDetour() {
    __asm {
        jmp dword ptr [g_mod_model_bind_tramp]
    }
}

extern "C" __declspec(naked) void ModBattleAnimBindDetour() {
    __asm {
        jmp dword ptr [g_mod_anim_bind_tramp]
    }
}

extern "C" __declspec(naked) void ModMenuFillDetour() {
    __asm {
        pushad
        push 0
        call ModFillUiPartyCache
        add esp, 4
        test eax, eax
        popad
        jnz filled
        jmp dword ptr [g_mod_menu_fill_tramp]
    filled:
        add esp, 4
        pushad
        push 0
        call ModRunUiFaces
        add esp, 4
        popad
        jmp dword ptr [g_mod_menu_fill_skip]
    }
}

extern "C" __declspec(naked) void ModStashFillDetour() {
    __asm {
        pushad
        push 1
        call ModFillUiPartyCache
        add esp, 4
        test eax, eax
        popad
        jnz filled
        jmp dword ptr [g_mod_stash_fill_tramp]
    filled:
        add esp, 4
        pushad
        push 1
        call ModRunUiFaces
        add esp, 4
        popad
        jmp dword ptr [g_mod_stash_fill_skip]
    }
}

extern "C" __declspec(naked) void ModItemFillDetour() {
    __asm {
        pushad
        push 2
        call ModFillUiPartyCache
        add esp, 4
        test eax, eax
        popad
        jnz filled
        jmp dword ptr [g_mod_item_fill_tramp]
    filled:
        add esp, 4
        pushad
        push 2
        call ModRunUiFaces
        add esp, 4
        popad
        jmp dword ptr [g_mod_item_fill_skip]
    }
}

extern "C" __declspec(naked) void ModFaceBankLoadDetour() {
    __asm {
        push eax
        mov esi, dword ptr [g_mod_face_bank_override]
        test esi, esi
        jnz done
        mov eax, dword ptr [g_mod_face_bank_slot_abs]
        mov esi, dword ptr [eax]
    done:
        pop eax
        jmp dword ptr [g_mod_face_bank_resume]
    }
}

extern "C" __declspec(naked) void ModEmptyEquipReturnDetour() {
    __asm {
        mov eax, dword ptr [esp + 0x14]
        test cx, cx
        jnz swap
        pushad
        push ebx
        push eax
        call ModOnEmptyEquipReturn
        add esp, 8
        popad
        jmp dword ptr [g_mod_empty_equip_resume]
    swap:
        mov word ptr [eax], cx
        jmp dword ptr [g_mod_empty_equip_resume]
    }
}

#endif

namespace grandia_mod {
namespace {

bool InstallTrampJump(void* site, std::size_t size, void* detour, void** tramp_mem, void** tramp_slot,
                      std::uint8_t* original, void** site_out, const char* name) {
    *tramp_mem = MakeTrampoline(site, size, static_cast<std::uint8_t*>(site) + size);
    if (!*tramp_mem) {
        LogWarn("Party %s trampoline alloc failed", name);
        return false;
    }
    *tramp_slot = *tramp_mem;
    if (!WriteJump(site, detour, original, size)) {
        VirtualFree(*tramp_mem, 0, MEM_RELEASE);
        *tramp_mem = nullptr;
        *tramp_slot = nullptr;
        LogWarn("Party %s hook failed", name);
        return false;
    }
    *site_out = site;
    return true;
}

}  // namespace

bool InstallPartyHooks() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }

    auto* load_site = reinterpret_cast<std::uint8_t*>(base + kBattleLoadRva);
    if (!IsExecutableAddress(load_site) || load_site[0] != 0x55 || load_site[1] != 0x8B ||
        load_site[2] != 0xEC) {
        LogWarn("OnBattleLoad bytes mismatch at +0x%X", static_cast<unsigned>(kBattleLoadRva));
        return false;
    }
    if (!InstallTrampJump(load_site, kBattleLoadPatchSize, reinterpret_cast<void*>(&ModBattleLoadDetour),
                          &g_battle_load_trampoline_mem, &g_mod_battle_load_tramp, g_battle_load_original,
                          &g_battle_load_site, "OnBattleLoad")) {
        return false;
    }
    LogInfo("OnBattleLoad hook at grandia.exe+0x%X", static_cast<unsigned>(kBattleLoadRva));

    auto* setup_site = reinterpret_cast<std::uint8_t*>(base + kBattleSetupRva);
    if (!IsExecutableAddress(setup_site) || setup_site[0] != 0x55 || setup_site[1] != 0x8B ||
        setup_site[2] != 0xEC || setup_site[3] != 0x51 || setup_site[4] != 0x53) {
        LogWarn("OnBattleSetup bytes mismatch at +0x%X", static_cast<unsigned>(kBattleSetupRva));
    } else if (!InstallTrampJump(setup_site, kBattleSetupPatchSize,
                                 reinterpret_cast<void*>(&ModBattleSetupDetour),
                                 &g_battle_setup_trampoline_mem, &g_mod_battle_setup_tramp,
                                 g_battle_setup_original, &g_battle_setup_site, "OnBattleSetup")) {
        LogWarn("OnBattleSetup hook failed");
    } else {
        LogInfo("OnBattleSetup hook at grandia.exe+0x%X", static_cast<unsigned>(kBattleSetupRva));
    }

    auto* ally_count_site = reinterpret_cast<std::uint8_t*>(base + kBattleAllyCountRva);
    auto* ally_gate_site = reinterpret_cast<std::uint8_t*>(base + kBattleAllySpawnGateRva);
    auto* ally_char_site = reinterpret_cast<std::uint8_t*>(base + kBattleAllyCharIdRva);
    if (IsExecutableAddress(ally_count_site) && IsExecutableAddress(ally_gate_site) &&
        IsExecutableAddress(ally_char_site) && ally_count_site[0] == 0xA1 && ally_gate_site[0] == 0x8D &&
        ally_gate_site[1] == 0x43 && ally_gate_site[2] == 0xFF && ally_char_site[0] == 0x8A &&
        ally_char_site[1] == 0x04 && ally_char_site[2] == 0x01) {
        g_mod_ally_count_resume = reinterpret_cast<void*>(base + kBattleAllyCountResumeRva);
        g_mod_ally_gate_resume = reinterpret_cast<void*>(base + kBattleAllySpawnGateResumeRva);
        g_mod_ally_spawn = reinterpret_cast<void*>(base + kBattleAllySpawnRva);
        g_mod_ally_char_resume = reinterpret_cast<void*>(base + kBattleAllyCharIdResumeRva);
        if (WriteJump(ally_count_site, reinterpret_cast<void*>(&ModBattleAllyCountDetour),
                      g_ally_count_original, kBattleAllyCountPatchSize) &&
            WriteJump(ally_gate_site, reinterpret_cast<void*>(&ModBattleAllySpawnGateDetour),
                      g_ally_gate_original, kBattleAllySpawnGatePatchSize) &&
            WriteJump(ally_char_site, reinterpret_cast<void*>(&ModBattleAllyCharIdDetour),
                      g_ally_char_original, kBattleAllyCharIdPatchSize)) {
            g_ally_count_site = ally_count_site;
            g_ally_gate_site = ally_gate_site;
            g_ally_char_site = ally_char_site;
            g_spawn_hooks_ok = true;
            LogInfo("Party spawn hooks at +0x%X / +0x%X / +0x%X",
                    static_cast<unsigned>(kBattleAllyCountRva),
                    static_cast<unsigned>(kBattleAllySpawnGateRva),
                    static_cast<unsigned>(kBattleAllyCharIdRva));
        } else {
            LogWarn("Party spawn hooks failed");
        }
    } else {
        LogWarn("Party spawn sites mismatch — OnBattleLoad will not stage MapObj");
    }

    auto* enemy_loaded_site = reinterpret_cast<std::uint8_t*>(base + kEnemyLoadedRva);
    if (IsExecutableAddress(enemy_loaded_site) && enemy_loaded_site[0] == 0x55 &&
        enemy_loaded_site[1] == 0x8B && enemy_loaded_site[2] == 0xEC &&
        enemy_loaded_site[3] == 0x83 && enemy_loaded_site[4] == 0xEC &&
        enemy_loaded_site[5] == 0x0C) {
        if (InstallTrampJump(enemy_loaded_site, kEnemyLoadedPatchSize,
                             reinterpret_cast<void*>(&ModEnemyLoadedDetour),
                             &g_enemy_loaded_trampoline_mem, &g_mod_enemy_loaded_tramp,
                             g_enemy_loaded_original, &g_enemy_loaded_site, "OnEnemyLoaded")) {
            LogInfo("OnEnemyLoaded hook at +0x%X", static_cast<unsigned>(kEnemyLoadedRva));
        }
    } else {
        LogWarn("OnEnemyLoaded site mismatch at +0x%X", static_cast<unsigned>(kEnemyLoadedRva));
    }

    auto* enemy_loaded_alt = reinterpret_cast<std::uint8_t*>(base + kEnemyLoadedAltRva);
    if (IsExecutableAddress(enemy_loaded_alt) && enemy_loaded_alt[0] == 0x55 &&
        enemy_loaded_alt[1] == 0x8B && enemy_loaded_alt[2] == 0xEC &&
        enemy_loaded_alt[3] == 0x83 && enemy_loaded_alt[4] == 0xEC &&
        enemy_loaded_alt[5] == 0x0C) {
        if (InstallTrampJump(enemy_loaded_alt, kEnemyLoadedPatchSize,
                             reinterpret_cast<void*>(&ModEnemyLoadedAltDetour),
                             &g_enemy_loaded_alt_trampoline_mem, &g_mod_enemy_loaded_alt_tramp,
                             g_enemy_loaded_alt_original, &g_enemy_loaded_alt_site,
                             "OnEnemyLoaded-alt")) {
            LogInfo("OnEnemyLoaded alt hook at +0x%X", static_cast<unsigned>(kEnemyLoadedAltRva));
        }
    }

    auto* shop_open_site = reinterpret_cast<std::uint8_t*>(base + kShopOpenRva);
    if (IsExecutableAddress(shop_open_site) && shop_open_site[0] == 0x55 &&
        shop_open_site[1] == 0x8B && shop_open_site[2] == 0xEC && shop_open_site[3] == 0x51 &&
        shop_open_site[4] == 0x53 && shop_open_site[5] == 0x56) {
        if (InstallTrampJump(shop_open_site, kShopOpenPatchSize,
                             reinterpret_cast<void*>(&ModShopOpenDetour),
                             &g_shop_open_trampoline_mem, &g_mod_shop_open_tramp,
                             g_shop_open_original, &g_shop_open_site, "OnShopOpen")) {
            LogInfo("OnShopOpen hook at +0x%X", static_cast<unsigned>(kShopOpenRva));
        }
    } else {
        LogWarn("OnShopOpen site mismatch at +0x%X", static_cast<unsigned>(kShopOpenRva));
    }

    auto* shop_sell_open = reinterpret_cast<std::uint8_t*>(base + kShopSellOpenRva);
    if (IsExecutableAddress(shop_sell_open) && shop_sell_open[0] == 0x53 &&
        shop_sell_open[1] == 0x56 && shop_sell_open[2] == 0x57 && shop_sell_open[3] == 0x83 &&
        shop_sell_open[4] == 0xEC && shop_sell_open[5] == 0x08) {
        if (InstallTrampJump(shop_sell_open, kShopSellOpenPatchSize,
                             reinterpret_cast<void*>(&ModShopSellOpenDetour),
                             &g_shop_sell_open_trampoline_mem, &g_mod_shop_sell_open_tramp,
                             g_shop_sell_open_original, &g_shop_sell_open_site, "OnShopSell")) {
            LogInfo("OnShopSell hook at +0x%X", static_cast<unsigned>(kShopSellOpenRva));
        }
    } else {
        LogWarn("OnShopSell site mismatch at +0x%X", static_cast<unsigned>(kShopSellOpenRva));
    }

    auto* sell_list = reinterpret_cast<std::uint8_t*>(base + kShopSellListPriceRva);
    if (IsExecutableAddress(sell_list) && sell_list[0] == 0x0F && sell_list[1] == 0xB7 &&
        sell_list[2] == 0x40 && sell_list[3] == 0x04 && sell_list[4] == 0xD1 &&
        sell_list[5] == 0xE8) {
        g_mod_shop_sell_list_resume = reinterpret_cast<void*>(base + kShopSellListPriceResumeRva);
        if (WriteJump(sell_list, reinterpret_cast<void*>(&ModShopSellListPriceDetour),
                      g_shop_sell_list_original, kShopSellListPricePatchSize)) {
            g_shop_sell_list_site = sell_list;
            LogInfo("Sell list price hook at +0x%X", static_cast<unsigned>(kShopSellListPriceRva));
        }
    } else {
        LogWarn("Sell list price site mismatch at +0x%X",
                static_cast<unsigned>(kShopSellListPriceRva));
    }

    auto* sell_sel = reinterpret_cast<std::uint8_t*>(base + kShopSellSelectPriceRva);
    if (IsExecutableAddress(sell_sel) && sell_sel[0] == 0x0F && sell_sel[1] == 0xB7 &&
        sell_sel[2] == 0x40 && sell_sel[3] == 0x04) {
        g_mod_shop_sell_sel_resume = reinterpret_cast<void*>(base + kShopSellSelectPriceResumeRva);
        if (WriteJump(sell_sel, reinterpret_cast<void*>(&ModShopSellSelectPriceDetour),
                      g_shop_sell_sel_original, kShopSellSelectPricePatchSize)) {
            g_shop_sell_sel_site = sell_sel;
            LogInfo("Sell select price hook at +0x%X",
                    static_cast<unsigned>(kShopSellSelectPriceRva));
        }
    } else {
        LogWarn("Sell select price site mismatch at +0x%X",
                static_cast<unsigned>(kShopSellSelectPriceRva));
    }

    auto* model_copy_site = reinterpret_cast<std::uint8_t*>(base + kEnemyModelCopyRva);
    if (IsExecutableAddress(model_copy_site) && model_copy_site[0] == 0x55 &&
        model_copy_site[1] == 0x8B && model_copy_site[2] == 0xEC) {
        if (InstallTrampJump(model_copy_site, kEnemyModelCopyPatchSize,
                             reinterpret_cast<void*>(&ModEnemyModelCopyDetour),
                             &g_enemy_model_copy_trampoline_mem, &g_mod_enemy_model_copy_tramp,
                             g_enemy_model_copy_original, &g_enemy_model_copy_site,
                             "enemy-model-copy")) {
            LogInfo("Attach model-copy hook at +0x%X", static_cast<unsigned>(kEnemyModelCopyRva));
        }
    } else {
        LogWarn("Attach model-copy site mismatch at +0x%X",
                static_cast<unsigned>(kEnemyModelCopyRva));
    }

    auto* utf8_site = reinterpret_cast<std::uint8_t*>(base + kUtf8DecRva);
    if (IsExecutableAddress(utf8_site) && utf8_site[0] == 0x55 && utf8_site[1] == 0x8B &&
        utf8_site[2] == 0xEC) {
        if (InstallTrampJump(utf8_site, kUtf8DecPatchSize, reinterpret_cast<void*>(&ModUtf8DecDetour),
                             &g_utf8_dec_trampoline_mem, &g_mod_utf8_dec_tramp, g_utf8_dec_original,
                             &g_utf8_dec_site, "utf8-dec")) {
            LogInfo("UTF-8 decoder null guard at +0x%X", static_cast<unsigned>(kUtf8DecRva));
        }
    } else {
        LogWarn("UTF-8 decoder site mismatch at +0x%X", static_cast<unsigned>(kUtf8DecRva));
    }

    auto* name_site = reinterpret_cast<std::uint8_t*>(base + kSkillNameInternRva);
    if (IsExecutableAddress(name_site) && name_site[0] == 0x55 && name_site[1] == 0x8B &&
        name_site[2] == 0xEC) {
        if (InstallTrampJump(name_site, kSkillNameInternPatchSize,
                             reinterpret_cast<void*>(&ModSkillNameInternDetour),
                             &g_skill_name_trampoline_mem, &g_mod_skill_name_tramp,
                             g_skill_name_original, &g_skill_name_site, "skill-name")) {
            LogInfo("Skill-name intern fallback at +0x%X", static_cast<unsigned>(kSkillNameInternRva));
        }
    } else {
        LogWarn("Skill-name intern site mismatch at +0x%X",
                static_cast<unsigned>(kSkillNameInternRva));
    }

    auto* parse_site = reinterpret_cast<std::uint8_t*>(base + kSkillScriptParseRva);
    if (IsExecutableAddress(parse_site) && parse_site[0] == 0x55 && parse_site[1] == 0x8B &&
        parse_site[2] == 0xEC) {
        if (InstallTrampJump(parse_site, kSkillScriptParsePatchSize,
                             reinterpret_cast<void*>(&ModSkillScriptParseDetour),
                             &g_skill_parse_trampoline_mem, &g_mod_skill_parse_tramp,
                             g_skill_parse_original, &g_skill_parse_site, "skill-parse")) {
            LogInfo("Skill-script null guard at +0x%X", static_cast<unsigned>(kSkillScriptParseRva));
        }
    } else {
        LogWarn("Skill-script parse site mismatch at +0x%X",
                static_cast<unsigned>(kSkillScriptParseRva));
    }

    auto* attach_site = reinterpret_cast<std::uint8_t*>(base + kAttachParentLookupRva);
    if (IsExecutableAddress(attach_site) && attach_site[0] == 0x80 && attach_site[1] == 0xBF &&
        attach_site[2] == 0x5B && attach_site[3] == 0x01 && attach_site[6] == 0x04) {
        g_mod_attach_resume = reinterpret_cast<void*>(base + kAttachParentLookupResumeRva);
        if (WriteJump(attach_site, reinterpret_cast<void*>(&ModAttachParentDetour),
                      g_attach_lookup_original, kAttachParentLookupPatchSize)) {
            g_attach_lookup_site = attach_site;
            LogInfo("Attach parent guard at +0x%X", static_cast<unsigned>(kAttachParentLookupRva));
        }
    } else {
        LogWarn("Attach parent site mismatch at +0x%X", static_cast<unsigned>(kAttachParentLookupRva));
    }

    auto* count_site = reinterpret_cast<std::uint8_t*>(base + kCountLoadRva);
    auto* char_site = reinterpret_cast<std::uint8_t*>(base + kCharIdLoadRva);
    if (count_site[0] == 0x0F && count_site[1] == 0xB6 && count_site[2] == 0x84 &&
        count_site[3] == 0x80 && char_site[0] == 0x8A && char_site[1] == 0x9C &&
        char_site[2] == 0x0A) {
        std::memcpy(&g_mod_party_char_table_abs, char_site + 3, sizeof(std::uint32_t));
        g_mod_party_count_resume = reinterpret_cast<void*>(base + kCountResumeRva);
        g_mod_party_char_resume = reinterpret_cast<void*>(base + kCharIdResumeRva);
        if (WriteJump(count_site, reinterpret_cast<void*>(&ModPartyCountDetour), g_count_original, 8) &&
            WriteJump(char_site, reinterpret_cast<void*>(&ModPartyCharIdDetour), g_char_original, 7)) {
            g_count_site = count_site;
            g_char_site = char_site;
            LogInfo("Party roster hooks at +0x%X / +0x%X (battle-only override)",
                    static_cast<unsigned>(kCountLoadRva), static_cast<unsigned>(kCharIdLoadRva));
        } else {
            LogWarn("Party roster hooks failed");
        }
    } else {
        LogWarn("Party roster site mismatch");
    }

    auto* init_site = reinterpret_cast<std::uint8_t*>(base + kBattlePartyInitRva);
    if (init_site[0] == 0x55 && init_site[1] == 0x8B && init_site[2] == 0xEC && init_site[3] == 0xA0) {
        InstallTrampJump(init_site, kBattlePartyInitPatchSize,
                         reinterpret_cast<void*>(&ModBattlePartyInitDetour), &g_init_trampoline_mem,
                         &g_mod_battle_init_tramp, g_init_original, &g_init_site, "battle-init");
    }

    auto* bind_site = reinterpret_cast<std::uint8_t*>(base + kBattleModelBindRva);
    if (bind_site[0] == 0x55 && bind_site[1] == 0x8B && bind_site[2] == 0xEC && bind_site[3] == 0xA1) {
        if (InstallTrampJump(bind_site, kBattleModelBindPatchSize,
                             reinterpret_cast<void*>(&ModBattleModelBindDetour),
                             &g_model_bind_trampoline_mem, &g_mod_model_bind_tramp, g_model_bind_original,
                             &g_model_bind_site, "model-bind")) {
            LogInfo("Party model-bind passthrough at +0x%X", static_cast<unsigned>(kBattleModelBindRva));
        }
    }

    auto* anim_site = reinterpret_cast<std::uint8_t*>(base + kBattleAnimBindRva);
    if (anim_site[0] == 0x55 && anim_site[1] == 0x8B && anim_site[2] == 0xEC && anim_site[3] == 0x51 &&
        anim_site[4] == 0xA1) {
        if (InstallTrampJump(anim_site, kBattleAnimBindPatchSize,
                             reinterpret_cast<void*>(&ModBattleAnimBindDetour),
                             &g_anim_bind_trampoline_mem, &g_mod_anim_bind_tramp, g_anim_bind_original,
                             &g_anim_bind_site, "anim-bind")) {
            LogInfo("Party anim-bind passthrough at +0x%X", static_cast<unsigned>(kBattleAnimBindRva));
        }
    }

    auto* face_bank_site = reinterpret_cast<std::uint8_t*>(base + kFaceBankLoadRva);
    if (face_bank_site[0] == 0x8B && face_bank_site[1] == 0x35) {
        g_mod_face_bank_slot_abs = base + kFaceBankPtrRva;
        g_mod_face_bank_resume = reinterpret_cast<void*>(base + kFaceBankLoadResumeRva);
        if (WriteJump(face_bank_site, reinterpret_cast<void*>(&ModFaceBankLoadDetour),
                      g_face_bank_original, kFaceBankLoadPatchSize)) {
            g_face_bank_site = face_bank_site;
            LogInfo("Party face-bank hook at +0x%X", static_cast<unsigned>(kFaceBankLoadRva));
        }
    }

    auto install_fill = [&](std::uintptr_t rva, std::uintptr_t skip_rva, void* detour, void** tramp_mem,
                            void** tramp_slot, void** skip_slot, std::uint8_t* original, void** site_out,
                            const char* name) {
        auto* site = reinterpret_cast<std::uint8_t*>(base + rva);
        if (site[0] != 0x8B || site[1] != 0x35) {
            LogWarn("Party %s site mismatch at +0x%X", name, static_cast<unsigned>(rva));
            return;
        }
        *skip_slot = reinterpret_cast<void*>(base + skip_rva);
        if (InstallTrampJump(site, kMenuFillPatchSize, detour, tramp_mem, tramp_slot, original, site_out,
                             name)) {
            LogInfo("Party %s hook at +0x%X", name, static_cast<unsigned>(rva));
        }
    };
    install_fill(kMenuFillLoopRva, kMenuFaceFinalizeRva, reinterpret_cast<void*>(&ModMenuFillDetour),
                 &g_menu_fill_trampoline_mem, &g_mod_menu_fill_tramp, &g_mod_menu_fill_skip,
                 g_menu_fill_original, &g_menu_fill_site, "status-fill");
    install_fill(kStashFillLoopRva, kStashFaceFinalizeRva, reinterpret_cast<void*>(&ModStashFillDetour),
                 &g_stash_fill_trampoline_mem, &g_mod_stash_fill_tramp, &g_mod_stash_fill_skip,
                 g_stash_fill_original, &g_stash_fill_site, "stash-fill");
    install_fill(kItemFillLoopRva, kItemFaceFinalizeRva, reinterpret_cast<void*>(&ModItemFillDetour),
                 &g_item_fill_trampoline_mem, &g_mod_item_fill_tramp, &g_mod_item_fill_skip,
                 g_item_fill_original, &g_item_fill_site, "assign-fill");

    auto* empty_equip = reinterpret_cast<std::uint8_t*>(base + kEmptyEquipReturnRva);
    if (IsExecutableAddress(empty_equip) && empty_equip[0] == 0x8B && empty_equip[1] == 0x44 &&
        empty_equip[2] == 0x24 && empty_equip[3] == 0x14 && empty_equip[4] == 0x66 &&
        empty_equip[5] == 0x89 && empty_equip[6] == 0x08) {
        g_mod_empty_equip_resume = reinterpret_cast<void*>(base + kEmptyEquipReturnResumeRva);
        if (WriteJump(empty_equip, reinterpret_cast<void*>(&ModEmptyEquipReturnDetour),
                      g_empty_equip_original, kEmptyEquipReturnPatchSize)) {
            g_empty_equip_site = empty_equip;
            LogInfo("Empty-slot equip compact at +0x%X",
                    static_cast<unsigned>(kEmptyEquipReturnRva));
        }
    } else {
        LogWarn("Empty-slot equip site mismatch at +0x%X",
                static_cast<unsigned>(kEmptyEquipReturnRva));
    }

    StartPartyPoll();
    return true;
#endif
}

void RemovePartyHooks() {
    StopPartyPoll();
    CommitFieldPartyRestore();
    SetBattleCullPatches(false);
    ClearAttachParent();
    auto drop_jump = [](void** site, const std::uint8_t* original, std::size_t size) {
        if (*site) {
            RestoreBytes(*site, original, size);
            *site = nullptr;
        }
    };
    auto drop_tramp = [](void** mem, void** slot) {
        if (*mem) {
            VirtualFree(*mem, 0, MEM_RELEASE);
            *mem = nullptr;
        }
        if (slot) {
            *slot = nullptr;
        }
    };
    drop_jump(&g_ally_char_site, g_ally_char_original, kBattleAllyCharIdPatchSize);
    drop_jump(&g_ally_gate_site, g_ally_gate_original, kBattleAllySpawnGatePatchSize);
    drop_jump(&g_ally_count_site, g_ally_count_original, kBattleAllyCountPatchSize);
    drop_jump(&g_attach_lookup_site, g_attach_lookup_original, kAttachParentLookupPatchSize);
    drop_jump(&g_enemy_loaded_site, g_enemy_loaded_original, kEnemyLoadedPatchSize);
    drop_jump(&g_enemy_loaded_alt_site, g_enemy_loaded_alt_original, kEnemyLoadedPatchSize);
    drop_jump(&g_shop_open_site, g_shop_open_original, kShopOpenPatchSize);
    drop_jump(&g_shop_sell_open_site, g_shop_sell_open_original, kShopSellOpenPatchSize);
    drop_jump(&g_shop_sell_list_site, g_shop_sell_list_original, kShopSellListPricePatchSize);
    drop_jump(&g_shop_sell_sel_site, g_shop_sell_sel_original, kShopSellSelectPricePatchSize);
    drop_jump(&g_enemy_model_copy_site, g_enemy_model_copy_original, kEnemyModelCopyPatchSize);
    drop_jump(&g_utf8_dec_site, g_utf8_dec_original, kUtf8DecPatchSize);
    drop_jump(&g_skill_name_site, g_skill_name_original, kSkillNameInternPatchSize);
    drop_jump(&g_skill_parse_site, g_skill_parse_original, kSkillScriptParsePatchSize);
    drop_jump(&g_count_site, g_count_original, 8);
    drop_jump(&g_char_site, g_char_original, 7);
    drop_jump(&g_face_bank_site, g_face_bank_original, kFaceBankLoadPatchSize);
    drop_jump(&g_menu_fill_site, g_menu_fill_original, kMenuFillPatchSize);
    drop_jump(&g_stash_fill_site, g_stash_fill_original, kMenuFillPatchSize);
    drop_jump(&g_item_fill_site, g_item_fill_original, kMenuFillPatchSize);
    drop_jump(&g_empty_equip_site, g_empty_equip_original, kEmptyEquipReturnPatchSize);
    drop_jump(&g_init_site, g_init_original, kBattlePartyInitPatchSize);
    drop_jump(&g_model_bind_site, g_model_bind_original, kBattleModelBindPatchSize);
    drop_jump(&g_anim_bind_site, g_anim_bind_original, kBattleAnimBindPatchSize);
    drop_jump(&g_battle_load_site, g_battle_load_original, kBattleLoadPatchSize);
    drop_jump(&g_battle_setup_site, g_battle_setup_original, kBattleSetupPatchSize);
    drop_tramp(&g_battle_load_trampoline_mem, &g_mod_battle_load_tramp);
    drop_tramp(&g_battle_setup_trampoline_mem, &g_mod_battle_setup_tramp);
    drop_tramp(&g_init_trampoline_mem, &g_mod_battle_init_tramp);
    drop_tramp(&g_model_bind_trampoline_mem, &g_mod_model_bind_tramp);
    drop_tramp(&g_anim_bind_trampoline_mem, &g_mod_anim_bind_tramp);
    drop_tramp(&g_enemy_loaded_trampoline_mem, &g_mod_enemy_loaded_tramp);
    drop_tramp(&g_enemy_loaded_alt_trampoline_mem, &g_mod_enemy_loaded_alt_tramp);
    drop_tramp(&g_shop_open_trampoline_mem, &g_mod_shop_open_tramp);
    drop_tramp(&g_shop_sell_open_trampoline_mem, &g_mod_shop_sell_open_tramp);
    drop_tramp(&g_enemy_model_copy_trampoline_mem, &g_mod_enemy_model_copy_tramp);
    drop_tramp(&g_utf8_dec_trampoline_mem, &g_mod_utf8_dec_tramp);
    drop_tramp(&g_skill_name_trampoline_mem, &g_mod_skill_name_tramp);
    drop_tramp(&g_skill_parse_trampoline_mem, &g_mod_skill_parse_tramp);
    drop_tramp(&g_menu_fill_trampoline_mem, &g_mod_menu_fill_tramp);
    drop_tramp(&g_stash_fill_trampoline_mem, &g_mod_stash_fill_tramp);
    drop_tramp(&g_item_fill_trampoline_mem, &g_mod_item_fill_tramp);
    g_spawn_hooks_ok = false;
    ResetPdatBattlePack();
    FreeFcBanks();
}

}  // namespace grandia_mod
