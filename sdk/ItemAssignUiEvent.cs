namespace Grandia.Sdk;

/// <summary>
/// Field loot is about to open the item-assign UI (+0x1DC100, return +0x61E0D).
/// Set <see cref="SkipVanilla"/> to return without granting the chest item.
/// </summary>
public sealed class ItemAssignUiEvent
{
    public ItemAssignUiEvent(uint eventId, uint returnRva, bool skipVanilla)
    {
        EventId = eventId;
        ReturnRva = returnRva;
        SkipVanilla = skipVanilla;
    }

    /// <summary>Last loot flag event id, or 0 if none is pending.</summary>
    public uint EventId { get; }

    public uint ReturnRva { get; }

    public bool SkipVanilla { get; set; }
}
