namespace Grandia.Sdk;

/// <summary>
/// One party skill from live WINDT sec7/sec8 (field menus) and STAT/BBG
/// copies (battle). Fires on every status / shop / stash WINDT load and
/// on battle load. Mutations write every live copy.
/// Magic and weapon moves share this catalog; ids match <see cref="Skill"/>.
/// </summary>
public sealed class MagicEvent
{
    public const int MaxRequirements = 3;

    public MagicEvent(int id, string name, MagicElement element, int characterMask,
        IEnumerable<LearnRequirement>? requirements = null)
    {
        Id = (Skill)id;
        Name = name ?? "";
        Element = element;
        CharacterMask = characterMask;
        Requirements = requirements?.ToList() ?? [];
    }

    /// <summary>Skill bit id (same as <see cref="CharacterEvent.Learn"/>).</summary>
    public Skill Id { get; }

    public string Name { get; }

    /// <summary>
    /// First F/W/U/E learn school, or <see cref="MagicElement.None"/>.
    /// Cure/Poizn are Water here; the battle icon is Forest via
    /// <see cref="Combat"/>. Informational unless you also change
    /// <see cref="Requirements"/>.
    /// </summary>
    public MagicElement Element { get; set; }

    /// <summary>
    /// Who can auto-learn it (bit <c>char_id-1</c>). Burn is <c>0x9F</c>
    /// (not Milda/Guido), not 0xFF. Use <see cref="Allow"/> / <see cref="Deny"/>.
    /// </summary>
    public int CharacterMask { get; set; }

    /// <summary>
    /// Up to three learn requirements (STAT sec2 pairs). Vanilla magic uses
    /// F/W/U/E levels; weapon moves use S/M/A/D/H/B. Written back.
    /// </summary>
    public List<LearnRequirement> Requirements { get; }

    /// <summary>
    /// Base Power (s16 at combat row +8). Damage / heal amount, or
    /// signed buff/debuff stages. FAQ writes (+2) / (−1); 65535 is −1
    /// if read as unsigned. Caps at +7 / −7 for stat mods.
    /// </summary>
    public int Power { get; set; }

    /// <summary>
    /// MP (magic) or SP (weapon move). Combat row u16 at +2.
    /// Not <see cref="Speed"/>.
    /// </summary>
    public int Cost { get; set; }

    /// <summary>
    /// Casting Time (combat row u16 at +4). Redux labels this Speed.
    /// Burn! is 30, Shockwave 90. Not MP/SP — that is <see cref="Cost"/>.
    /// </summary>
    public int Speed { get; set; }

    /// <summary>Same as <see cref="Speed"/> (old name).</summary>
    public int IpCost
    {
        get => Speed;
        set => Speed = value;
    }

    /// <summary>
    /// IP Knockback (combat row u16 at +6). How far the target is pushed
    /// on the IP bar. V-Slash is 3500, Milda Hit 9999, Heal is 0.
    /// </summary>
    public int IpKnockback { get; set; }

    /// <summary>Same as <see cref="IpKnockback"/> (old name).</summary>
    public int Area
    {
        get => IpKnockback;
        set => IpKnockback = value;
    }

    /// <summary>
    /// EXP Rate (combat row +12). Element / weapon XP per target hit.
    /// Heal vanilla 8; Redux 15. Not targeting range.
    /// </summary>
    public int Exp { get; set; }

    /// <summary>Same as <see cref="Exp"/> (old name). Real AoE is <see cref="Radius"/>.</summary>
    public int Range
    {
        get => Exp;
        set => Exp = value;
    }

    /// <summary>
    /// Circle AoE size (combat row +13). Burn! 15, Howl 32, Shockwave 30.
    /// 0 is single-target / no radius.
    /// </summary>
    public int Radius { get; set; }

    /// <summary>
    /// Walk-up Distance (combat row +23). V-Slash is 4. 0 is “Any”.
    /// Independent of equipped weapon range.
    /// </summary>
    public int Distance { get; set; }

    /// <summary>
    /// Combat element bits at STAT sec0 +14. Prefer <see cref="Combat"/>.
    /// 0x10 fire, 0x20 water, 0x40 wind, 0x80 earth (OR for combos).
    /// Forest is Water|Earth (<c>0xA0</c>).
    /// </summary>
    public int ElementFlags { get; set; }

    /// <summary>
    /// Typed <see cref="ElementFlags"/>. Cure / Poizn / Stram are
    /// <see cref="CombatElement.Forest"/>.
    /// </summary>
    public CombatElement Combat
    {
        get => (CombatElement)ElementFlags;
        set => ElementFlags = (int)value;
    }

    /// <summary>Combat row +18. Heal, damage, status, drain, …</summary>
    public EffectType Effect { get; set; }

    /// <summary>
    /// Combat row +19. Meaning depends on <see cref="Effect"/> —
    /// use <see cref="Heal"/> / <see cref="Damage"/> / <see cref="Status"/> /
    /// <see cref="Stat"/> / <see cref="Clear"/> instead of raw numbers.
    /// Party ailment-on-hit is not a percent here — that is
    /// <see cref="EnemySkill.AddAilment"/> / <see cref="EnemySkill.Chance"/>.
    /// Crit rate is <see cref="CriticalChance"/>.
    /// </summary>
    public int Mode { get; set; }

    /// <summary>
    /// Cancel % at combat row +17 (0–100). Chance to cancel an enemy
    /// between COM and ACT. Shockwave is 10, Midair Cut is 100.
    /// Not <see cref="EnemySkill.Chance"/> (status proc).
    /// </summary>
    public int CancelChance { get; set; }

    /// <summary>Same as <see cref="CancelChance"/> (old name).</summary>
    public int CriticalChance
    {
        get => CancelChance;
        set => CancelChance = value;
    }

    public HealMode Heal
    {
        get => (HealMode)Mode;
        set => Mode = (int)value;
    }

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

    public StatMod Stat
    {
        get => (StatMod)Mode;
        set => Mode = (int)value;
    }

    public ClearAilment Clear
    {
        get => (ClearAilment)Mode;
        set => Mode = (int)value;
    }

    public DeathKind Death
    {
        get => (DeathKind)Mode;
        set => Mode = (int)value;
    }

    public bool Allows(CharacterId id)
    {
        var bit = 1 << ((int)id - 1);
        return (CharacterMask & bit) != 0;
    }

    public void Allow(CharacterId id) => CharacterMask |= 1 << ((int)id - 1);

    public void Deny(CharacterId id) => CharacterMask &= ~(1 << ((int)id - 1));
}
