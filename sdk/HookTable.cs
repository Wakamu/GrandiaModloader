namespace Grandia.Sdk;

public sealed class HookTable
{
    private readonly Dictionary<int, Hook> _hooks = [];
    private readonly HashSet<int> _occupied = [];

    public IReadOnlyCollection<Hook> Items => _hooks.Values;

    public IReadOnlyCollection<int> Occupied => _occupied;

    public bool Dirty => _hooks.Values.Any(h => h.Dirty);

    public Hook? Get(int id) => _hooks.TryGetValue(id, out var hook) ? hook : null;

    public void SeedOccupied(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            if (id is > 0 and <= 255)
            {
                _occupied.Add(id);
            }
        }
    }

    /// <summary>First unused table-2 id in 1..255 (vanilla rows + hooks added this load).</summary>
    public int NextId()
    {
        for (var i = 1; i <= 255; i++)
        {
            if (!_occupied.Contains(i))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no free table-2 hook id (1–255 are all used)");
    }

    public Hook Add() => Add(NextId());

    public Hook Add(int id)
    {
        if (id is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "hook id must be 1–255");
        }

        if (!_hooks.TryGetValue(id, out var hook))
        {
            hook = new Hook(id);
            _hooks[id] = hook;
        }

        hook.Dirty = true;
        hook.Append = true;
        _occupied.Add(id);
        return hook;
    }

    public Hook Replace(int id)
    {
        if (!_hooks.TryGetValue(id, out var hook))
        {
            hook = new Hook(id);
            _hooks[id] = hook;
        }

        hook.Dirty = true;
        hook.Append = false;
        _occupied.Add(id);
        return hook;
    }
}
