namespace Grandia.Sdk;

/// <summary>
/// One authored sec[29] emitter. Hydrated on <c>OnMapLoad</c> with
/// <see cref="Map.Zones"/> — this is the MDP row, not the live mixer
/// (<see cref="Game.FieldSfx"/>). Setters mark the map dirty so the host
/// recopies the table onto the heap before mixer bind.
/// </summary>
public sealed class MapSfx
{
    private int _id;
    private int _sfx;
    private int _flags;
    private int _kind;
    private int _period;
    private int _bias;
    private int _x;
    private int _y;
    private int _z;

    public MapSfx(int index, int id, int sfx, int flags, int x, int y, int z,
        int kind = 0, int period = 0, int bias = 0)
    {
        Index = index;
        _id = ClampId(id);
        _sfx = ClampByte(sfx);
        _flags = ClampByte(flags);
        _kind = ClampByte(kind);
        _period = ClampByte(period);
        _bias = ClampByte(bias);
        _x = ClampCoord(x);
        _y = ClampCoord(y);
        _z = ClampCoord(z);
    }

    public int Index { get; internal set; }

    /// <summary>Record id in the table (not the catalog sample). 0–254.</summary>
    public int Id
    {
        get => _id;
        set => Set(ref _id, ClampId(value));
    }

    /// <summary>SFX catalog id (Gumbo frogs = 16, river/birds family 6–9).</summary>
    public int Sfx
    {
        get => _sfx;
        set => Set(ref _sfx, ClampByte(value));
    }

    public int Flags
    {
        get => _flags;
        set => Set(ref _flags, ClampByte(value));
    }

    /// <summary>Byte +2. High bit set = mixer will play (held / loop family).</summary>
    public int Kind
    {
        get => _kind;
        set => Set(ref _kind, ClampByte(value));
    }

    public int Period
    {
        get => _period;
        set => Set(ref _period, ClampByte(value));
    }

    public int Bias
    {
        get => _bias;
        set => Set(ref _bias, ClampByte(value));
    }

    public bool Looping
    {
        get => (_flags & 0x40) != 0;
        set => Flags = value ? (_flags | 0x40) : (_flags & ~0x40);
    }

    public int X
    {
        get => _x;
        set => Set(ref _x, ClampCoord(value));
    }

    public int Y
    {
        get => _y;
        set => Set(ref _y, ClampCoord(value));
    }

    public int Z
    {
        get => _z;
        set => Set(ref _z, ClampCoord(value));
    }

    public WalkPos Position
    {
        get => new(_x, _y, _z);
        set
        {
            X = value.X;
            Y = value.Y;
            Z = value.Z;
        }
    }

    public bool Dirty { get; set; }

    public bool Append { get; set; }

    /// <summary>Drop this row on the next sec[29] emit.</summary>
    public bool Removed { get; set; }

    public void Remove()
    {
        Removed = true;
        Dirty = true;
    }

    public override string ToString()
    {
        var loop = Looping ? " loop" : "";
        var gone = Removed ? " removed" : "";
        return $"[{Index}] id={Id} sfx={Sfx}{loop}{gone} ({X},{Y},{Z})";
    }

    internal static MapSfx FromRaw(int index, byte[] raw)
    {
        if (raw.Length < MdpSec29.RowSize)
        {
            return new MapSfx(index, 0, 0, 0, 0, 0, 0);
        }

        return new MapSfx(
            index,
            raw[0],
            raw[3],
            raw[4],
            (short)(raw[0xA] | (raw[0xB] << 8)),
            (short)(raw[0xC] | (raw[0xD] << 8)),
            (short)(raw[0xE] | (raw[0xF] << 8)),
            kind: raw[2],
            period: raw[5],
            bias: raw[7]);
    }

    private void Set(ref int field, int value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        Dirty = true;
    }

    internal static int ClampByte(int value) =>
        value < 0 ? 0 : value > 255 ? 255 : value;

    internal static int ClampId(int value) =>
        value < 0 ? 0 : value > 0xFE ? 0xFE : value;

    internal static int ClampCoord(int value) =>
        value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : value;
}

/// <summary>sec[29] emitters on this map. Same lazy hydrate as <see cref="Map.Zones"/>.</summary>
public sealed class MapSfxTable
{
    private readonly List<MapSfx> _items;
    private int _flags;
    private int _range;
    private bool _headerDirty;

    public MapSfxTable()
        : this(0, 0, [])
    {
    }

    public MapSfxTable(int flags, int range, IEnumerable<MapSfx> items)
    {
        _flags = flags;
        _range = range < 0 ? 0 : range;
        _items = items.ToList();
    }

    internal Action? Ensure { get; set; }

    public int Flags
    {
        get
        {
            Ensure?.Invoke();
            return _flags;
        }
        set
        {
            Ensure?.Invoke();
            if (_flags == value)
            {
                return;
            }

            _flags = value;
            _headerDirty = true;
        }
    }

    /// <summary>Map-wide hear-distance (Gumbo / Marna / Parm = 896). One value for every row.</summary>
    public int Range
    {
        get
        {
            Ensure?.Invoke();
            return _range;
        }
        set
        {
            Ensure?.Invoke();
            var next = value < 0 ? 0 : value;
            if (_range == next)
            {
                return;
            }

            _range = next;
            _headerDirty = true;
        }
    }

    public bool Dirty => _headerDirty || _items.Any(e => e.Dirty);

    public IReadOnlyList<MapSfx> Items
    {
        get
        {
            Ensure?.Invoke();
            return _items;
        }
    }

    public MapSfx? this[int index] => Get(index);

    public MapSfx? Get(int index)
    {
        Ensure?.Invoke();
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    public IReadOnlyList<MapSfx> OfSfx(int sfxId)
    {
        Ensure?.Invoke();
        return _items.Where(e => !e.Removed && e.Sfx == sfxId).ToList();
    }

    /// <summary>
    /// Append an emitter. <paramref name="looping"/> sets flags <c>0x60</c> /
    /// period 12; otherwise held <c>0x80</c>. Heap copy holds at most
    /// <see cref="MdpSec29.MaxLive"/> live rows.
    /// </summary>
    public MapSfx Add(int sfx, int x, int y, int z, bool looping = true)
    {
        Ensure?.Invoke();
        var flags = looping ? 0x60 : 0x80;
        var period = looping ? 12 : 0;
        var row = new MapSfx(_items.Count, NextId(), sfx, flags, x, y, z, kind: 0x81, period, 0)
        {
            Dirty = true,
            Append = true,
        };
        _items.Add(row);
        return row;
    }

    public void Remove(MapSfx row) => row.Remove();

    public void RemoveAt(int index) => Get(index)?.Remove();

    internal int NextId()
    {
        var used = _items.Where(e => !e.Removed).Select(e => e.Id).ToHashSet();
        for (var i = 0; i < 0xFF; i++)
        {
            if (i == 0xF4 || used.Contains(i))
            {
                continue;
            }

            return i;
        }

        return 0;
    }

    internal void Hydrate(int flags, int range, IEnumerable<MapSfx> stock)
    {
        Ensure = null;
        var keep = _items.Where(e => e.Dirty).ToList();
        if (!_headerDirty)
        {
            _flags = flags;
            _range = range < 0 ? 0 : range;
        }

        _items.Clear();
        _items.AddRange(stock);
        foreach (var row in keep)
        {
            row.Index = _items.Count;
            _items.Add(row);
        }
    }
}
