namespace Grandia.Sdk;

/// <summary>
/// Which shop UI +0x1E93A0 opened (<c>dl</c>).
/// <see cref="Buy"/> is an item shop (weapons / armor / goods).
/// <see cref="Sell"/> is the sell screen (party inventory; catalog
/// <see cref="ItemEvent.SellPrice"/>, vanilla Cost/2).
/// <see cref="Magic"/> is a Mana Egg shop (learn magic; no item stock).
/// Stash/Get are the other <c>shop_win_mes1</c> screens and are unconfirmed.
/// </summary>
public enum ShopKind
{
    Unknown = 0,
    Buy = 1,
    Sell = 2,
    Stash = 3,
    Get = 4,
    Magic = 5,
}
