namespace Grandia.Sdk;

public sealed class HookTable
{
    private readonly Dictionary<int, Hook> _hooks = [];
    private readonly HashSet<int> _occupied = [];

    internal Action? Ensure { get; set; }

    public IReadOnlyCollection<Hook> Items
    {
        get
        {
            Ensure?.Invoke();
            return _hooks.Values;
        }
    }

    public IReadOnlyCollection<int> Occupied
    {
        get
        {
            Ensure?.Invoke();
            return _occupied;
        }
    }

    public bool Dirty => _hooks.Values.Any(h => h.Dirty);

    public Hook? Get(int id)
    {
        Ensure?.Invoke();
        return _hooks.TryGetValue(id, out var hook) ? hook : null;
    }

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
        Ensure?.Invoke();
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
        Ensure?.Invoke();
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
        Ensure?.Invoke();
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

    internal void Hydrate(IEnumerable<Hook> stock)
    {
        Ensure = null;
        foreach (var hook in stock)
        {
            if (hook.Id is < 0 or > 255)
            {
                continue;
            }

            if (_hooks.TryGetValue(hook.Id, out var existing) && existing.Dirty)
            {
                continue;
            }

            _hooks[hook.Id] = hook;
            if (hook.Id > 0)
            {
                _occupied.Add(hook.Id);
            }
        }
    }
}
