namespace Grandia.Sdk;

public sealed class MapLoadEvent
{
    public MapLoadEvent(MapId from, MapId to, int spawn, Map map)
    {
        From = from;
        To = to;
        Spawn = spawn;
        Map = map;
    }

    public MapId From { get; }

    public MapId To { get; }

    public int Spawn { get; }

    public Map Map { get; }
}
