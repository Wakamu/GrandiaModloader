namespace Grandia.Sdk;

/// <summary>
/// A progress-flag write at grandia.exe+0x70505. Mutate in place.
/// Set <see cref="SuppressLootUi"/> / <see cref="SuppressGold"/> to arm the
/// following assign-UI and field-gold intercepts for this loot event.
/// </summary>
public sealed class EventFlagEvent
{
    public EventFlagEvent(uint eventId, uint callerRva, EventFlagKind kind, uint mask, uint flagOffset)
    {
        EventId = eventId;
        CallerRva = callerRva;
        Kind = kind;
        Mask = mask;
        FlagOffset = flagOffset;
    }

    public uint EventId { get; }

    public uint CallerRva { get; }

    public EventFlagKind Kind { get; }

    public uint Mask { get; }

    public uint FlagOffset { get; }

    /// <summary>Skip the vanilla "who gets this item" UI for this loot flag.</summary>
    public bool SuppressLootUi { get; set; }

    /// <summary>Zero the following field-chest gold add for this loot flag.</summary>
    public bool SuppressGold { get; set; }
}
