namespace Grandia.Sdk;

/// <summary>
/// MDP sec[10]+4 camera mode. Mode 2 skips Select tilt
/// (<c>+0x821B5</c> / <c>+0x8928B</c>).
/// </summary>
public enum CameraMode : byte
{
    /// <summary>Town / large outdoor — Select tilt (191 maps).</summary>
    Town = 0,

    /// <summary>Outdoor / dungeon (184 maps).</summary>
    Outdoor = 1,

    /// <summary>Interior — no Select tilt (64 maps).</summary>
    Interior = 2,

    /// <summary>Special (11 maps).</summary>
    Special = 3,
}

/// <summary>
/// Select pan AABB at sec[10]+0x94..+0x97. HD copies these to
/// <c>713F44/3E/40/42</c> and clamps Select camera X/Z. Byte 0x94 == 0
/// turns pan off (not a west wall of −2048). World units snap to 16.
/// </summary>
public sealed class MapSelectPan
{
    private readonly MapCamera _owner;
    private bool _enabled;
    private int _xMin;
    private int _zMin;
    private int _xMax;
    private int _zMax;
    private short _clipLo;
    private short _clipHi;
    private short _scale;
    private float _distance = StockDistance;
    private bool _distanceSet;

    internal MapSelectPan(MapCamera owner) => _owner = owner;

    /// <summary>Stock Select p28 (script 0 +0x20 / <c>[0x719334]</c>).</summary>
    public const float StockDistance = 0.25f;

    public bool Enabled
    {
        get => _owner.Touch(_enabled);
        set => _owner.Set(ref _enabled, value);
    }

    public int XMin
    {
        get => _owner.Touch(_xMin);
        set => SetBounds(value, _zMin, _xMax, _zMax);
    }

    public int ZMin
    {
        get => _owner.Touch(_zMin);
        set => SetBounds(_xMin, value, _xMax, _zMax);
    }

    public int XMax
    {
        get => _owner.Touch(_xMax);
        set => SetBounds(_xMin, _zMin, value, _zMax);
    }

    public int ZMax
    {
        get => _owner.Touch(_zMax);
        set => SetBounds(_xMin, _zMin, _xMax, value);
    }

    /// <summary>
    /// Pack world XZ limits (16-unit steps). While enabled, west of
    /// −2048 becomes −2032 so byte 0x94 is not the pan-off switch.
    /// </summary>
    public void SetBounds(int xMin, int zMin, int xMax, int zMax)
    {
        _owner.EnsureLoaded();
        if (!_owner.Present)
        {
            throw new InvalidOperationException("map has no MDP sec[10] camera params");
        }

        var packed = MdpSec10.EncodeSelectPan(xMin, zMin, xMax, zMax, enabled: true);
        var decoded = MdpSec10.DecodeSelectPan(packed);
        if (_xMin == decoded.XMin && _zMin == decoded.ZMin &&
            _xMax == decoded.XMax && _zMax == decoded.ZMax)
        {
            return;
        }

        _xMin = decoded.XMin;
        _zMin = decoded.ZMin;
        _xMax = decoded.XMax;
        _zMax = decoded.ZMax;
        _owner.Dirty = true;
    }

    /// <summary>
    /// Sec[10]+0xE4. Minimap/AMAP span lo (<c>|hi−lo|</c> →
    /// <c>[0x71C1B0]</c>). Not field Select height or pan walls.
    /// Stock Parm/Marna is −512.
    /// </summary>
    public short ClipLo
    {
        get => _owner.Touch(_clipLo);
        set => _owner.Set(ref _clipLo, value);
    }

    /// <summary>Sec[10]+0xE8 minimap/AMAP span hi. Not field Select height.</summary>
    public short ClipHi
    {
        get => _owner.Touch(_clipHi);
        set => _owner.Set(ref _clipHi, value);
    }

    /// <summary>
    /// Sec[10]+0xEE. SoftHD writes <c>(EE&gt;&gt;8)&lt;&lt;16</c> to
    /// <c>7196F0</c> (Parm 256 → 0x10000). Minimap/AMAP only — not
    /// field Select zoom.
    /// </summary>
    public short Scale
    {
        get => _owner.Touch(_scale);
        set => _owner.Set(ref _scale, value);
    }

