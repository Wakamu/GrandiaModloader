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

    public EnemySkill(string name, int power, bool strength, int speed, int element,
        EffectType effect = EffectType.Heal, int mode = 0,
        StatusAilment addAilment = StatusAilment.None, int chance = 0, int addLevel = 0)
    {
        Name = name;
        Power = power;
        Strength = strength;
        Speed = speed;
        Element = element;
        Effect = effect;
        Mode = mode;
        AddAilment = addAilment;
        Chance = chance;
        AddLevel = addLevel;
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

    /// <summary>
    /// u8 at skill header +0xA. Same bits as <see cref="CombatElement"/>.
    /// 0 = none, 0x10 fire, 0xA0 forest, …
    /// </summary>
    public int Element { get; set; }

    /// <summary>Typed <see cref="Element"/>.</summary>
    public CombatElement Combat
    {
        get => (CombatElement)Element;
        set => Element = (int)value;
    }

    /// <summary>u8 at skill header +0xD. Same enum as <see cref="MagicEvent.Effect"/>.</summary>
    public EffectType Effect { get; set; }

    /// <summary>
    /// u8 at skill header +0xE. Subtype for <see cref="Effect"/>
    /// (<see cref="DamageKind"/>, <see cref="StatusAilment"/>, …).
    /// </summary>
    public int Mode { get; set; }

    public DamageKind Damage
    {
        get => (DamageKind)Mode;
        set => Mode = (int)value;
    }

    public StatusAilment Status
    {
        get => (StatusAilment)Mode;
        set => Mode = (int)value;
    }

    public HealMode Heal
    {
        get => (HealMode)Mode;
        set => Mode = (int)value;
    }

    public StatMod Stat
    {
        get => (StatMod)Mode;
        set => Mode = (int)value;
    }

    /// <summary>
    /// Extra ailment on a damage hit (header +0xF). Same numbers as
    /// <see cref="StatusAilment"/>. <see cref="StatusAilment.None"/> = no add-on.
    /// </summary>
    public StatusAilment AddAilment { get; set; }

    /// <summary>
    /// Percent chance for <see cref="AddAilment"/> (header +0x10, 0–100).
    /// Poison Bite is 75, Stun Mist 50, vanilla 100 is “always”.
    /// </summary>
    public int Chance { get; set; }

    /// <summary>Status intensity / duration at header +0x11 (vanilla 1–6).</summary>
    public int AddLevel { get; set; }
}
