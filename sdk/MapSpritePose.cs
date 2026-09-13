namespace Grandia.Sdk;

/// <summary>
/// One 16-byte sec[23] part record. A pose is a list of these; the tick
/// installs each piece onto a character layer. <see cref="SpriteIndex"/>
/// indexes the 8-byte cookie bank at header <c>+0x30</c>.
/// </summary>
public sealed class MapSpritePart
{
    public MapSpritePart(
        int width,
        int height,
        int offsetX,
        int offsetY,
        int offsetZ,
        int extra,
        int tpage,
        int channel,
        int spriteIndex,
        byte[]? cookie = null,
        byte[]? raw = null)
    {
        Width = width & 0xFF;
        Height = height & 0xFF;
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
        Extra = extra;
        Tpage = tpage & 0xFFFF;
        Channel = channel & 0xFFFF;
        SpriteIndex = spriteIndex & 0xFFFF;
        Cookie = cookie is { Length: > 0 } ? cookie.ToArray() : [];
        Raw = raw is { Length: > 0 } ? raw.ToArray() : [];
    }

    /// <summary>Byte +0. Tick footprint uses <c>Width * Height &lt;&lt; 2</c>.</summary>
    public int Width { get; }

    /// <summary>Byte +1.</summary>
    public int Height { get; }

    /// <summary>s16 +2 — placement on the character.</summary>
    public int OffsetX { get; }

    /// <summary>s16 +4.</summary>
    public int OffsetY { get; }

    /// <summary>s16 +6.</summary>
    public int OffsetZ { get; }

    /// <summary>s16 +8 — extra offset / extent.</summary>
    public int Extra { get; }

    /// <summary>u16 +0xA — tpage / CLUT (BA38 cinematic parts are <c>0x8808</c>).</summary>
    public int Tpage { get; }

    /// <summary>u16 +0xC — layer / part channel (0–3 on BA38).</summary>
    public int Channel { get; }

    /// <summary>u16 +0xE — index into the 8-byte sprite-cookie bank.</summary>
    public int SpriteIndex { get; }

    /// <summary>8-byte GPU / UV cookie at <see cref="SpriteIndex"/>, or empty if OOB.</summary>
    public IReadOnlyList<byte> Cookie { get; }

    /// <summary>SPRT <c>u</c> — first cookie byte. SoftHD hashes VRAM at this UV, not the cookie.</summary>
    public int UvU => Cookie.Count > 0 ? Cookie[0] : 0;

    /// <summary>SPRT <c>v</c> — second cookie byte.</summary>
    public int UvV => Cookie.Count > 1 ? Cookie[1] : 0;

    /// <summary>The raw 16-byte part record.</summary>
    public IReadOnlyList<byte> Raw { get; }

    public override string ToString() =>
        $"ch={Channel} spr=0x{SpriteIndex:X} {Width}x{Height} @ {OffsetX},{OffsetY},{OffsetZ}";
}

/// <summary>
/// One sec[23] pose. <see cref="Index"/> is the u16 the clip frame bank
/// stores — not a packed part+sprite. Most poses are one part; some maps
/// use up to 8 pieces.
/// </summary>
public sealed class MapSpritePose
{
    public MapSpritePose(int index, IReadOnlyList<MapSpritePart>? parts = null)
    {
        Index = index;
        Parts = parts is { Count: > 0 } ? parts.ToList() : [];
    }

    /// <summary>Pose index used by <see cref="MapSpriteClipFrame.Pose"/>.</summary>
    public int Index { get; }

    public IReadOnlyList<MapSpritePart> Parts { get; }

    public override string ToString() =>
        $"pose {Index} parts={Parts.Count}";
}

/// <summary>
/// This map's sec[23] pose directory (header <c>+0x28</c>). Same lazy
/// fopen hydrate as <see cref="Map.Zones"/>. Indexer is the pose index
/// the clip frame names, not a compacted list. Read-only.
/// </summary>
public sealed class MapSpritePoseTable
{
    private readonly List<MapSpritePose> _items;

    public MapSpritePoseTable()
        : this([])
    {
    }

    public MapSpritePoseTable(IEnumerable<MapSpritePose> items) =>
        _items = items.ToList();

    internal Action? Ensure { get; set; }

    public IReadOnlyList<MapSpritePose> Items
    {
        get
        {
            Ensure?.Invoke();
            return _items;
        }
    }

    public MapSpritePose? this[int index] => Get(index);

    public MapSpritePose? Get(int index)
    {
        Ensure?.Invoke();
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    internal void Hydrate(IEnumerable<MapSpritePose> stock)
    {
        Ensure = null;
        _items.Clear();
        _items.AddRange(stock);
    }
}
