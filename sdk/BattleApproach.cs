namespace Grandia.Sdk;

/// <summary>
/// How the field encounter started. Live in <see cref="BattleLoadEvent.Encounter"/> byte 11.
/// </summary>
public enum BattleApproach
{
    Normal = 0,
    Initiative = 1,
    Ambush = 2,
}