    /// <summary>
    /// How far the Select camera sits from the ground (PGMDT script 0
    /// p28 / live <c>[0x719334]</c>). Stock is <see cref="StockDistance"/>
    /// (0x4000). Smaller pulls the camera further out; the host substitutes
    /// this for the hardcoded enter write at <c>+0x7D028</c>. Does not
    /// change the pan AABB — use <see cref="SetBounds"/> for that.
    /// </summary>
    public float Distance
    {
        get => _owner.Touch(_distance);
        set
        {
            _owner.EnsureLoaded();
            if (!_owner.Present)
            {
                throw new InvalidOperationException("map has no MDP sec[10] camera params");
            }

            // 0.05 and below crash SoftHD's GPU packet walk.
            var d = Math.Clamp(value, 0.06f, 8f);
            if (_distanceSet && _distance == d)
            {
                return;
            }

            _distance = d;
            _distanceSet = true;
            _owner.Dirty = true;
        }
    }

    /// <summary>
    /// 16.16 p28 to poke at Select enter, or 0 when stock (do not override).
    /// </summary>
    internal int P28Raw =>
        !_distanceSet ? 0 : Math.Max(1, (int)Math.Round(_distance * 65536.0));

    internal void Load(bool enabled, int xMin, int zMin, int xMax, int zMax,
        short clipLo = 0, short clipHi = 0, short scale = 0,
        float distance = StockDistance, bool distanceSet = false)
    {
        _enabled = enabled;
        _xMin = xMin;
        _zMin = zMin;
        _xMax = xMax;
        _zMax = zMax;
        _clipLo = clipLo;
        _clipHi = clipHi;
        _scale = scale;
        _distance = distance;
        _distanceSet = distanceSet;
    }

    public override string ToString() =>
        Enabled
            ? $"on ({XMin},{ZMin})-({XMax},{ZMax}) dist={Distance}"
            : $"off ({XMin},{ZMin})-({XMax},{ZMax}) dist={Distance}";
}

/// <summary>
/// This map's sec[10] camera params (mode, pitch reset, Select pan,
/// Select height, follow-cam / proj words). Same lazy fopen hydrate as
/// <see cref="Map.Zones"/>. Dirty fields are written onto the live
/// field-params heap at <c>[0x63FA9C]</c> after the field-setup
/// word-copy; Select pan also pokes <c>713F44/3E/40/42</c>;
/// <see cref="MapSelectPan.Distance"/> hooks the Select-enter p28 write.
/// </summary>
public sealed class MapCamera
{
    private byte[] _raw = [];
    private CameraMode _mode;
    private uint _pitch;
    private uint _follow;
    private uint _followTerm;
    private ushort _projA;
    private ushort _projB;
    private ushort _projC;
    private short _viewX;
    private short _viewY;
    private short _viewZ;

    public MapCamera() => SelectPan = new MapSelectPan(this);

    internal Action? Ensure { get; set; }

    private bool _present;

    /// <summary>True after a 512-byte sec[10] hydrate.</summary>
    public bool Present
    {
        get
        {
            EnsureLoaded();
            return _present;
        }
        private set => _present = value;
    }

    public bool Dirty { get; set; }

