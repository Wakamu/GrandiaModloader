namespace Grandia.Sdk;

/// <summary>
/// Status / stash / item-assign is about to fill its party cache.
/// Start value is the field roster (MapObj+0x0A, or the pre-fight snapshot
/// while a battle is staged). Mutate <see cref="Party"/> in place
/// (slots 0..3, 1 Justin / 2 Feena / 3 Sue … 8 Liete, 0=empty).
/// Prefer <see cref="PartyRoster.Set"/> so a hole in the middle cannot drop later members.
/// The host never writes field MapObj+0x0A from this event.
/// </summary>
public sealed class MenuOpenEvent
{
    public MenuOpenEvent(MenuKind which, int[] party)
    {
        Which = which;
        Party = new PartyRoster(party);
    }

    public MenuKind Which { get; }

    public PartyRoster Party { get; }
}
