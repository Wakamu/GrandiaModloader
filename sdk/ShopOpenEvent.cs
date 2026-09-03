namespace Grandia.Sdk;

/// <summary>
/// Shop UI is opening at +0x1E93A0. Stock is the live field-params copy of
/// MDP sec[10] (+0x188 / +0x1A8 / +0x1C8): three pages of up to 16 items.
/// <see cref="ShopKind.Buy"/> shops use weapons / armor / goods.
/// <see cref="ShopKind.Magic"/> is the Mana Egg tutor (lists are usually empty).
/// Prices are the global item catalog (WINDT sec3 +4), not a per-shop table;
/// <see cref="SetPrice"/> overrides buy-gold while this shop is open.
/// Mutate the lists in place; the host writes the first 16 of each page back
/// before the buy list is built.
/// </summary>
public sealed class ShopOpenEvent
{
    public const int PageCount = 3;
    public const int SlotsPerPage = 16;
    public const int SellOverrideCap = 64;

    public ShopOpenEvent(MapId map, ShopKind kind, IEnumerable<Item>? weapons = null,
        IEnumerable<Item>? armor = null, IEnumerable<Item>? goods = null)
    {
        Map = map;
        Kind = kind;
        Weapons = CopyPage(weapons);
        Armor = CopyPage(armor);
        Goods = CopyPage(goods);
    }

    public MapId Map { get; }

    public ShopKind Kind { get; }

    /// <summary>Page 0 (vanilla weapons / tools).</summary>
    public List<Item> Weapons { get; }

    /// <summary>Page 1 (vanilla armor / accessories).</summary>
    public List<Item> Armor { get; }

    /// <summary>Page 2 (vanilla consumables / goods).</summary>
    public List<Item> Goods { get; }

    /// <summary>
    /// Buy gold for items in this shop, seeded from the global WINDT catalog
    /// (sec3 +4). Mutate a value to override the price while this shop is
    /// open. Same item id shares one catalog cost, so a change applies to
    /// every page that sells it. Restored on the next shop or map load.
    /// </summary>
    public Dictionary<Item, int> Prices { get; } = [];

    public List<Item> Page(int index) => index switch
    {
        0 => Weapons,
        1 => Armor,
        2 => Goods,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public int GetPrice(Item item) => Prices.TryGetValue(item, out var gold) ? gold : 0;

    public void SetPrice(Item item, int gold) => Prices[item] = gold < 0 ? 0 : gold;

    /// <summary>
    /// Sell gold for bag items. Empty until a mod sets a value; otherwise
    /// the catalog <see cref="ItemEvent.SellPrice"/> (vanilla Cost/2) is
    /// used. Does not change inventory. Cleared on map load.
    /// </summary>
    public Dictionary<Item, int> SellPrices { get; } = [];

    public int GetSellPrice(Item item) => SellPrices.TryGetValue(item, out var gold) ? gold : 0;

    public void SetSellPrice(Item item, int gold) => SellPrices[item] = gold < 0 ? 0 : gold;

    private static List<Item> CopyPage(IEnumerable<Item>? src)
    {
        var list = new List<Item>(SlotsPerPage);
        if (src is null)
        {
            return list;
        }

        foreach (var item in src)
        {
            if (list.Count >= SlotsPerPage)
            {
                break;
            }

            list.Add(item);
        }

        return list;
    }
}
