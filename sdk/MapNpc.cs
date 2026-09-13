namespace Grandia.Sdk;

/// <summary>
/// XZ rectangle at sec[8] <c>+0x12..+0x18</c> as <c>(xmin, z_hi, xmax, z_lo)</c>.
/// Kind 4: wander yard the NPC is allowed to walk (Parm talk 9 is
/// <c>(676,820)-(766,514)</c>). Kind 0: often a small talk box around the
/// feet, or all-zero. Kind 0 never runs the wander tick.
/// </summary>
public readonly record struct TalkBox(short XMin, short Z0, short XMax, short Z1)
{
    public override string ToString() => $"{XMin},{Z0},{XMax},{Z1}";
}

/// <summary>
/// One sec[8] field instance. <see cref="Map.Npcs"/> lists town talkers
/// (kind 0 or 4 with a talk id). Kind-2 wanderers stay on
/// <see cref="Map.Encounters"/> and are passed through on emit.
/// Setters mark the map dirty so the host recopies the 4 KiB heap after
/// the field-setup word-copy at <c>+0x61140</c>.
/// </summary>
public sealed class MapNpc
{
    private byte[] _raw;
    private int _kind;
    private int _subId;
    private int _flags;
    private int _talkId;
    private int _x;
    private int _y;
    private int _z;
    private int _clut;
    private int _runtimeFlags;
    private TalkBox _talkBox;
    private int _innerX;
    private int _innerZ;

    public MapNpc(int index, int kind, int talkId, int x, int y, int z,
        int flags = 0x80, int subId = 0, int clut = 0, int runtimeFlags = 0,
        TalkBox talkBox = default, int innerX = 0, int innerZ = 0, byte[]? raw = null)
    {
        Index = index;
        _kind = ClampByte(kind) & 0xF;
        _subId = ClampByte(subId);
        _flags = ClampByte(flags);
        _talkId = ClampByte(talkId);
        _x = ClampCoord(x);
        _y = ClampCoord(y);
        _z = ClampCoord(z);
        _clut = ClampU16(clut);
        _runtimeFlags = ClampU16(runtimeFlags);
        _talkBox = talkBox;
        _innerX = ClampCoord(innerX);
        _innerZ = ClampCoord(innerZ);
        _raw = raw is { Length: >= MdpSec8.RowSize }
            ? raw.AsSpan(0, MdpSec8.RowSize).ToArray()
            : new byte[MdpSec8.RowSize];
    }

    /// <summary>sec[8] slot (all instances, not the town-only list index).</summary>
    public int Index { get; internal set; }

    /// <summary>
    /// Spawn kind nibble. <c>0</c> stands (facing from <see cref="Facing"/>).
    /// <c>4</c> random-walks inside <see cref="TalkBox"/> when
    /// <see cref="WalkMode"/> is 1–4. <c>2</c> is a field wanderer, not a
    /// townsfolk.
    /// </summary>
    public int Kind
    {
        get => _kind;
        set => Set(ref _kind, ClampByte(value) & 0xF);
    }

    public int SubId
    {
        get => _subId;
        set => Set(ref _subId, ClampByte(value));
    }

    /// <summary>Byte +2. High nibble is copied to the spawn slot.</summary>
    public int Flags
    {
        get => _flags;
        set => Set(ref _flags, ClampByte(value));
    }

