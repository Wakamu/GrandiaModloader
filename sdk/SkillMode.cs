namespace Grandia.Sdk;

/// <summary>
/// <see cref="EffectType.Status"/> / <see cref="EnemySkill.AddAilment"/> value
/// (combat Mode, or enemy skill header +0xF).
/// </summary>
public enum StatusAilment
{
    None = 0,
    /// <summary>Craze.</summary>
    Confuse = 1,
    /// <summary>Snooze, Yawn, Sleep Gas.</summary>
    Sleep = 2,
    /// <summary>Paralyze Fluid, Stun Mist, Paralyze Mushroom.</summary>
    Paralyze = 3,
    /// <summary>Poizn, Poison Gas / Bite / Lance.</summary>
    Poison = 4,
    /// <summary>Plague / disease (Culture Medium, Plague Spores).</summary>
    Plague = 5,
    /// <summary>Fiora — IP / movement lock.</summary>
    Stop = 6,
    /// <summary>Shhh! — magic block.</summary>
    Silence = 7,
}

/// <summary><see cref="EffectType.Heal"/> Mode.</summary>
public enum HealMode
{
    Hp = 0,
    Revive = 1,
    Sp = 3,
    MpLv1 = 4,
    MpLv2 = 5,
    MpLv3 = 6,
    /// <summary>Bond of Trust–style huge heal (id 110).</summary>
    Special = 7,
    AllMp = 8,
}

/// <summary><see cref="EffectType.Damage"/> Mode (hit formula), not the ailment.</summary>
public enum DamageKind
{
    Physical = 0,
    Magic = 1,
    /// <summary>Weapon move that carries an element (Ice Slash, Thor Cut).</summary>
    WeaponElement = 2,
}

/// <summary><see cref="EffectType.PowerUpDown"/> Mode. Negative <see cref="MagicEvent.Power"/> is a down.</summary>
public enum StatMod
{
    Attack = 0,
    Defense = 1,
    Speed = 2,
    Move = 3,
    MaxHp = 4,
    All = 5,
}

/// <summary>
/// <see cref="EffectType.ClearStatus"/> Mode. Poison / para / plague match
/// <see cref="StatusAilment"/>; wake / blind / silence do not.
/// </summary>
public enum ClearAilment
{
    Wake = 1,
    Blind = 2,
    Paralyze = 3,
    Poison = 4,
    Plague = 5,
    Stop = 6,
    Silence = 7,
    All = 8,
}

/// <summary><see cref="EffectType.ProbDamage"/> Mode. Vanilla only uses instant death.</summary>
public enum DeathKind
{
    InstantDeath = 0,
}
