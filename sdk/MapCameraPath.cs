namespace Grandia.Sdk;

/// <summary>
/// One 16.16 (or sentinel) word from a sec[15] camera-path op.
/// Walker at <c>+0x5EAD0</c>: <c>0x80000000</c> inherit live,
/// <c>0x7FFFFFFF</c> unset, <c>0x7FFFFFFE</c> player/entity axis,
/// <c>0x7FFFFFFD</c> snapshot from <c>save_cam</c>. Else signed 16.16.
/// </summary>
public readonly record struct MapCameraPathValue(uint Raw)
{
    public const uint InheritRaw = 0x80000000;
    public const uint UnsetRaw = 0x7FFFFFFF;
    public const uint PlayerRaw = 0x7FFFFFFE;
    public const uint SnapshotRaw = 0x7FFFFFFD;

    public bool IsInherit => Raw == InheritRaw;
    public bool IsUnset => Raw == UnsetRaw;
    public bool IsPlayer => Raw == PlayerRaw;
    public bool IsSnapshot => Raw == SnapshotRaw;
    public bool IsSpecial => IsInherit || IsUnset || IsPlayer || IsSnapshot;
    public int Signed => unchecked((int)Raw);
    public float Units => Signed / 65536f;

    public static MapCameraPathValue FromUnits(float units) =>
        new(unchecked((uint)(int)Math.Round(units * 65536d)));

    public override string ToString()
    {
        if (IsInherit)
        {
            return "inherit";
        }

        if (IsUnset)
        {
            return "unset";
        }

        if (IsPlayer)
        {
            return "player";
        }

        if (IsSnapshot)
        {
            return "snapshot";
        }

        return Units.ToString("G4");
    }
}

public enum MapCameraPathOpKind : byte
{
    Unknown = 0,
    SetPos = 0x01,
    SetDelta = 0x02,
    SetRot = 0x03,
    SetFov = 0x04,
    SetP28 = 0x05,
    AddPos = 0x06,
    AddRot = 0x07,
    Channel = 0x08,
    TableS32 = 0x0C,
    SlotMeta = 0x0D,
    /// <summary>BE u16 frames in <c>[0x6C2C0C]</c>; steps channel tweens each tick.</summary>
    Wait = 0x0F,
    Set63Fa5B = 0x10,
    Set63Faa2 = 0x11,
    Set71A640 = 0x12,
    /// <summary>
    /// BE u16 frames in <c>[0x6C2C10]</c>; freezes the walker IP.
    /// After <see cref="YieldTween"/> the countdown is held until flag
    /// <c>0x080E</c> (<c>box_lock</c>) is set — that bit is
    /// <c>[0x718BD8+0x101] &amp; 2</c>, the same cell <c>set_flag 0x080E</c>
    /// writes via <c>+0x70400</c>.
    /// </summary>
    WaitB = 0x13,
    YieldTween = 0x14,
    SaveCam = 0x15,
    SetWords = 0x16,
    Call70400 = 0x1B,
    Skip3 = 0x1C,
    ResetCam = 0x1D,
    End = 0xFF,
}

public enum MapCameraPathChannel : byte
{
    PosX = 0,
    PosY = 1,
    PosZ = 2,
    DeltaX = 3,
    DeltaY = 4,
    DeltaZ = 5,
    Yaw = 6,
    Pitch = 7,
    Roll = 8,
    FovA = 9,
    P28 = 10,
    FovB = 11,
}

/// <summary>One decoded opcode from a sec[15] chunk (+0x5B770 walker).</summary>
public sealed class MapCameraPathOp
{
    public MapCameraPathOp(
        int offset,
        byte opcode,
        MapCameraPathOpKind kind,
        IReadOnlyList<MapCameraPathValue>? values = null,
        MapCameraPathChannel? channel = null,
        int tweenMode = -1,
        int duration = 0,
        byte[]? foot = null,
        int arg = 0,
        int scale = 0,
        int mark = 0,
        int saveFlags = 0,
        IReadOnlyList<int>? words = null)
    {
        Offset = offset;
        Opcode = opcode;
        Kind = kind;
        Values = values is { Count: > 0 } ? values.ToList() : [];
        Channel = channel;
        TweenMode = tweenMode;
        Duration = duration;
        Foot = foot is { Length: > 0 } ? foot.ToArray() : [];
        Arg = arg;
        Scale = scale;
        Mark = mark;
        SaveFlags = saveFlags;
        Words = words is { Count: > 0 } ? words.ToList() : [];
    }

