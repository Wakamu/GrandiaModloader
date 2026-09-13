namespace Grandia.Sdk;

/// <summary>
/// One WINDT sec3 item record (28 bytes at (id-1)*28) after the heap
/// copy is live. Fires for each catalog id on every status / shop /
/// stash load (the game recopies vanilla WINDT each time).
/// Mutations write every live sec3 alias (shop cost, icon, paras).
/// </summary>
public sealed class ItemEvent
{
    private int _sellPrice;
    private bool _sellExplicit;

    private string? _name;
    private string? _shortName;
    private string? _description;
    private bool _nameSet;
    private bool _shortNameSet;
    private bool _descriptionSet;

    public const int StatSlotCount = 3;

    public ItemEvent(int id, int cost, int icon, int useStatus)
    {
        Id = (Item)id;
        Cost = cost;
        Icon = icon;
        UseStatus = useStatus;
        Stats =
        [
            new ItemStatBonus(this, 0),
            new ItemStatBonus(this, 1),
            new ItemStatBonus(this, 2),
        ];
        Auto = new ItemAutoEffect(this);
    }

    public Item Id { get; }

    /// <summary>u16 at record+4 (shop buy gold).</summary>
    public int Cost { get; set; }

    /// <summary>
    /// Shop sell gold. WINDT has no sell field — vanilla shops show
    /// <see cref="Cost"/>/2 (minimum 1 if Cost &gt; 0). Defaults to that
    /// half of the current <see cref="Cost"/>. Set this to override sell
    /// in every shop. <see cref="ShopOpenEvent.SetSellPrice"/> still wins
    /// for one shop session.
    /// </summary>
    public int SellPrice
    {
        get => _sellExplicit ? _sellPrice : DefaultSellGold(Cost);
        set
        {
            _sellExplicit = true;
            _sellPrice = value < 0 ? 0 : value;
        }
    }

    /// <summary>u8 at record+6.</summary>
    public int Icon { get; set; }

    /// <summary>u16 at record+2.</summary>
    public int UseStatus { get; set; }

    /// <summary>
    /// Skill / item-effect id at record+9. Herbs is <see cref="Skill.Heal"/>,
    /// Dynamite is <see cref="Skill.Burnflame"/>, Cholla Flowers is
    /// <see cref="Skill.RestoreLv1Mp"/>. 0 = not a usable combat effect
    /// (equipment, keys, seeds).
    /// </summary>
    public Skill Effect { get; set; }

    /// <summary>
    /// Magnitude at record+10 for <see cref="Effect"/> (heal amount, status
    /// chance, spell power override). Herbs is 15.
    /// </summary>
    public int EffectValue { get; set; }

    /// <summary>u8 at record+7.</summary>
    public int Unknown7 { get; set; }

    /// <summary>
    /// Three typed para lines (Wooden Sword Strength 7; Godspeed Wit 30).
    /// Same bytes as <see cref="Para2"/>–<see cref="Para4"/> and the high
    /// bytes of <see cref="Para1Post"/>–<see cref="Para3Post"/>.
    /// </summary>
    public ItemStatBonus[] Stats { get; }

    /// <summary>
    /// Auto Effect (on-hit proc, regen, counter). Same bytes as
    /// <see cref="Unknown13"/> / <see cref="Unknown14"/> / <see cref="Para1Pre"/>.
    /// </summary>
    public ItemAutoEffect Auto { get; }

    /// <summary>
    /// Weapon reach at record+25 (low byte of <see cref="Para4Post"/>).
    /// Wooden Sword 1, Force Knife 10, Angel's Darts 16. Not the Telescope
    /// bonus — that is <see cref="ItemStat.AttackRange"/> on <see cref="Stats"/>.
    /// </summary>
    public int AttackRange
    {
        get => Para4Post & 0xFF;
        set => Para4Post = (Para4Post & ~0xFF) | (value & 0xFF);
    }

    /// <summary>
    /// Weapon class at record+8. Same ids as <see cref="WeaponType"/>
    /// (1 dagger … 6 bow). Key items are <see cref="WeaponType.None"/>.
    /// Unseeded 0 wipes class.
    /// </summary>
    public WeaponType WeaponKind
    {
        get => (WeaponType)Unknown8;
        set => Unknown8 = (int)value;
    }