    /// <summary>Sec[10]+4. Mode 2 skips Select tilt.</summary>
    public CameraMode Mode
    {
        get => Touch(_mode);
        set
        {
            if ((byte)value > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "camera mode must be 0..3");
            }

            Set(ref _mode, value);
        }
    }

    /// <summary>
    /// Sec[10]+0x10 camera-path pitch reset (<c>&lt;&lt;16</c> →
    /// <c>[0x719304]</c> at <c>+0x5B61F</c>). 0 on most outdoors; 45
    /// on typical interiors. Not Select height.
    /// </summary>
    public uint Pitch
    {
        get => Touch(_pitch);
        set => Set(ref _pitch, value);
    }

    /// <summary>
    /// Sec[10]+0x0C follow-cam term (<c>&lt;&lt;16</c> → cam +0x44).
    /// Towns 36, stock Marna 35. Walk/follow only.
    /// </summary>
    public uint Follow
    {
        get => Touch(_follow);
        set => Set(ref _follow, value);
    }

    /// <summary>
    /// Sec[10]+0x18 follow-cam term → cam +0x28. Towns
    /// <c>0x10000</c>; stock Marna <c>0xDEBD</c>. Disk 0 is treated as
    /// <c>0x10000</c> (stock maps never store 0).
    /// </summary>
    public uint FollowTerm
    {
        get => Touch(_followTerm);
        set => Set(ref _followTerm, value);
    }

    /// <summary>Sec[10]+0x84 → <c>71A9D8</c> at map load. 0 keeps 0x400.</summary>
    public ushort ProjA
    {
        get => Touch(_projA);
        set => Set(ref _projA, value);
    }

    /// <summary>Sec[10]+0x86 → <c>71C1A0</c> at map load. 0 keeps 0x180.</summary>
    public ushort ProjB
    {
        get => Touch(_projB);
        set => Set(ref _projB, value);
    }

    /// <summary>Sec[10]+0x88 → <c>719708</c> at map load. 0 keeps 0x800.</summary>
    public ushort ProjC
    {
        get => Touch(_projC);
        set => Set(ref _projC, value);
    }

    /// <summary>Sec[10]+6 default view X (sec[14] miss fallback).</summary>
    public short ViewX
    {
        get => Touch(_viewX);
        set => Set(ref _viewX, value);
    }

    /// <summary>Sec[10]+8 default view Y (often 256).</summary>
    public short ViewY
    {
        get => Touch(_viewY);
        set => Set(ref _viewY, value);
    }

    /// <summary>Sec[10]+0xA default view Z.</summary>
    public short ViewZ
    {
        get => Touch(_viewZ);
        set => Set(ref _viewZ, value);
    }

    public MapSelectPan SelectPan { get; }

    internal byte[] Raw
    {
        get
        {
            EnsureLoaded();
            return _raw;
        }
    }

    internal T Touch<T>(T value)
    {
        EnsureLoaded();
        return value;
    }

    internal void EnsureLoaded() => Ensure?.Invoke();

    internal void Set<T>(ref T field, T value)
    {
        EnsureLoaded();
        if (!Present)
        {
            throw new InvalidOperationException("map has no MDP sec[10] camera params");
        }

        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Dirty = true;
    }

    internal void Hydrate(byte[]? blob)
    {
        Ensure = null;
        var keep = Dirty;
        var mode = _mode;
        var pitch = _pitch;
        var follow = _follow;
        var followTerm = _followTerm;
        var projA = _projA;
        var projB = _projB;
        var projC = _projC;
        var viewX = _viewX;
        var viewY = _viewY;
        var viewZ = _viewZ;
        var panOn = SelectPan.Enabled;
        var xMin = SelectPan.XMin;
        var zMin = SelectPan.ZMin;
        var xMax = SelectPan.XMax;
        var zMax = SelectPan.ZMax;
        var clipLo = SelectPan.ClipLo;
        var clipHi = SelectPan.ClipHi;
        var scale = SelectPan.Scale;
        var distance = SelectPan.Distance;
        var distanceSet = SelectPan.P28Raw != 0;

        MdpSec10.Load(this, blob);

        if (!keep)
        {
            return;
        }

        _mode = mode;
        _pitch = pitch;
        _follow = follow;
        _followTerm = followTerm;
        _projA = projA;
        _projB = projB;
        _projC = projC;
        _viewX = viewX;
        _viewY = viewY;
        _viewZ = viewZ;
        SelectPan.Load(panOn, xMin, zMin, xMax, zMax, clipLo, clipHi, scale, distance, distanceSet);
        Dirty = true;
    }

    internal void Adopt(byte[] raw, CameraMode mode, uint pitch, uint follow, uint followTerm,
        ushort projA, ushort projB, ushort projC, short viewX, short viewY, short viewZ,
        bool panEnabled, int xMin, int zMin, int xMax, int zMax,
        short clipLo, short clipHi, short scale)
    {
        _raw = raw;
        Present = raw.Length >= MdpSec10.Size;
        _mode = mode;
        _pitch = pitch;
        _follow = follow;
        _followTerm = followTerm;
        _projA = projA;
        _projB = projB;
        _projC = projC;
        _viewX = viewX;
        _viewY = viewY;
        _viewZ = viewZ;
        SelectPan.Load(panEnabled, xMin, zMin, xMax, zMax, clipLo, clipHi, scale);
    }

    public override string ToString() =>
        Present
            ? $"mode={Mode} pitch={Pitch} pan={SelectPan}"
            : "no sec[10]";
}
