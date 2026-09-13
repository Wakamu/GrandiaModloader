namespace Grandia.Sdk;

/// <summary>
/// Kind byte for one of the three item stat lines
/// (<see cref="ItemEvent.Stats"/> — record +16 / +17 / +18).
/// Value is the high byte of the matching para-post word (signed, except
/// <see cref="AttackRange"/> which is 0–255). Ids match vanilla WINDT
/// plus the Battle Formula sheet labels.
/// </summary>
public enum ItemStat
{
    None = 0,
    Strength = 1,
    Vitality = 2,
    Wit = 3,
    Agility = 4,
    /// <summary>Telescope; stored as an unsigned 0–255 reach bonus.</summary>
    AttackRange = 9,
    ComboHits = 10,
    CriticalHits = 11,
    SpRestoreOnHit = 13,
    /// <summary>Dark Armor / Ring of Rage.</summary>
    SpRestoreOnDamage = 14,
    SlowIpLoss = 16,
    Knockback = 17,
    FallResist = 18,
    MagicPower = 19,
    MagicBlockResist = 21,
    MoveBlockResist = 22,
    PlagueResist = 23,
    PoisonResist = 24,
    ParalysisResist = 25,
    SleepResist = 26,
    ConfusionResist = 27,
    AllStatusResist = 28,
    FireResist = 29,
    WaterResist = 30,
    WindResist = 31,
    EarthResist = 32,
    AllMagicResist = 33,
    CriticalResist = 34,
    InstantDeathResist = 35,
    SkillPower = 36,
}

/// <summary>
/// Auto-effect kind at record +13 (<see cref="ItemEvent.Auto"/>).
/// Value is +14 (chance or magnitude); <see cref="ItemAutoEffect.Param"/>
/// is +15 (sheet Auto Par2 — Shocking Knife 2, Assassin 7).
/// </summary>
public enum ItemAuto
{
    None = 0,
    SpellSpeed = 1,
    ReduceSpCost = 2,
    WarpOnAttack = 4,
    Bind = 5,
    RestoreHpOnHit = 7,
    DebuffDefense = 8,
    InstantDeath = 9,
    Silence = 10,
    Stop = 11,
    Poison = 13,
    Paralyze = 14,
    ItemDropRate = 20,
    DoubleWeaponXp = 21,
    DoubleMagicXp = 22,
    DoubleGold = 23,
    RareItemDropRate = 24,
    PreventDamageBelow = 25,
    WarpOnDamage = 28,
    Counter = 30,
    HpRegen = 31,
}
