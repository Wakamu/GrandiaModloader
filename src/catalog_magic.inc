// Auto-copied from Grandipelago character_stats_skills.inc — magic only.
struct MagicDef {
    std::uint16_t id;
    std::uint8_t mask;
    std::uint8_t nreq;
    char kind[4];
    std::uint8_t level[4];
    const char* name;
};
const MagicDef kMagicCatalog[] = {
    { 12, 0xFF, 1, { 'F', ' ', ' ', ' ' }, { 1, 0, 0, 0 }, "Burn!" },
    { 40, 0xFF, 2, { 'F', 'U', ' ', ' ' }, { 5, 4, 0, 0 }, "Zap!" },
    { 17, 0xFF, 1, { 'U', ' ', ' ', ' ' }, { 1, 0, 0, 0 }, "Howl" },
    { 18, 0xFF, 1, { 'U', ' ', ' ', ' ' }, { 2, 0, 0, 0 }, "Runner" },
    { 35, 0xFF, 2, { 'U', 'W', ' ', ' ' }, { 3, 2, 0, 0 }, "Crackle" },
    { 36, 0xFF, 2, { 'U', 'W', ' ', ' ' }, { 6, 5, 0, 0 }, "Freeze!" },
    { 5, 0xFF, 1, { 'W', ' ', ' ', ' ' }, { 1, 0, 0, 0 }, "Heal" },
    { 7, 0xFF, 1, { 'W', ' ', ' ', ' ' }, { 3, 0, 0, 0 }, "Snooze" },
    { 24, 0xFF, 2, { 'W', 'E', ' ', ' ' }, { 3, 2, 0, 0 }, "Cure" },
    { 23, 0xFF, 2, { 'W', 'E', ' ', ' ' }, { 5, 3, 0, 0 }, "Poizn" },
    { 25, 0xFF, 2, { 'W', 'E', ' ', ' ' }, { 6, 5, 0, 0 }, "Stram" },
    { 99, 0xFF, 1, { 'E', ' ', ' ', ' ' }, { 1, 0, 0, 0 }, "Diggin'" },
    { 1, 0xFF, 1, { 'E', ' ', ' ', ' ' }, { 6, 0, 0, 0 }, "Def-Loss" },
    { 31, 0xFF, 2, { 'E', 'F', ' ', ' ' }, { 1, 1, 0, 0 }, "WOW!" },
    { 30, 0xFF, 2, { 'E', 'F', ' ', ' ' }, { 7, 6, 0, 0 }, "BOOM!" },
    { 13, 0xFF, 1, { 'F', ' ', ' ', ' ' }, { 4, 0, 0, 0 }, "Burnflame" },
    { 14, 0xFF, 1, { 'F', ' ', ' ', ' ' }, { 18, 0, 0, 0 }, "Burnstrike" },
    { 42, 0xFF, 2, { 'F', 'U', ' ', ' ' }, { 10, 9, 0, 0 }, "Zap All" },
    { 19, 0xFF, 1, { 'U', ' ', ' ', ' ' }, { 6, 0, 0, 0 }, "Howlslash" },
    { 37, 0xFF, 2, { 'U', 'W', ' ', ' ' }, { 10, 8, 0, 0 }, "Cold" },
    { 39, 0xFF, 2, { 'U', 'W', ' ', ' ' }, { 10, 12, 0, 0 }, "Crackling" },
    { 6, 0xFF, 1, { 'W', ' ', ' ', ' ' }, { 5, 0, 0, 0 }, "Alheal" },
    { 8, 0xFF, 1, { 'W', ' ', ' ', ' ' }, { 8, 0, 0, 0 }, "Healer" },
    { 2, 0xFF, 1, { 'E', ' ', ' ', ' ' }, { 3, 0, 0, 0 }, "Tremor" },
    { 3, 0xFF, 1, { 'E', ' ', ' ', ' ' }, { 12, 0, 0, 0 }, "Gravity" },
    { 33, 0xFF, 2, { 'E', 'F', ' ', ' ' }, { 10, 10, 0, 0 }, "BOOM-POW!" },
    { 15, 0xFF, 1, { 'F', ' ', ' ', ' ' }, { 9, 0, 0, 0 }, "Burnflare" },
    { 16, 0xFF, 1, { 'F', ' ', ' ', ' ' }, { 25, 0, 0, 0 }, "Fireburner" },
    { 43, 0xFF, 2, { 'F', 'U', ' ', ' ' }, { 21, 19, 0, 0 }, "DragonZap" },
    { 41, 0xFF, 2, { 'F', 'U', ' ', ' ' }, { 19, 15, 0, 0 }, "GadZap" },
    { 22, 0xFF, 1, { 'U', ' ', ' ', ' ' }, { 23, 0, 0, 0 }, "Howlnado" },
    { 11, 0xFF, 1, { 'W', ' ', ' ', ' ' }, { 16, 0, 0, 0 }, "Resurrect" },
    { 10, 0xFF, 1, { 'W', ' ', ' ', ' ' }, { 20, 0, 0, 0 }, "Alhealer+" },
    { 29, 0xFF, 2, { 'W', 'E', ' ', ' ' }, { 19, 16, 0, 0 }, "Halvah" },
    { 4, 0xFF, 1, { 'E', ' ', ' ', ' ' }, { 19, 0, 0, 0 }, "Quake" },
    { 34, 0xFF, 2, { 'E', 'F', ' ', ' ' }, { 20, 19, 0, 0 }, "BA-BOOM!" },
    { 21, 0xFF, 1, { 'U', ' ', ' ', ' ' }, { 12, 0, 0, 0 }, "Shhh!" },
    { 38, 0xFF, 2, { 'U', 'W', ' ', ' ' }, { 8, 10, 0, 0 }, "Fiora" },
    { 97, 0xFF, 2, { 'U', 'W', ' ', ' ' }, { 20, 23, 0, 0 }, "Protect" },
    { 26, 0xFF, 2, { 'W', 'E', ' ', ' ' }, { 10, 10, 0, 0 }, "Craze" },
    { 32, 0xFF, 2, { 'E', 'F', ' ', ' ' }, { 15, 13, 0, 0 }, "Meteor Strike" },
    { 9, 0xFF, 1, { 'W', ' ', ' ', ' ' }, { 12, 0, 0, 0 }, "Alhealer" },
    { 44, 0xFF, 1, { 'F', ' ', ' ', ' ' }, { 12, 0, 0, 0 }, "Magic Art" },
    { 45, 0xFF, 2, { 'W', 'E', ' ', ' ' }, { 20, 20, 0, 0 }, "Star Symphony" },
};
constexpr unsigned kMagicCatalogCount = sizeof(kMagicCatalog) / sizeof(kMagicCatalog[0]);
