namespace Grandia.Sdk;

/// <summary>
/// World-map destination is about to commit (before fopen / OnMapLoad).
/// Set <see cref="Allow"/> to false to cancel (clears the confirm FSM).
/// </summary>
public sealed class WorldMapConfirmEvent
{
    public WorldMapConfirmEvent(MapId destination, bool allow = true)
    {
        Destination = destination;
        Allow = allow;
    }

    public MapId Destination { get; }

    public bool Allow { get; set; }
}
