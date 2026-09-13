namespace Grandia.Sdk;

/// <summary>
/// One of the three typed stat lines on <see cref="ItemEvent.Stats"/>.
/// Kind is +16 / +17 / +18; Value is the high byte of +19 / +21 / +23.
/// The low extra byte (vanilla Strength keeps 9) is left alone.
/// </summary>
public sealed class ItemStatBonus
{
    private readonly ItemEvent _item;
    private readonly int _slot;

    internal ItemStatBonus(ItemEvent item, int slot)
    {
        _item = item;
        _slot = slot;
    }

    public ItemStat Kind
    {
        get => (ItemStat)_item.GetStatKind(_slot);
        set => _item.SetStatKind(_slot, (int)value);
    }

    /// <summary>
    /// Signed magnitude (Vitality −40, Wit +30).
    /// <see cref="ItemStat.AttackRange"/> is 0–255 (Telescope 128).
    /// </summary>
    public int Value
    {
        get
        {
            var raw = (_item.GetStatPacked(_slot) >> 8) & 0xFF;
            return Kind == ItemStat.AttackRange ? raw : unchecked((sbyte)raw);
        }
        set => _item.SetStatPacked(_slot, ItemEvent.PackStatHigh(_item.GetStatPacked(_slot), value));
    }
}

/// <summary>
/// Sheet Auto Effect + Auto Par2. Kind / Value are record +13 / +14;
/// <see cref="Param"/> is +15.
/// </summary>
public sealed class ItemAutoEffect
{
    private readonly ItemEvent _item;

    internal ItemAutoEffect(ItemEvent item) => _item = item;

    public ItemAuto Kind
    {
        get => (ItemAuto)_item.Unknown13;
        set => _item.Unknown13 = (int)value;
    }

    /// <summary>Chance or magnitude (Shocking Knife 33, Binding Whip 160).</summary>
    public int Value
    {
        get => _item.Unknown14;
        set => _item.Unknown14 = value;
    }

    /// <summary>Sheet Auto Par2 (often 0; Shocking Knife 2, Instant Death 7 / 1).</summary>
    public int Param
    {
        get => _item.Para1Pre;
        set => _item.Para1Pre = value;
    }
}
