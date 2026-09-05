namespace Grandia.Sdk;

/// <summary>
/// Mana-egg learn school from the first F/W/U/E requirement.
/// In-battle labels like Forest / Ice / Thunder / Explosion are
/// <see cref="CombatElement"/> combos on <see cref="MagicEvent.ElementFlags"/>,
/// not extra values here. Cure and Poizn are Water+Earth → Forest.
/// </summary>
public enum MagicElement
{
    None = 0,
    Fire = 1,
    Water = 2,
    Wind = 3,
    Earth = 4,
}
