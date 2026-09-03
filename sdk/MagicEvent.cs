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
    /// First F/W/U/E learn requirement, or <see cref="MagicElement.None"/>.
    /// Informational unless you also change <see cref="Requirements"/>.
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

    /// <summary>Combat power / heal potency (u16 at combat row +8).</summary>
    public int Power { get; set; }

    /// <summary>
    /// MP (magic) or SP (weapon move) shown in menus and spent in battle.
    /// Combat row u16 at +2, indexed as skill id - 1 (Burn is row 11).
    /// This is not <see cref="IpCost"/>.
    /// </summary>
    public int Cost { get; set; }

    /// <summary>Cast time / IP-gauge field (combat row u16 at +4). Not the MP/SP number.</summary>
    public int IpCost { get; set; }

    /// <summary>ATACK-IP / extra combat value (u16 at +6).</summary>
    public int Area { get; set; }

    /// <summary>Range-ish byte (STAT sec0 +12).</summary>
    public int Range { get; set; }

    /// <summary>
    /// Combat element bits at STAT sec0 +14:
    /// 0x10 fire, 0x20 water, 0x40 wind, 0x80 earth (OR for combos).
    /// </summary>
    public int ElementFlags { get; set; }

    public bool Allows(CharacterId id)
    {
        var bit = 1 << ((int)id - 1);
        return (CharacterMask & bit) != 0;
    }

    public void Allow(CharacterId id) => CharacterMask |= 1 << ((int)id - 1);

    public void Deny(CharacterId id) => CharacterMask &= ~(1 << ((int)id - 1));
}
