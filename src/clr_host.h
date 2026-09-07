#pragma once

#include <cstdint>

namespace grandia_mod {

bool InstallClrHost();
void RemoveClrHost();
bool ClrHostReady();

// 1 = stem has a RAM patch to apply after bind, 0 = vanilla, -1 = error.
int RuntimeOnMapOpen(const char* stem, std::uint16_t from, std::uint16_t to, int spawn,
                     const char* cache_dir);

#pragma pack(push, 1)
struct MapPatchInfoNative {
    char stem[16];
    std::uint32_t sec7;
    std::int32_t sec7_len;
    std::uint32_t scn;
    std::int32_t scn_len;
    std::uint32_t ofs;
    std::int32_t ofs_len;
    std::int32_t stock_scn_len;
    std::int32_t stock_ofs_len;
    std::int32_t dirty;
};
#pragma pack(pop)
static_assert(sizeof(MapPatchInfoNative) == 52, "MapPatchInfoNative pack must match C#");

int RuntimeMapPatchInfo(const char* stem, MapPatchInfoNative* info);

#pragma pack(push, 1)
struct MapFileNative {
    char stem[16];
    std::int32_t kind;
    std::uint32_t ptr;
    std::int32_t len;
};
#pragma pack(pop)
static_assert(sizeof(MapFileNative) == 28, "MapFileNative pack must match C#");

// kind 0=mdp 1=scn 2=ofs. 1 = found (ptr/len filled), 0 = none.
int RuntimeGetMapFile(const char* stem, int kind, const void** ptr, int* len);

#pragma pack(push, 1)
struct ScriptLookupNative {
    char stem[16];
    std::uint16_t script_id;
    std::uint16_t pad;
    std::uint32_t ip;
    std::int32_t redirect;
};
#pragma pack(pop)
static_assert(sizeof(ScriptLookupNative) == 28, "ScriptLookupNative pack must match C#");

#pragma pack(push, 1)
struct CallHookNative {
    char stem[16];
    std::int32_t table;
    std::uint16_t hook_id;
    std::uint16_t pad;
    std::uint32_t row;
    std::int32_t redirect;
};
#pragma pack(pop)
static_assert(sizeof(CallHookNative) == 32, "CallHookNative pack must match C#");

int RuntimeOnScriptLookup(const char* stem, std::uint16_t script_id, std::uint32_t* ip_out);
int RuntimeOnCallHook(const char* stem, int table, std::uint16_t hook_id, std::uint32_t* row_out);

#pragma pack(push, 1)
struct EventFlagNative {
    std::uint32_t event_id;
    std::uint32_t flag_offset;
    std::uint32_t flag_value;
    std::uint32_t mask;
    std::uint32_t ecx_index;
    std::uint32_t caller_rva;
    std::int32_t kind;
    std::int32_t suppress_loot;
    std::int32_t suppress_gold;
};

struct ItemAssignNative {
    std::uint32_t event_id;
    std::uint32_t return_rva;
    std::int32_t skip_vanilla;
};

struct FieldGoldNative {
    std::uint32_t event_id;
    std::int32_t amount;
};

struct WorldMapConfirmNative {
    std::uint16_t map_id;
    std::uint16_t pad;
    std::int32_t allow;
};

#pragma pack(push, 1)
struct MapTravelNative {
    std::uint16_t from;
    std::uint16_t dest;
    std::int32_t spawn;
    std::int32_t allow;
    std::int32_t kind;
};
#pragma pack(pop)
static_assert(sizeof(MapTravelNative) == 16, "MapTravelNative pack must match C#");

constexpr unsigned kWorldMapSlots = 32;

struct WorldMapLoadNative {
    std::int32_t set_id;
    std::int32_t amap_index;
    std::int32_t origin_ctx;
    std::int32_t count;
    std::int32_t dirty;
    std::int32_t slot[kWorldMapSlots];
    std::uint16_t map_id[kWorldMapSlots];
    std::uint16_t aux[kWorldMapSlots];
    std::int16_t x[kWorldMapSlots];
    std::int16_t y[kWorldMapSlots];
    std::int32_t revealed[kWorldMapSlots];
    std::int32_t accessible[kWorldMapSlots];
    std::int32_t picture[kWorldMapSlots];
    std::uint16_t extra[kWorldMapSlots * 4];
    std::uint8_t extra_n[kWorldMapSlots];
    char picture_path[kWorldMapSlots][260];
    std::int16_t picture_w[kWorldMapSlots];
    std::int16_t picture_h[kWorldMapSlots];
};
static_assert(sizeof(WorldMapLoadNative) == 9524, "WorldMapLoadNative pack must match C#");
#pragma pack(pop)

constexpr int kMaxSaveTrailer = 1024;

#pragma pack(push, 1)
struct SaveEventNative {
    std::int32_t slot;
    std::int32_t trailer_len;
    std::uint8_t trailer[kMaxSaveTrailer];
};

struct LoadEventNative {
    std::int32_t slot;
    std::int32_t phase;
    std::int32_t allow;
    std::int32_t trailer_len;
    std::uint8_t trailer[kMaxSaveTrailer];
};
#pragma pack(pop)

int RuntimeOnEventFlag(EventFlagNative* req);
int RuntimeOnItemAssignUi(ItemAssignNative* req);
int RuntimeOnFieldGoldAdd(FieldGoldNative* req);
int RuntimeOnWorldMapConfirm(WorldMapConfirmNative* req);
int RuntimeOnWorldMapLoad(WorldMapLoadNative* req);
int RuntimeOnMapTravel(MapTravelNative* req);
int RuntimeOnSave(SaveEventNative* req);
int RuntimeOnLoad(LoadEventNative* req);

// Keep in sync with EncounterSlot.DumpSize (header 16 + 20 * MaxCount 4).
constexpr unsigned kBattleEncounterMaxSlots = 4;
constexpr unsigned kBattleEncounterSlotSize = 20;
constexpr unsigned kBattleEncounterHeader = 16;
constexpr unsigned kBattleEncounterDump =
    kBattleEncounterHeader + kBattleEncounterSlotSize * kBattleEncounterMaxSlots;

#pragma pack(push, 1)
struct BattleLoadNative {
    std::uint8_t party[4];
    std::uint8_t formation;
    std::uint8_t battle_mode;
    std::uint16_t map;
    std::uint16_t dest;
    std::int32_t spawn;
    std::uint32_t enc_obj;
    std::uint32_t enc_row;
    std::uint16_t pack_w0;
    std::uint16_t pack_w2;
    std::uint16_t pack_w4;
    std::uint16_t pack_w6;
    std::uint8_t encounter[kBattleEncounterDump];
    std::uint8_t species[16];
};

struct MenuOpenNative {
    std::int32_t which;
    std::uint8_t party[4];
};
#pragma pack(pop)

#pragma pack(push, 1)
struct EnemyLoadedNative {
    std::int32_t actor_id;
    std::int32_t catalog;
    std::int32_t form_row;
    std::int32_t level;
    std::int32_t hp;
    std::int32_t max_hp;
    std::int32_t str;
    std::int32_t vit;
    std::int32_t wit;
    std::int32_t agi;
    std::int32_t exp;
    std::int32_t gold;
    std::int32_t attack_count;
    std::int32_t attack_range;
    std::int32_t drop_item[2];
    std::int32_t drop_rate[2];
    std::int32_t fire_resist;
    std::int32_t water_resist;
    std::int32_t wind_resist;
    std::int32_t earth_resist;
    std::int32_t skill_count;
    std::int32_t skill_power[8];
    std::int32_t skill_speed[8];
    std::int32_t skill_element[8];
    std::int32_t skill_uses_strength[8];
    std::int32_t skill_effect[8];
    std::int32_t skill_mode[8];
    std::int32_t skill_add[8];
    std::int32_t skill_chance[8];
    std::int32_t skill_add_level[8];
    char skill_name[8][24];
};
#pragma pack(pop)

constexpr unsigned kShopPages = 3;
constexpr unsigned kShopSlots = 16;
constexpr unsigned kShopSellMax = 64;

#pragma pack(push, 1)
struct ShopOpenNative {
    std::uint16_t map;
    std::int32_t kind;
    std::int32_t items[kShopPages * kShopSlots];
    std::int32_t prices[kShopPages * kShopSlots];
    std::int32_t sell_count;
    std::int32_t sell_item[kShopSellMax];
    std::int32_t sell_gold[kShopSellMax];
};
#pragma pack(pop)

int RuntimeOnBattleLoad(BattleLoadNative* req);
int RuntimeOnBattleSetup(BattleLoadNative* req);
int RuntimeOnMenuOpen(MenuOpenNative* req);
int RuntimeOnEnemyLoaded(EnemyLoadedNative* req);
int RuntimeOnShopOpen(ShopOpenNative* req);

#pragma pack(push, 1)
struct TickNative {
    std::uint16_t buttons;
    std::uint8_t left_trigger;
    std::uint8_t right_trigger;
    std::int32_t block;
};
#pragma pack(pop)
static_assert(sizeof(TickNative) == 8, "TickNative pack must match C#");

int RuntimeOnTick(TickNative* req);
int RuntimeOnTitleScreen();

constexpr unsigned kCharacterLearnedMax = 128;

#pragma pack(push, 1)
struct CharacterNative {
    std::int32_t id;
    std::int32_t level;
    std::int32_t hp;
    std::int32_t max_hp;
    std::int32_t sp;
    std::int32_t max_sp;
    std::int32_t str;
    std::int32_t vit;
    std::int32_t wit;
    std::int32_t agi;
    std::int32_t exp;
    std::int32_t fire;
    std::int32_t water;
    std::int32_t wind;
    std::int32_t earth;
    std::int32_t mp1;
    std::int32_t mp2;
    std::int32_t mp3;
    std::int32_t weapon_level[4];
    std::int32_t weapon_type[4];
    std::int32_t learned_count;
    std::int32_t learned[kCharacterLearnedMax];
};
#pragma pack(pop)
static_assert(sizeof(CharacterNative) == 620, "CharacterNative pack must match C#");

#pragma pack(push, 1)
struct ItemNative {
    std::int32_t id;
    std::int32_t cost;
    std::int32_t icon;
    std::int32_t use_status;
    std::int32_t unknown7;
    std::int32_t para1_pre;
    std::int32_t para2;
    std::int32_t para3;
    std::int32_t para4;
    std::int32_t para1_post;
    std::int32_t para2_post;
    std::int32_t para3_post;
    std::int32_t para4_post;
    std::int32_t sell_price;
    std::int32_t effect;
    std::int32_t effect_value;
    std::int32_t unknown8;
    std::int32_t unknown11;
    std::int32_t unknown12;
    std::int32_t unknown13;
    std::int32_t unknown14;
    std::int32_t unknown27;
};
#pragma pack(pop)
static_assert(sizeof(ItemNative) == 88, "ItemNative pack must match C#");

#pragma pack(push, 1)
struct Text1Native {
    std::uint32_t src;
    std::int32_t src_len;
    std::uint32_t dest;
    std::int32_t dest_len;
};
#pragma pack(pop)

#pragma pack(push, 1)
struct MagicNative {
    std::int32_t id;
    std::int32_t element;
    std::int32_t char_mask;
    std::int32_t req_count;
    std::int32_t req_kind[4];
    std::int32_t req_level[4];
    std::int32_t power;
    std::int32_t ip_cost;
    std::int32_t cost;
    std::int32_t area;
    std::int32_t range;
    std::int32_t element_flags;
    std::int32_t effect;
    std::int32_t mode;
    std::int32_t crit;
    char name[32];
    std::int32_t radius;
    std::int32_t distance;
};
#pragma pack(pop)
static_assert(sizeof(MagicNative) == 124, "MagicNative pack must match C#");

int RuntimeOnCharacter(CharacterNative* req);
int RuntimeOnItem(ItemNative* req);
int RuntimePatchText1(Text1Native* req);
int RuntimeOnMagic(MagicNative* req);

constexpr unsigned kDialogueMaxPayload = 4096;

#pragma pack(push, 1)
struct DialogueNative {
    char stem[16];
    std::uint16_t script_id;
    std::uint16_t pad;
    std::int32_t op_index;
    std::int32_t kind;
    std::uint32_t src;
    std::int32_t src_len;
    std::int32_t dest_len;
    std::uint8_t dest[kDialogueMaxPayload];
    std::int32_t skip;
};
#pragma pack(pop)
static_assert(sizeof(DialogueNative) == 16 + 2 + 2 + 4 + 4 + 4 + 4 + 4 + 4096 + 4,
              "DialogueNative pack must match C#");

int RuntimeOnDialogue(DialogueNative* req);

}  // namespace grandia_mod
