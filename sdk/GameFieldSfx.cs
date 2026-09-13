namespace Grandia.Sdk;

/// <summary>
/// Live MDP sec[29] positional field SFX (river, frogs, town beds).
/// Mixer listener is the camera, so volume follows walk <em>or</em> pan.
/// Edits hit the heap copy at <c>[0x640E4C]</c> and last until the next
/// map load recopies the MDP. Off the field, or before setup, the list
/// is empty.
/// </summary>
public sealed class GameFieldSfx
{
    public const int MaxEmitters = 250;

    /// <summary>Live rows before the <c>0xFF</c> terminator. 0 if unbound.</summary>
    public int Count => Game.Native?.SfxEmitterCount() ?? 0;

    /// <summary>Header range (Gumbo / Marna / Parm = 896). 0 if unbound.</summary>
    public int Range => Game.Native?.SfxEmitterRange() ?? 0;

    public FieldSfxEmitter? this[int index] => Get(index);

    public FieldSfxEmitter? Get(int index)
    {
        if (Game.Native?.SfxEmitterGet(index, out var recId, out var sfx, out var flags, out var x,
                out var y, out var z, out var muted) is not > 0)
        {
            return null;
        }

        return new FieldSfxEmitter(index, recId, sfx, flags, x, y, z, muted > 0);
    }

    public FieldSfxEmitter[] List()
    {
        var n = Count;
        if (n <= 0)
        {
            return [];
        }

        if (n > MaxEmitters)
        {
            n = MaxEmitters;
        }

        var list = new FieldSfxEmitter[n];
        var written = 0;
        for (var i = 0; i < n; i++)
        {
            var row = Get(i);
            if (row is null)
            {
                break;
            }

            list[written++] = row;
        }

        if (written == n)
        {
            return list;
        }

        var trimmed = new FieldSfxEmitter[written];
        Array.Copy(list, trimmed, written);
        return trimmed;
    }

    public bool Move(int index, int x, int y, int z) =>
        Game.Native?.SfxEmitterMove(index, x, y, z) > 0;

    public bool Move(int index, WalkPos pos) => Move(index, pos.X, pos.Y, pos.Z);

    public bool Mute(int index, bool muted = true) =>
        Game.Native?.SfxEmitterMute(index, muted ? 1 : 0) > 0;

    public bool Unmute(int index) => Mute(index, false);

    /// <summary>Mute or unmute every live emitter. Returns how many succeeded.</summary>
    public int MuteAll(bool muted = true)
    {
        var n = 0;
        foreach (var row in List())
        {
            if (Mute(row.Index, muted))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Mute every emitter with this catalog id (e.g. Gumbo frogs = 16).</summary>
    public int MuteSfx(int sfxId, bool muted = true)
    {
        var n = 0;
        foreach (var row in List())
        {
            if (row.Sfx == sfxId && Mute(row.Index, muted))
            {
                n++;
            }
        }

        return n;
    }

    public bool SetSfx(int index, int sfxId) => Game.Native?.SfxEmitterSetSfx(index, sfxId) > 0;

    public bool SetFlags(int index, int flags) => Game.Native?.SfxEmitterSetFlags(index, flags) > 0;

    /// <summary>
    /// Insert before the terminator if the 1024-byte heap still has room
    /// (<see cref="MdpSec29.MaxLive"/>). Lasts until the next map load.
    /// </summary>
    public FieldSfxEmitter? Add(int sfx, int x, int y, int z, bool looping = true)
    {
        var flags = looping ? 0x60 : 0x80;
        var period = looping ? 12 : 0;
        var index = Game.Native?.SfxEmitterAdd(0, sfx, flags, x, y, z, 0x81, period, 0) ?? -1;
        return index >= 0 ? Get(index) : null;
    }

    public FieldSfxEmitter? Add(int sfx, WalkPos pos, bool looping = true) =>
        Add(sfx, pos.X, pos.Y, pos.Z, looping);

    public bool Remove(int index) => Game.Native?.SfxEmitterRemove(index) > 0;
}

/// <summary>One sec[29] emitter. Properties write through to live RAM.</summary>
public sealed class FieldSfxEmitter
{
    internal FieldSfxEmitter(int index, int id, int sfx, int flags, int x, int y, int z, bool muted)
    {
        Index = index;
        Id = id;
        _sfx = sfx;
        _flags = flags;
        _x = x;
        _y = y;
        _z = z;
        _muted = muted;
    }

    public int Index { get; }

    /// <summary>Record id in the table (not the catalog sample).</summary>
    public int Id { get; }

    public int Flags
    {
        get => _flags;
        set
        {
            if (Game.Native?.SfxEmitterSetFlags(Index, value) > 0)
            {
                _flags = value;
            }
        }
    }

    public bool Looping => (_flags & 0x40) != 0;

    public int Sfx
    {
        get => _sfx;
        set
        {
            if (Game.Native?.SfxEmitterSetSfx(Index, value) > 0)
            {
                _sfx = value;
            }
        }
    }

    public int X
    {
        get => _x;
        set => Move(value, _y, _z);
    }

    public int Y
    {
        get => _y;
        set => Move(_x, value, _z);
    }

    public int Z
    {
        get => _z;
        set => Move(_x, _y, value);
    }

    public WalkPos Position
    {
        get => new(_x, _y, _z);
        set => Move(value.X, value.Y, value.Z);
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (Game.Native?.SfxEmitterMute(Index, value ? 1 : 0) > 0)
            {
                _muted = value;
            }
        }
    }

    public bool Move(int x, int y, int z)
    {
        if (Game.Native?.SfxEmitterMove(Index, x, y, z) is not > 0)
        {
            return false;
        }

        _x = x;
        _y = y;
        _z = z;
        return true;
    }

    public bool Move(WalkPos pos) => Move(pos.X, pos.Y, pos.Z);

    public bool Remove() => Game.FieldSfx.Remove(Index);

    public bool Mute()
    {
        Muted = true;
        return _muted;
    }

    public bool Unmute()
    {
        Muted = false;
        return !_muted;
    }

    public override string ToString()
    {
        var loop = Looping ? " loop" : "";
        var mute = _muted ? " muted" : "";
        return $"[{Index}] id={Id} sfx={_sfx}{loop}{mute} ({_x},{_y},{_z})";
    }

    private int _sfx;
    private int _flags;
    private int _x;
    private int _y;
    private int _z;
    private bool _muted;
}
