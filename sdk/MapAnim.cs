namespace Grandia.Sdk;

/// <summary>
/// One Stream B cue. <see cref="Table"/> 0/1/2 is <c>call_hook</c>
/// (<c>+0x53560</c>) on sec[7] table-1 zones / table-2 hooks / table-3 alt.
/// <c>3</c> is a delay: <see cref="Param"/> ticks written to <c>71A748</c>
/// (the slot countdown subtracts 1 per tick).
/// </summary>
public readonly record struct MapAnimCue(int Frame, int Table, int Param);

/// <summary>
/// One sec[21] clip directory row. <see cref="Id"/> is the number
/// <c>anim N</c> looks up. <see cref="StreamA"/> / <see cref="StreamB"/>
/// are the raw payloads at <c>off_a</c> / <c>off_b</c> (two streams, not a
/// range). Stream A is <c>u16</c> header + <see cref="Flags"/> × s16 XYZ
/// (walk units; mode 2 writes the NPC). Header low byte 0 skips facing;
/// nonzero turns (mode 1 also stores the byte and uses ≥6 as gait 2).
/// Stream B is timed <see cref="Cues"/>. <see cref="SetFrames"/> /
/// <see cref="SetCues"/> (or raw streams) mark dirty; the host mallocs
/// the emit and swaps <c>[0x71CAE0]</c> after bind. Shared-bank ids are
/// omitted.
/// </summary>
public sealed class MapAnim
{
    private int _id;
    private int _flags;
    private readonly int _stockFlags;
    private byte[] _streamA;
    private byte[] _streamB;

    public MapAnim(int index, int id, int flags, byte[]? streamA = null, byte[]? streamB = null)
    {
        Index = index;
        _id = ClampU16(id);
        _flags = ClampU16(flags);
        _stockFlags = _flags;
        _streamA = Copy(streamA);
        _streamB = Copy(streamB);
    }

    /// <summary>Directory slot (0-based).</summary>
    public int Index { get; internal set; }

    /// <summary>Clip id the <c>anim</c> hook looks up (sec[21] +0 of the row).</summary>
    public int Id
    {
        get => _id;
        set => Set(ref _id, ClampU16(value));
    }

    /// <summary>Stream A frame count (packed into <c>71A746</c> on latch).</summary>
    public int Flags
    {
        get => _flags;
        set => Set(ref _flags, ClampU16(value));
    }

    /// <summary>Raw stream at <c>off_a</c>. Copied on get/set.</summary>
    public byte[] StreamA
    {
        get => Copy(_streamA);
        set
        {
            var next = Copy(value);
            if (_streamA.AsSpan().SequenceEqual(next))
            {
                return;
            }

            _streamA = next;
            Dirty = true;
        }
    }

    /// <summary>Raw stream at <c>off_b</c>. Copied on get/set.</summary>
    public byte[] StreamB
    {
        get => Copy(_streamB);
        set
        {
            var next = Copy(value);
            if (_streamB.AsSpan().SequenceEqual(next))
            {
                return;
            }

            _streamB = next;
            Dirty = true;
        }
    }

    /// <summary>First u16 of Stream A. Mode 2/1 only test the low byte.</summary>
    public int Header
    {
        get => _streamA.Length >= 2 ? _streamA[0] | (_streamA[1] << 8) : 0;
        set
        {
            var next = ClampU16(value);
            if (Header == next && _streamA.Length >= 2)
            {
                return;
            }

            var a = _streamA.Length >= 2 ? _streamA.ToArray() : new byte[2];
            a[0] = (byte)(next & 0xFF);
            a[1] = (byte)(next >> 8);
            StreamA = a;
        }
    }

    /// <summary>Low byte of <see cref="Header"/> is nonzero — update facing.</summary>
    public bool Turn => (_streamA.Length >= 1 ? _streamA[0] : 0) != 0;

    /// <summary>Stream A samples after the header (walk-unit XYZ).</summary>
    public IReadOnlyList<WalkPos> Frames
    {
        get
        {
            if (_streamA.Length < 8)
            {
                return [];
            }

            var n = (_streamA.Length - 2) / 6;
            if (_flags > 0 && _flags < n)
            {
                n = _flags;
            }
            var frames = new WalkPos[n];
            for (var i = 0; i < n; i++)
            {
                var o = 2 + i * 6;
                frames[i] = new WalkPos(
                    (short)(_streamA[o] | (_streamA[o + 1] << 8)),
                    (short)(_streamA[o + 2] | (_streamA[o + 3] << 8)),
                    (short)(_streamA[o + 4] | (_streamA[o + 5] << 8)));
            }

            return frames;
        }
    }

    /// <summary>Stream B events (hook / zone / delay).</summary>
    public IReadOnlyList<MapAnimCue> Cues
    {
        get
        {
            if (_streamB.Length < 2)
            {
                return [];
            }

            var n = _streamB[0] | (_streamB[1] << 8);
            var cues = new List<MapAnimCue>(n);
            var o = 2;
            for (var i = 0; i < n && o + 4 <= _streamB.Length; i++, o += 4)
            {
                var frame = _streamB[o] | (_streamB[o + 1] << 8);
                var cmd = _streamB[o + 2] | (_streamB[o + 3] << 8);
                cues.Add(new MapAnimCue(frame, cmd >> 14, cmd & 0x3FFF));
            }

            return cues;
        }
    }