    public int Offset { get; }
    public byte Opcode { get; }
    public MapCameraPathOpKind Kind { get; }
    public MapCameraPathChannel? Channel { get; }

    /// <summary>edx passed to <c>+0x5E070</c> (0..7) for channel tweens.</summary>
    public int TweenMode { get; }

    public IReadOnlyList<MapCameraPathValue> Values { get; }
    public int Duration { get; }
    public IReadOnlyList<byte> Foot { get; }
    public int Arg { get; }
    public int Scale { get; }
    public int Mark { get; }
    public int SaveFlags { get; }
    public IReadOnlyList<int> Words { get; }

    public bool SavesPos => (SaveFlags & 1) != 0;
    public bool SavesDelta => (SaveFlags & 2) != 0;
    public bool SavesRot => (SaveFlags & 4) != 0;

    public override string ToString() => CameraPathAsm.FormatOp(this);
}

/// <summary>
/// One sec[15] camera-path chunk. <see cref="Id"/> is the number
/// <c>camera_path {id}</c> looks up (hook <c>+5</c>, 1-based). Directory
/// slot 0 is id 1 — the engine <c>dec dl</c> then reads
/// <c>[sec15 + id*4]</c> into IP <c>[0x719934]</c>.
/// A <c>yield_tween</c> + <c>wait_b N</c> pair holds until
/// <c>set_flag 0x080E</c> (<c>box_lock</c>), then counts N frames.
/// </summary>
public sealed class MapCameraPath
{
    /// <summary>VM mutex the <c>wait_b</c> gate reads (<c>box_lock</c>).</summary>
    public const int BoxLockFlag = 0x080E;

    private byte[] _raw;
    private IReadOnlyList<MapCameraPathOp> _ops;
    private string? _error;
    private List<string>? _lines;

    public MapCameraPath(
        int index,
        int id,
        byte[]? raw = null,
        IReadOnlyList<MapCameraPathOp>? ops = null,
        string? error = null)
    {
        Index = index;
        Id = id;
        _raw = raw is { Length: > 0 } ? raw.ToArray() : [];
        if (ops is { Count: > 0 })
        {
            _ops = ops.ToList();
            _error = error;
            if (_raw.Length == 0)
            {
                _raw = MdpSec15.EncodeChunk(_ops);
            }

            return;
        }

        if (_raw.Length > 0 && _raw[0] != 0xFF)
        {
            var parsed = MdpSec15.ParseChunk(_raw);
            _ops = parsed.Ops;
            _error = parsed.Error ?? error;
            return;
        }

        _ops = [];
        _error = error;
    }

    /// <summary>Directory slot (0-based).</summary>
    public int Index { get; internal set; }

    /// <summary>Path id <c>camera_path</c> / hook <c>+5</c> looks up.</summary>
    public int Id { get; internal set; }

    /// <summary>Chunk bytes. Copied on get/set; set marks dirty and re-parses.</summary>
    public byte[] Raw
    {
        get => _raw.ToArray();
        set => Adopt(value, markDirty: true);
    }

    public IReadOnlyList<MapCameraPathOp> Ops => _ops;
    public string? Error => _error;
    public bool Empty => _raw.Length == 0 || _raw[0] == 0xFF;
    public bool Ok => _error is null && (Empty || _ops.Count > 0);
    public bool Dirty { get; set; }
    public bool Removed { get; set; }

    /// <summary>Drop this slot on the next sec[15] emit (leaves an <c>0xFF</c> stub).</summary>
    public void Remove()
    {
        Removed = true;
        Dirty = true;
    }

