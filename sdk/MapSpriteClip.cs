namespace Grandia.Sdk;

/// <summary>
/// One timed pose in a sec[23] sprite clip. <see cref="Pose"/> is an
/// index into <see cref="Map.Poses"/>; <see cref="Delay"/> is the
/// countdown the tick at <c>+0x79180</c> waits before the next frame.
/// </summary>
public readonly record struct MapSpriteClipFrame(int Pose, int Delay);

/// <summary>
/// One sec[23] clip directory row. <see cref="Id"/> is the number
/// <c>unit_bind {id} …</c> looks up (hook <c>+5</c>), not a 0-based
/// directory slot. This is the character sprite-clip bank, not
/// <see cref="Map.Anims"/> (sec[21] xyz polylines). Read-only for now.
/// </summary>
public sealed class MapSpriteClip
{
    public MapSpriteClip(int index, int id, IReadOnlyList<MapSpriteClipFrame>? frames = null)
    {
        Index = index;
        Id = id & 0xFFFF;
        Frames = frames is { Count: > 0 } ? frames.ToList() : [];
    }

    /// <summary>Directory slot (0-based). BA38 is not sorted by <see cref="Id"/>.</summary>
    public int Index { get; }

    /// <summary>Clip id <c>unit_bind</c> x / hook <c>+5</c> looks up.</summary>
    public int Id { get; }

    public IReadOnlyList<MapSpriteClipFrame> Frames { get; }

    public override string ToString() =>
        $"[{Index}] id={Id} frames={Frames.Count}";
}

/// <summary>
/// This map's sec[23] sprite-clip directory. Same lazy fopen hydrate as
/// <see cref="Map.Zones"/>. Ids missing here are not on this map (many
/// maps have no sec[23]). Read-only — does not mark <see cref="Map.Dirty"/>.
/// </summary>
public sealed class MapSpriteClipTable
{
    private readonly List<MapSpriteClip> _items;

    public MapSpriteClipTable()
        : this([])
    {
    }

    public MapSpriteClipTable(IEnumerable<MapSpriteClip> items) =>
        _items = items.ToList();

    internal Action? Ensure { get; set; }

    public IReadOnlyList<MapSpriteClip> Items
    {
        get
        {
            Ensure?.Invoke();
            return _items;
        }
    }

    public MapSpriteClip? this[int index] => Get(index);

    public MapSpriteClip? Get(int index)
    {
        Ensure?.Invoke();
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    /// <summary>First clip with this directory id, or null.</summary>
    public MapSpriteClip? OfId(int id)
    {
        Ensure?.Invoke();
        return _items.FirstOrDefault(e => e.Id == id);
    }

    internal void Hydrate(IEnumerable<MapSpriteClip> stock)
    {
        Ensure = null;
        _items.Clear();
        _items.AddRange(stock);
    }
}
