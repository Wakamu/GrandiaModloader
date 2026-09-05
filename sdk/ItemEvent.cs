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