    /// <summary>
    /// Disassembly lines (no <c>camera_path N</c> header). First read
    /// formats <see cref="Ops"/>; writes go through <see cref="Replace"/>
    /// / <see cref="AddLine"/>.
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            _lines ??= CameraPathAsm.FormatLines(_ops, Empty);
            return _lines;
        }
    }

    /// <summary>Replace the bytecode and re-parse. Marks dirty.</summary>
    public void SetOps(IEnumerable<MapCameraPathOp> ops)
    {
        Adopt(MdpSec15.EncodeChunk(ops as IReadOnlyList<MapCameraPathOp> ?? ops.ToList()),
            markDirty: true);
    }

    /// <summary>
    /// Assemble a dump (same mnemonics as <see cref="ToAsm"/>) and mark
    /// dirty. Accepts a <c>camera_path N</c> envelope, <c>@offset</c>
    /// prefixes, and <c>#</c> comments.
    /// </summary>
    public void Replace(string disassembly)
    {
        Adopt(CameraPathAsm.Assemble(disassembly), markDirty: true);
        _lines = CameraPathAsm.FormatLines(_ops, Empty);
    }

    public void AddLine(string assemblerLine)
    {
        if (string.IsNullOrWhiteSpace(assemblerLine))
        {
            return;
        }

        var lines = Lines.ToList();
        if (lines.Count == 1 && lines[0] == "end")
        {
            lines.Clear();
        }
        else if (lines.Count > 0 && lines[^1] == "end")
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add(assemblerLine.Trim());
        Replace(string.Join('\n', lines));
    }

    public string ToAsm() => CameraPathAsm.Format(this);

    public override string ToString() =>
        $"[{Index}] id={Id} ops={_ops.Count}" + (Empty ? " empty" : "") +
        (Removed ? " removed" : "") +
        (_error is null ? "" : $" {_error}");

    internal byte[] RawBytes => _raw;

    private void Adopt(byte[]? raw, bool markDirty)
    {
        var next = raw is { Length: > 0 } ? raw.ToArray() : [];
        if (_raw.AsSpan().SequenceEqual(next) && !markDirty)
        {
            return;
        }

        _raw = next;
        if (_raw.Length == 0 || _raw[0] == 0xFF)
        {
            _ops = [];
            _error = null;
        }
        else
        {
            var (ops, error) = MdpSec15.ParseChunk(_raw);
            _ops = ops;
            _error = error;
        }

        _lines = null;
        if (markDirty)
        {
            Dirty = true;
            Removed = false;
        }
    }
}

/// <summary>
/// This map's sec[15] camera-path directory. Same lazy fopen hydrate as
/// <see cref="Map.Zones"/>. Play with <c>camera_path {id}</c> (BA38 hook 27
/// is id 1). Dirty rows are emitted and swapped onto <c>[0x71A644]</c>
/// after bind. Removed ids stay as <c>0xFF</c> stubs so hook numbers
/// do not shift.
/// </summary>
public sealed class MapCameraPathTable
{
    private readonly List<MapCameraPath> _items;

    public MapCameraPathTable()
        : this([])
    {
    }

    public MapCameraPathTable(IEnumerable<MapCameraPath> items) =>
        _items = items.ToList();

    internal Action? Ensure { get; set; }

    public bool Dirty => _items.Any(e => e.Dirty);

    public IReadOnlyList<MapCameraPath> Items
    {
        get
        {
            Ensure?.Invoke();
            return _items;
        }
    }

    public MapCameraPath? this[int index] => Get(index);

    public MapCameraPath? Get(int index)
    {
        Ensure?.Invoke();
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    /// <summary>Live path with this 1-based id, or null.</summary>
    public MapCameraPath? OfId(int id)
    {
        Ensure?.Invoke();
        return _items.FirstOrDefault(e => !e.Removed && e.Id == id);
    }

    /// <summary>
    /// Append a path (or reuse the first removed slot). Play with
    /// <c>camera_path {id}</c>. Empty until <see cref="MapCameraPath.Raw"/>
    /// / <see cref="MapCameraPath.SetOps"/> / <see cref="MapCameraPath.Replace"/>
    /// is set.
    /// </summary>
    public MapCameraPath Add(byte[]? raw = null)
    {
        Ensure?.Invoke();
        var hole = _items.FindIndex(e => e.Removed);
        if (hole >= 0)
        {
            var reuse = new MapCameraPath(hole, hole + 1, raw is { Length: > 0 } ? raw : [0xFF])
            {
                Dirty = true,
            };
            _items[hole] = reuse;
            return reuse;
        }

        var row = new MapCameraPath(_items.Count, _items.Count + 1,
            raw is { Length: > 0 } ? raw : [0xFF])
        {
            Dirty = true,
        };
        _items.Add(row);
        return row;
    }

    public MapCameraPath Add(IEnumerable<MapCameraPathOp> ops)
    {
        var row = Add();
        row.SetOps(ops);
        return row;
    }

    public MapCameraPath Add(string disassembly)
    {
        var row = Add();
        row.Replace(disassembly);
        return row;
    }

    public void Remove(MapCameraPath row) => row.Remove();

    public void RemoveAt(int index) => Get(index)?.Remove();

    internal void Hydrate(IEnumerable<MapCameraPath> stock)
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
                row.Id = i + 1;
                _items[i] = row;
                continue;
            }

            row.Index = _items.Count;
            row.Id = _items.Count + 1;
            _items.Add(row);
        }
    }
}