    /// <summary>Byte +3. Matches SCN / hdr+4 body <c>100+(talk−1)</c> on towns.</summary>
    public int TalkId
    {
        get => _talkId;
        set => Set(ref _talkId, ClampByte(value));
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

    /// <summary>CLUT GPU word at +0xA (Parm often y=320). 0 on many wanderers.</summary>
    public int Clut
    {
        get => _clut;
        set => Set(ref _clut, ClampU16(value));
    }

    /// <summary>
    /// Word at +0x10. Low nibble is <see cref="Facing"/>; high byte is
    /// <see cref="WalkMode"/>. Stock kind 0 is <c>0x0000..0x0007</c>; stock
    /// kind 4 is <c>0x01xx..0x04xx</c> (Parm walkers often <c>0x0200</c>).
    /// </summary>
    public int RuntimeFlags
    {
        get => _runtimeFlags;
        set => Set(ref _runtimeFlags, ClampU16(value));
    }

    /// <summary>
    /// Low nibble of +0x10 (0–7). Kind 0 writes this to spawn
    /// <c>71AA42</c>, then camera-adjusts through LUT <c>01 02 04 07 06 05 03 00</c>
    /// into <c>71AA41</c> (which idle FT4 to show). Kind 4 does not run that
    /// copy — walk heading is rolled at runtime into <c>71AA4C</c>
    /// (<c>(rand &amp; 7)*2</c>). Stock walkers still store a nibble here;
    /// it applies when they are kind 0.
    /// </summary>
    public int Facing
    {
        get => _runtimeFlags & 0xF;
        set => RuntimeFlags = (_runtimeFlags & ~0xFF) | (ClampByte(value) & 0xF);
    }

    /// <summary>
    /// High byte of +0x10. <c>0</c> on standers. Stock walkers use 1–4
    /// (2 is the most common). Kind 4 with this still 0 does not walk.
    /// </summary>
    public int WalkMode
    {
        get => (_runtimeFlags >> 8) & 0xFF;
        set => RuntimeFlags = (_runtimeFlags & 0xFF) | (ClampByte(value) << 8);
    }

    public TalkBox TalkBox
    {
        get => _talkBox;
        set
        {
            if (_talkBox == value)
            {
                return;
            }

            _talkBox = value;
            Dirty = true;
        }
    }

    /// <summary>Inner X pad at +0x1A (usually 0).</summary>
    public int InnerX
    {
        get => _innerX;
        set => Set(ref _innerX, ClampCoord(value));
    }

    /// <summary>Inner Z pad at +0x1C (usually 0).</summary>
    public int InnerZ
    {
        get => _innerZ;
        set => Set(ref _innerZ, ClampCoord(value));
    }

    /// <summary>Kind 0 or 4 with a non-zero talk id — not a kind-2 wanderer.</summary>
    public bool IsTown => MdpSec8.IsTown(_kind, _talkId);

    public bool Dirty { get; set; }

    public bool Append { get; set; }

    /// <summary>Drop this row on the next sec[8] emit.</summary>
    public bool Removed { get; set; }

    public void Remove()
    {
        Removed = true;
        Dirty = true;
    }

    /// <summary>
    /// Kind 4 + walk mode (stock default 2) + wander rectangle. Setting
    /// <see cref="Kind"/> to 4 alone is not enough — standers keep
    /// <see cref="WalkMode"/> 0.
    /// </summary>
    public void Walk(TalkBox box, int mode = 2)
    {
        Kind = 4;
        WalkMode = mode;
        TalkBox = box;
    }

    public override string ToString()
    {
        var gone = Removed ? " removed" : "";
        var walk = WalkMode != 0 ? $" walk={WalkMode}" : "";
        return $"[{Index}] kind={Kind} talk={TalkId}{walk}{gone} ({X},{Y},{Z})";
    }

    internal static MapNpc FromRaw(int index, ReadOnlySpan<byte> raw)
    {
        if (raw.Length < MdpSec8.RowSize)
        {
            return new MapNpc(index, 0, 0, 0, 0, 0);
        }

        var rec = raw[..MdpSec8.RowSize];
        return new MapNpc(
            index,
            rec[1] & 0xF,
            rec[3],
            ReadS16(rec, 4),
            ReadS16(rec, 6),
            ReadS16(rec, 8),
            flags: rec[2],
            subId: rec[0],
            clut: ReadU16(rec, 0xA),
            runtimeFlags: ReadU16(rec, 0x10),
            talkBox: new TalkBox(
                ReadS16(rec, 0x12),
                ReadS16(rec, 0x14),
                ReadS16(rec, 0x16),
                ReadS16(rec, 0x18)),
            innerX: ReadS16(rec, 0x1A),
            innerZ: ReadS16(rec, 0x1C),
            raw: rec.ToArray());
    }

    internal MapNpc CloneAt(int index, int talk, int x, int y, int z)
    {
        var dx = (short)ClampCoord(x - _x);
        var dz = (short)ClampCoord(z - _z);
        var box = new TalkBox(
            (short)ClampCoord(_talkBox.XMin + dx),
            (short)ClampCoord(_talkBox.Z0 + dz),
            (short)ClampCoord(_talkBox.XMax + dx),
            (short)ClampCoord(_talkBox.Z1 + dz));
        return new MapNpc(index, _kind, talk, x, y, z, _flags, _subId, _clut, _runtimeFlags,
            box, _innerX, _innerZ, ToRaw())
        {
            Dirty = true,
            Append = true,
        };
    }

    internal byte[] ToRaw()
    {
        var dest = _raw.Length >= MdpSec8.RowSize
            ? _raw.ToArray()
            : new byte[MdpSec8.RowSize];
        if (dest.Length > MdpSec8.RowSize)
        {
            dest = dest.AsSpan(0, MdpSec8.RowSize).ToArray();
        }

        dest[0] = (byte)ClampByte(_subId);
        dest[1] = (byte)(ClampByte(_kind) & 0xF);
        dest[2] = (byte)ClampByte(_flags);
        dest[3] = (byte)ClampByte(_talkId);
        WriteS16(dest, 4, _x);
        WriteS16(dest, 6, _y);
        WriteS16(dest, 8, _z);
        WriteU16(dest, 0xA, _clut);
        WriteU16(dest, 0x10, _runtimeFlags);
        WriteS16(dest, 0x12, _talkBox.XMin);
        WriteS16(dest, 0x14, _talkBox.Z0);
        WriteS16(dest, 0x16, _talkBox.XMax);
        WriteS16(dest, 0x18, _talkBox.Z1);
        WriteS16(dest, 0x1A, _innerX);
        WriteS16(dest, 0x1C, _innerZ);
        return dest;
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

    internal static int ClampU16(int value) =>
        value < 0 ? 0 : value > 0xFFFF ? 0xFFFF : value;

    internal static int ClampCoord(int value) =>
        value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : value;

    private static short ReadS16(ReadOnlySpan<byte> rec, int off) =>
        (short)(rec[off] | (rec[off + 1] << 8));

    private static int ReadU16(ReadOnlySpan<byte> rec, int off) =>
        rec[off] | (rec[off + 1] << 8);

    private static void WriteS16(byte[] dest, int off, int value)
    {
        var w = (ushort)(short)ClampCoord(value);
        dest[off] = (byte)(w & 0xFF);
        dest[off + 1] = (byte)(w >> 8);
    }

    private static void WriteU16(byte[] dest, int off, int value)
    {
        var w = (ushort)ClampU16(value);
        dest[off] = (byte)(w & 0xFF);
        dest[off + 1] = (byte)(w >> 8);
    }
}

/// <summary>
/// Town talkers on this map (sec[8] kind 0/4). Same lazy hydrate as
/// <see cref="Map.Zones"/>. Kind-2 rows stay in the emit blob so wanderers
/// are not wiped.
/// </summary>
public sealed class MapNpcTable
{
    private readonly List<MapNpc> _items;

    public MapNpcTable()
        : this([])
    {
    }

    public MapNpcTable(IEnumerable<MapNpc> instances) =>
        _items = instances.ToList();

    internal Action? Ensure { get; set; }

    public bool Dirty => _items.Any(e => e.Dirty);

    /// <summary>Town talkers only (kind 0/4, talk ≠ 0).</summary>
    public IReadOnlyList<MapNpc> Items
    {
        get
        {
            Ensure?.Invoke();
            return _items.Where(e => e.IsTown).ToList();
        }
    }

    /// <summary>Every sec[8] row, including wanderers and party triggers.</summary>
    internal IReadOnlyList<MapNpc> Instances
    {
        get
        {
            Ensure?.Invoke();
            return _items;
        }
    }

    public MapNpc? this[int index] => Get(index);

    public MapNpc? Get(int index)
    {
        var items = Items;
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    public IReadOnlyList<MapNpc> OfTalk(int talkId)
    {
        Ensure?.Invoke();
        return _items.Where(e => e.IsTown && !e.Removed && e.TalkId == talkId).ToList();
    }

    /// <summary>
    /// Append a kind-0 talker. Clones flags / CLUT / box from an existing
    /// talker with the same id (else the first town NPC). A new talk id
    /// without an hdr+4 body does not draw a townsfolk — reuse a talk id
    /// that already has a mesh. Heap copy holds at most
    /// <see cref="MdpSec8.MaxLive"/> live rows.
    /// </summary>
    public MapNpc Add(int talk, int x, int y, int z)
    {
        Ensure?.Invoke();
        var donor = _items.FirstOrDefault(e => e.IsTown && !e.Removed && e.TalkId == talk)
            ?? _items.FirstOrDefault(e => e.IsTown && !e.Removed);
        MapNpc row;
        if (donor != null)
        {
            row = donor.CloneAt(_items.Count, talk, x, y, z);
        }
        else
        {
            row = new MapNpc(_items.Count, 0, talk, x, y, z)
            {
                Dirty = true,
                Append = true,
            };
        }

        _items.Add(row);
        return row;
    }

    public void Remove(MapNpc row) => row.Remove();

    public void RemoveAt(int index) => Get(index)?.Remove();

    internal void Hydrate(IEnumerable<MapNpc> stock)
    {
        Ensure = null;
        var keep = _items.Where(e => e.Dirty).ToList();
        _items.Clear();
        _items.AddRange(stock);
        foreach (var row in keep)
        {
            row.Index = _items.Count;
            _items.Add(row);
        }
    }
}
