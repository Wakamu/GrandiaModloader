namespace Grandia.Sdk;

/// <summary>
/// Field chest gold add at +0x7612E (<c>add [eax+4], edx</c>). Set
/// <see cref="Amount"/> to 0 to suppress vanilla gold.
/// </summary>
public sealed class FieldGoldAddEvent
{
    public FieldGoldAddEvent(uint eventId, int amount)
    {
        EventId = eventId;
        OriginalAmount = amount;
        Amount = amount;
    }

    /// <summary>Last loot flag event id, or 0 if none is pending.</summary>
    public uint EventId { get; }

    public int OriginalAmount { get; }

    public int Amount { get; set; }
}
