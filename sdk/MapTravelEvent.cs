namespace Grandia.Sdk;

public enum MapTravelKind
{
    /// <summary>Setup dest / door AABB (handler +0x72EC0).</summary>
    Field = 0,

    /// <summary>World-map confirm body (after <see cref="IMod.OnWorldMapConfirm"/>).</summary>
    WorldMap = 1,

    /// <summary>Title / other +0x614D0 callers.</summary>
    Other = 2,
}

/// <summary>
/// Field doors / <c>setup dest=</c> fire <b>before</b> the auto-walk so
/// <see cref="Allow"/> = false keeps control. World-map confirm still
/// commits at <c>+0x614D0</c> (cancel that with
/// <see cref="IMod.OnWorldMapConfirm"/>).
/// Change <see cref="Destination"/> / <see cref="Spawn"/>, or set
/// <see cref="Allow"/> to false to stay on this map.
/// </summary>
public sealed class MapTravelEvent
{
    public MapTravelEvent(MapId from, MapId destination, int spawn, MapTravelKind kind)
    {
        From = from;
        Destination = destination;
        Spawn = spawn;
        Kind = kind;
        Allow = true;
    }

    public MapId From { get; }

    public MapId Destination { get; set; }

    /// <summary>Setup <c>spawn=</c> / dest-load key written to <c>[0x71CD48]</c>.</summary>
    public int Spawn { get; set; }

    public MapTravelKind Kind { get; }

    public bool Allow { get; set; }
}
