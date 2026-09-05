namespace Grandia.Sdk;

/// <summary>
/// Combat element bits (magic row +14, enemy skill +0xA).
/// The four bases OR into the in-game combo names.
/// </summary>
[Flags]
public enum CombatElement
{
    None = 0,
    Fire = 0x10,
    Water = 0x20,
    Wind = 0x40,
    Earth = 0x80,

    /// <summary>Fire + Wind (<c>0x50</c>).</summary>
    Thunder = Fire | Wind,

    /// <summary>Water + Wind (<c>0x60</c>).</summary>
    Ice = Water | Wind,

    /// <summary>Fire + Earth (<c>0x90</c>).</summary>
    Explosion = Fire | Earth,

    /// <summary>Water + Earth (<c>0xA0</c>). Cure, Poizn, Halvah, …</summary>
    Forest = Water | Earth,
}
