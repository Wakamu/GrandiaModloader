namespace Grandia.Sdk;

public sealed class ZoneTable
{
    private readonly List<Zone> _zones = [];

    public IReadOnlyList<Zone> Items => _zones;

    public bool Dirty => _zones.Any(z => z.Dirty);

    public Zone? GetByDest(int dest) => _zones.FirstOrDefault(z => z.Dest == dest);

    public Zone Add(int dest)
    {
        var zone = new Zone(dest) { Dirty = true, Append = true };
        _zones.Add(zone);
        return zone;
    }

    public Zone Replace(int dest)
    {
        var zone = GetByDest(dest);
        if (zone is null)
        {
            zone = new Zone(dest);
            _zones.Add(zone);
        }

        zone.Dirty = true;
        zone.Append = false;
        return zone;
    }
}