    public bool Dirty { get; set; }

    /// <summary>Drop this row on the next sec[21] emit.</summary>
    public bool Removed { get; set; }

    public void Remove()
    {
        Removed = true;
        Dirty = true;
    }

    /// <summary>
    /// Encode Stream A and set <see cref="Flags"/> to the sample count.
    /// Keeps the current <see cref="Header"/> unless <paramref name="header"/> is set.
    /// </summary>
    public void SetFrames(IEnumerable<WalkPos> frames, int? header = null)
    {
        var list = frames as IList<WalkPos> ?? frames.ToList();
        var hdr = ClampU16(header ?? Header);
        var play = list.Count;
        var samples = play > _stockFlags ? play : _stockFlags;
        var a = new byte[2 + samples * 6];
        a[0] = (byte)(hdr & 0xFF);
        a[1] = (byte)(hdr >> 8);
        for (var i = 0; i < samples; i++)
        {
            var src = list[i < play ? i : play - 1];
            if (play == 0)
            {
                break;
            }

            var o = 2 + i * 6;
            WriteS16(a, o, src.X);
            WriteS16(a, o + 2, src.Y);
            WriteS16(a, o + 4, src.Z);
        }

        StreamA = a;
        Flags = play;
    }

    /// <summary>Encode Stream B (<c>table</c> 0–3, <c>param</c> 14-bit).</summary>
    public void SetCues(IEnumerable<MapAnimCue> cues)
    {
        var list = cues as IList<MapAnimCue> ?? cues.ToList();
        var b = new byte[2 + list.Count * 4];
        b[0] = (byte)(list.Count & 0xFF);
        b[1] = (byte)(list.Count >> 8);
        for (var i = 0; i < list.Count; i++)
        {
            var o = 2 + i * 4;
            var frame = ClampU16(list[i].Frame);
            var table = list[i].Table < 0 ? 0 : list[i].Table > 3 ? 3 : list[i].Table;
            var param = list[i].Param < 0 ? 0 : list[i].Param > 0x3FFF ? 0x3FFF : list[i].Param;
            var cmd = (table << 14) | param;
            b[o] = (byte)(frame & 0xFF);
            b[o + 1] = (byte)(frame >> 8);
            b[o + 2] = (byte)(cmd & 0xFF);
            b[o + 3] = (byte)(cmd >> 8);
        }

        StreamB = b;
    }

    public override string ToString()
    {
        var gone = Removed ? " removed" : "";
        return $"[{Index}] id={Id} flags=0x{_flags:X} a={_streamA.Length} b={_streamB.Length}{gone}";
    }

    internal byte[] StreamARaw => _streamA;

    internal byte[] StreamBRaw => _streamB;

    private void Set(ref int field, int value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        Dirty = true;
    }

    internal static int ClampU16(int value) =>
        value < 0 ? 0 : value > 0xFFFF ? 0xFFFF : value;

    private static byte[] Copy(byte[]? src) =>
        src is { Length: > 0 } ? src.ToArray() : [];

    private static void WriteS16(byte[] dest, int off, int value)
    {
        var s = (short)(value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : value);
        dest[off] = (byte)(s & 0xFF);
        dest[off + 1] = (byte)((s >> 8) & 0xFF);
    }
}

/// <summary>
/// This map's sec[21] clip directory. Same lazy fopen hydrate as
/// <see cref="Map.Zones"/>. Ids missing here are the shared bank, not NPC data.
/// Dirty rows are emitted and swapped onto <c>[0x71CAE0]</c> after bind.
/// </summary>
public sealed class MapAnimTable
{
    private readonly List<MapAnim> _items;

    public MapAnimTable()
        : this([])
    {
    }

    public MapAnimTable(IEnumerable<MapAnim> items) =>
        _items = items.ToList();

    internal Action? Ensure { get; set; }

    public bool Dirty => _items.Any(e => e.Dirty);

    public IReadOnlyList<MapAnim> Items
    {
        get
        {
            Ensure?.Invoke();
            return _items;
        }
    }

    public MapAnim? this[int index] => Get(index);

    public MapAnim? Get(int index)
    {
        Ensure?.Invoke();
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    /// <summary>First live clip with this directory id, or null (shared-bank ids miss).</summary>
    public MapAnim? OfId(int id)
    {
        Ensure?.Invoke();
        return _items.FirstOrDefault(e => !e.Removed && e.Id == id);
    }

    /// <summary>Append a clip with this directory id (empty path / cues).</summary>
    public MapAnim Add(int id)
    {
        Ensure?.Invoke();
        var row = new MapAnim(_items.Count, id, 0) { Dirty = true };
        _items.Add(row);
        return row;
    }

    public void Remove(MapAnim row) => row.Remove();

    public void RemoveAt(int index) => Get(index)?.Remove();

    internal void Hydrate(IEnumerable<MapAnim> stock)
    {
        Ensure = null;
        var keep = _items.Where(e => e.Dirty && !e.Removed).ToList();
        _items.Clear();
        _items.AddRange(stock);
        foreach (var row in keep)
        {
            var i = _items.FindIndex(e => !e.Removed && e.Id == row.Id);
            if (i >= 0)
            {
                row.Index = i;
                _items[i] = row;
                continue;
            }

            row.Index = _items.Count;
            _items.Add(row);
        }
    }
}
