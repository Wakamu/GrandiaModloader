namespace Grandia.Sdk;

/// <summary>
/// Combat effect class (STAT/BBG row +18, enemy skill header +0xD).
/// <see cref="MagicEvent.Mode"/> / <see cref="EnemySkill.Mode"/> picks the
/// subtype — see <see cref="HealMode"/>, <see cref="DamageKind"/>,
/// <see cref="StatusAilment"/>, <see cref="StatMod"/>, <see cref="ClearAilment"/>.
/// Enemy damage-plus-ailment uses <see cref="EnemySkill.AddAilment"/> /
/// <see cref="EnemySkill.Chance"/>, not a second EffectType.
/// </summary>
public enum EffectType
{
    Heal = 0,
    Damage = 3,
    ProbDamage = 4,
    Status = 5,
    ClearStatus = 6,
    PowerUpDown = 7,
    Drain = 8,
    Special = 9,
    MonsterSpecial = 10,
}
