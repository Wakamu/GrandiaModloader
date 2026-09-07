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

    public ItemEvent(int id, int cost, int icon, int useStatus)
    {
        Id = (Item)id;
        Cost = cost;
        Icon = icon;
        UseStatus = useStatus;
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

    /// <summary>u8 at record+15 (ReDux para-1 before slash).</summary>
    public int Para1Pre { get; set; }

    /// <summary>u8 at record+16.</summary>
    public int Para2 { get; set; }

    /// <summary>u8 at record+17.</summary>
    public int Para3 { get; set; }

    /// <summary>u8 at record+18.</summary>
    public int Para4 { get; set; }

    /// <summary>u16 at record+19.</summary>
    public int Para1Post { get; set; }

    /// <summary>u16 at record+21.</summary>
    public int Para2Post { get; set; }

    /// <summary>u16 at record+23.</summary>
    public int Para3Post { get; set; }

    /// <summary>u16 at record+25.</summary>
    public int Para4Post { get; set; }

    /// <summary>
    /// u8 at record+8 (weapon class: 1 dagger … 6 bow; Lump of Coal is 0).
    /// Seeded from the live row; assign to change. Unseeded 0 wipes class.
    /// </summary>
    public int Unknown8 { get; set; }

    /// <summary>u8 at record+11.</summary>
    public int Unknown11 { get; set; }

    /// <summary>u8 at record+12.</summary>
    public int Unknown12 { get; set; }

    /// <summary>u8 at record+13.</summary>
    public int Unknown13 { get; set; }

    /// <summary>u8 at record+14.</summary>
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