    /// <summary>u8 at record+15 (sheet Auto Par2 / <see cref="Auto"/>.<see cref="ItemAutoEffect.Param"/>).</summary>
    public int Para1Pre { get; set; }

    /// <summary>u8 at record+16 (stat-1 kind / <see cref="Stats"/>[0].<see cref="ItemStatBonus.Kind"/>).</summary>
    public int Para2 { get; set; }

    /// <summary>u8 at record+17 (stat-2 kind).</summary>
    public int Para3 { get; set; }

    /// <summary>u8 at record+18 (stat-3 kind).</summary>
    public int Para4 { get; set; }

    /// <summary>u16 at record+19 (stat-1 value in the high byte).</summary>
    public int Para1Post { get; set; }

    /// <summary>u16 at record+21 (stat-2 value in the high byte).</summary>
    public int Para2Post { get; set; }

    /// <summary>u16 at record+23 (stat-3 value in the high byte).</summary>
    public int Para3Post { get; set; }

    /// <summary>u16 at record+25 (low = <see cref="AttackRange"/>, high = drop anime).</summary>
    public int Para4Post { get; set; }

    /// <summary>Same as <see cref="WeaponKind"/> (old name).</summary>
    public int Unknown8 { get; set; }

    /// <summary>u8 at record+11.</summary>
    public int Unknown11 { get; set; }

    /// <summary>u8 at record+12.</summary>
    public int Unknown12 { get; set; }

    /// <summary>u8 at record+13 (auto kind / <see cref="Auto"/>.<see cref="ItemAutoEffect.Kind"/>).</summary>
    public int Unknown13 { get; set; }

    /// <summary>u8 at record+14 (auto value / <see cref="Auto"/>.<see cref="ItemAutoEffect.Value"/>).</summary>
    public int Unknown14 { get; set; }

    /// <summary>u8 at record+27.</summary>
    public int Unknown27 { get; set; }

    /// <summary>
    /// Menu display name from <c>TEXT1.BIN</c> sec6 (without the
    /// <c>0x03</c> marker). Assign to rename; unread / unassigned keeps
    /// the file string. Host patches <c>text1.bin</c> in place on fopen
    /// (same file size). A string that does not fit its section is skipped.
    /// </summary>
    public string Name
    {
        get => _name ?? "";
        set
        {
            _name = value ?? "";
            _nameSet = true;
        }
    }

    /// <summary>Battle / short label from TEXT1 sec5 (no marker).</summary>
    public string ShortName
    {
        get => _shortName ?? "";
        set
        {
            _shortName = value ?? "";
            _shortNameSet = true;
        }
    }

    /// <summary>Flavor / para line from TEXT1 sec7 (without <c>0x03</c>).</summary>
    public string Description
    {
        get => _description ?? "";
        set
        {
            _description = value ?? "";
            _descriptionSet = true;
        }
    }

    internal bool NameSet => _nameSet;
    internal bool ShortNameSet => _shortNameSet;
    internal bool DescriptionSet => _descriptionSet;

    internal void SeedText(string? name, string? shortName, string? description)
    {
        _name = name ?? "";
        _shortName = shortName ?? "";
        _description = description ?? "";
    }

    internal int GetStatKind(int slot) => slot switch
    {
        0 => Para2,
        1 => Para3,
        2 => Para4,
        _ => 0,
    };

    internal void SetStatKind(int slot, int kind)
    {
        switch (slot)
        {
            case 0: Para2 = kind; break;
            case 1: Para3 = kind; break;
            case 2: Para4 = kind; break;
        }
    }

    internal int GetStatPacked(int slot) => slot switch
    {
        0 => Para1Post,
        1 => Para2Post,
        2 => Para3Post,
        _ => 0,
    };

    internal void SetStatPacked(int slot, int packed)
    {
        switch (slot)
        {
            case 0: Para1Post = packed; break;
            case 1: Para2Post = packed; break;
            case 2: Para3Post = packed; break;
        }
    }

    internal static int PackStatHigh(int packed, int value)
    {
        var raw = unchecked((byte)value);
        return (packed & 0xFF) | (raw << 8);
    }

    /// <summary>Vanilla shop rule: <paramref name="cost"/>/2, or 1 if cost is 1.</summary>
    public static int DefaultSellGold(int cost)
    {
        if (cost <= 0)
        {
            return 0;
        }

        var half = cost / 2;
        return half > 0 ? half : 1;
    }
}
