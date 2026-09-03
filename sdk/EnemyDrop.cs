namespace Grandia.Sdk;

/// <summary>
/// One victory drop slot. <see cref="Item.None"/> means empty.
/// </summary>
public sealed class EnemyDrop
{
    public EnemyDrop()
    {
    }

    public EnemyDrop(Item item, int rate)
    {
        Item = item;
        Rate = rate;
    }

    /// <summary>Item id at actor+0x182 / +0x184 (model +0x14 / +0x16).</summary>
    public Item Item { get; set; }

    /// <summary>Drop chance 0–100 at actor+0x187 / +0x188 (model +0x18 / +0x19).</summary>
    public int Rate { get; set; }
}
