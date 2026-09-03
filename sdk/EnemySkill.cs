namespace Grandia.Sdk;

/// <summary>
/// One enemy skill on the model blob (header at model+0x5C, then name).
/// Power / Speed / Element / Strength match the ReDux bestiary columns.
/// </summary>
public sealed class EnemySkill
{
    public EnemySkill()
    {
    }

    public EnemySkill(string name, int power, bool strength, int speed, int element)
    {
        Name = name;
        Power = power;
        Strength = strength;
        Speed = speed;
        Element = element;
    }

    /// <summary>Display name from the model (read-only for identification).</summary>
    public string Name { get; set; } = "";

    /// <summary>u16 at skill header +6.</summary>
    public int Power { get; set; }

    /// <summary>
    /// ReDux "Strength": physical (uses Str) when true. Stored as flags bit 0 clear.
    /// False is a magic hit (Fire Orb, etc.).
    /// </summary>
    public bool Strength { get; set; }

    /// <summary>u8 at skill header +0x13 (vanilla often 30).</summary>
    public int Speed { get; set; }

    /// <summary>u8 at skill header +0xA. 0 = none, 0x10 = Fire. Other elements are raw.</summary>
    public int Element { get; set; }
}
