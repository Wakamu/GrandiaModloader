namespace Grandia.Sdk;

/// <summary>
/// One PS1 UV cell from MDP sec[32]. <see cref="U"/>/<see cref="V"/>/
/// <see cref="Width"/>/<see cref="Height"/> are texels inside the tpage;
/// <see cref="Tpage"/> is the extra word (X = bits 0–3 × 64, Y = 256 if bit 4).
/// </summary>
public readonly record struct MapSpriteUv(int Block, int U, int V, int Width, int Height, int Tpage, int Clut = 0)
{
    public int PageX => (Tpage & 0xF) << 6;

    public int PageY => (Tpage & 0x10) << 4;

    public int Bpp => (Tpage & 0x180) == 0 ? 4 : 2;

    public int VramX => PageX + U / Math.Max(Bpp, 1);

    public int VramY => PageY + V;

    public override string ToString() =>
        $"b{Block} uv={U},{V} {Width}x{Height} tpage=0x{Tpage:X}";
}

/// <summary>
/// One SoftHD spriteinfo row: HD atlas crop + the key they stored for
/// the original PS1 sprite (FNV of TIM texels on anim v1; packed UV
/// on tenants / maps v4). <see cref="Source"/> is the unpacked UV when
/// the key is a v4 pack.
/// </summary>
public sealed class MapSprite
{
    public MapSprite(int index, HdAssetKind kind, HdSpriteRect atlas, ulong key, MapSpriteUv? source = null)
    {
        Index = index;
        Kind = kind;
        Atlas = atlas;
        Key = key;
        Source = source;
    }

    public int Index { get; }

    public HdAssetKind Kind { get; }

    /// <summary>HD atlas xywh (<c>00xx.png</c> crop).</summary>
    public HdSpriteRect Atlas { get; }

    /// <summary>Spriteinfo footer key (sign-extended FNV or UV pack).</summary>
    public ulong Key { get; }

    /// <summary>Original PS1 UV when the key unpacks as SoftHD <c>+0x2EF10</c>.</summary>
    public MapSpriteUv? Source { get; }

    public override string ToString() =>
        $"[{Index:D4}] {Kind} {Atlas.Width}x{Atlas.Height} key={Key:X16}";
}

/// <summary>
/// SoftHD <c>{stem}_{kind}__spriteinfo.bin</c> catalog for one kind.
/// Read-only. Empty when the sidecar is missing.
/// </summary>
public sealed class MapSpriteSheet
{
    private readonly List<MapSprite> _items;

    public MapSpriteSheet(HdAssetKind kind, IEnumerable<MapSprite>? items = null)
    {
        Kind = kind;
        _items = items?.ToList() ?? [];
    }

    public HdAssetKind Kind { get; }

    public IReadOnlyList<MapSprite> Items => _items;

    public MapSprite? this[int index] =>
        index >= 0 && index < _items.Count ? _items[index] : null;

    public int Count => _items.Count;

    internal void Replace(IEnumerable<MapSprite> stock)
    {
        _items.Clear();
        _items.AddRange(stock);
    }

    public override string ToString() => $"{Kind} sprites={_items.Count}";
}

/// <summary>
/// This map's original + SoftHD sprite catalogs. <see cref="Uv"/> is
/// MDP sec[32] (PS1 cells). <see cref="Anim"/> / <see cref="Tenants"/> /
/// <see cref="Maps"/> / <see cref="MapEff"/> are the FIELD sidecars
/// SoftHD opens. Same lazy fopen hydrate as <see cref="Map.Zones"/>.
/// Read-only — does not mark <see cref="Map.Dirty"/>.
/// </summary>
public sealed class MapSpriteBank
{
    private readonly List<MapSpriteUv> _uv = [];
    private readonly Dictionary<HdAssetKind, MapSpriteSheet> _sheets = new()
    {
        [HdAssetKind.Anim] = new MapSpriteSheet(HdAssetKind.Anim),
        [HdAssetKind.Tenants] = new MapSpriteSheet(HdAssetKind.Tenants),
        [HdAssetKind.Maps] = new MapSpriteSheet(HdAssetKind.Maps),
        [HdAssetKind.MapEff] = new MapSpriteSheet(HdAssetKind.MapEff),
    };

    internal Action? Ensure { get; set; }

    /// <summary>sec[32] UV cells (block 0 then block 1).</summary>
    public IReadOnlyList<MapSpriteUv> Uv
    {
        get
        {
            Ensure?.Invoke();
            return _uv;
        }
    }

    public MapSpriteSheet Anim => Sheet(HdAssetKind.Anim);

    public MapSpriteSheet Tenants => Sheet(HdAssetKind.Tenants);

    public MapSpriteSheet Maps => Sheet(HdAssetKind.Maps);

    public MapSpriteSheet MapEff => Sheet(HdAssetKind.MapEff);

    public MapSpriteSheet this[HdAssetKind kind] => Sheet(kind);

    public MapSpriteSheet Sheet(HdAssetKind kind)
    {
        Ensure?.Invoke();
        if (_sheets.TryGetValue(kind, out var sheet))
        {
            return sheet;
        }

        return new MapSpriteSheet(kind);
    }

    internal void Hydrate(IEnumerable<MapSpriteUv> uv, IEnumerable<MapSpriteSheet> sheets)
    {
        Ensure = null;
        _uv.Clear();
        _uv.AddRange(uv);
        foreach (var sheet in _sheets.Values)
        {
            sheet.Replace([]);
        }

        foreach (var sheet in sheets)
        {
            if (_sheets.TryGetValue(sheet.Kind, out var dest))
            {
                dest.Replace(sheet.Items);
            }
            else
            {
                _sheets[sheet.Kind] = new MapSpriteSheet(sheet.Kind, sheet.Items);
            }
        }
    }

    public static MapSpriteSheet FromSpriteInfo(HdAssetKind kind, ReadOnlySpan<byte> blob)
    {
        var rows = HdSpriteKey.ParseLookup(blob);
        var items = new MapSprite[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            MapSpriteUv? source = null;
            if (HdSpriteKey.TryUnpackUv(row.Key, out var u, out var v, out var w, out var h, out var tpage, out var clut))
            {
                source = new MapSpriteUv(-1, u, v, w, h, tpage, clut);
            }

            items[i] = new MapSprite(row.Index, kind, row.Rect, row.Key, source);
        }

        return new MapSpriteSheet(kind, items);
    }
}
