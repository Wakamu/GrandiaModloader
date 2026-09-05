namespace Grandia.Sdk;

public sealed class ZoneTable
{
    private readonly List<Zone> _zones = [];

    internal Action? Ensure { get; set; }

    public IReadOnlyList<Zone> Items
    {
        get
        {
            Ensure?.Invoke();
            return _zones;
        }
    }

    public bool Dirty => _zones.Any(z => z.Dirty);

    public Zone? GetByDest(int dest)
    {
        Ensure?.Invoke();
        return _zones.FirstOrDefault(z => z.Dest == dest);
    }

    public Zone? Get(int index)
    {
        Ensure?.Invoke();
        return index >= 0 && index < _zones.Count ? _zones[index] : null;
    }

    public Zone Add(int dest)
    {
        Ensure?.Invoke();
        var zone = new Zone(dest) { Dirty = true, Append = true, Index = _zones.Count };
        _zones.Add(zone);
        return zone;
    }

    public Zone Replace(int dest)
    {
        Ensure?.Invoke();
        var zone = GetByDest(dest);
        if (zone is null)
        {
            zone = new Zone(dest) { Index = _zones.Count };
            _zones.Add(zone);
        }

        zone.Dirty = true;
        zone.Append = false;
        zone.Removed = false;
        return zone;
    }

    public void RemoveAt(int index)
    {
        var zone = Get(index);
        zone?.Remove();
    }

    internal void Hydrate(IEnumerable<Zone> stock)
    {
        Ensure = null;
        var keep = _zones.Where(z => z.Dirty).ToList();
        _zones.Clear();
        _zones.AddRange(stock);
        foreach (var zone in keep)
        {
            zone.Index = _zones.Count;
            _zones.Add(zone);
        }
    }
}
