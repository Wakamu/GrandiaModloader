namespace Grandia.Sdk;

/// <summary>
/// Time-warp turbo (IAT QPC/tick/Sleep + unlocked Present). 0 = off, else 2..5.
/// </summary>
public sealed class GameTurbo
{
    public const int Off = 0;
    public const int Min = 2;
    public const int Max = 5;

    /// <summary>Latched speed. 0 or 1 stores as off; 2..5 warp game clocks.</summary>
    public int Level
    {
        get => Game.Native?.TurboGet() ?? 0;
        set => Game.Native?.TurboSet(value);
    }

    /// <summary>
    /// Temporary override (field FF / hold-to-boost). 0 = use <see cref="Level"/>.
    /// Clear it when the hold ends or the latch sticks at this speed.
    /// </summary>
    public int Override
    {
        get => Game.Native?.TurboOverrideGet() ?? 0;
        set => Game.Native?.TurboOverrideSet(value);
    }

    public bool StepUp()
    {
        var next = Level <= 0 ? Min : Level + 1;
        if (next > Max)
        {
            next = Max;
        }

        Level = next;
        return true;
    }

    public bool StepDown()
    {
        Level = Level <= Min ? Off : Level - 1;
        return true;
    }
}

/// <summary>
/// Battle XP multipliers from slot_data (1 = vanilla). Magic and skill are
/// per-gain; level multiplies in-fight kill EXP into the victory pot.
/// </summary>
public sealed class GameXp
{
    public const int Min = 1;
    public const int Max = 100;

    public int Magic
    {
        get => Get(0);
        set => Set(0, value);
    }

    public int Skill
    {
        get => Get(1);
        set => Set(1, value);
    }

    public int Level
    {
        get => Get(2);
        set => Set(2, value);
    }

    private static int Get(int kind)
    {
        var n = Game.Native?.XpGet(kind) ?? Min;
        return n < Min ? Min : n;
    }

    private static void Set(int kind, int multiplier)
    {
        Game.Native?.XpSet(kind, multiplier);
    }
}

/// <summary>
/// Random field encounters. Same bit as Map debug "ENCOUNT OFF" (flag <c>0x08FD</c>).
/// Needs a loaded save (flag blob).
/// </summary>
public sealed class GameEncounters
{
    public const uint FlagId = 0x08FD;

    public bool Disabled
    {
        get => Game.Native?.EncountersGet() > 0;
        set => Game.Native?.EncountersSet(value ? 1 : 0);
    }
}

/// <summary>
/// Field 3D compass at the top-right. Stock is on. Set
/// <see cref="Visible"/> false from <c>Init</c> or <c>[OnMapLoad]</c>
/// so SoftHD never caches the mesh.
/// </summary>
public sealed class GameCompass
{
    public bool Visible
    {
        get => (Game.Native?.CompassGet() ?? 1) > 0;
        set => Game.Native?.CompassSet(value ? 1 : 0);
    }

    public void Hide() => Visible = false;

    public void Show() => Visible = true;
}

/// <summary>Vanilla ASCII debug flag at <c>+0x23F00E</c> ("4000" / "0000"). 9999 damage etc.</summary>
public sealed class GameDebug
{
    public bool Enabled
    {
        get => Game.Native?.DebugGet() > 0;
        set => Game.Native?.DebugSet(value ? 1 : 0);
    }
}

/// <summary>Keyboard via GetAsyncKeyState. Edge detect in the mod with a was-down flag.</summary>
public sealed class GameInput
{
    public bool KeyDown(int vk)
    {
        return Game.Native?.KeyDown(vk) > 0;
    }

    /// <summary>Scan code (e.g. <c>0x29</c> for ² / tilde), mapped with MapVirtualKey.</summary>
    public bool ScanDown(int scan)
    {
        return Game.Native?.ScanDown(scan) > 0;
    }
}

/// <summary>Live XInput poll (same pad <see cref="TickEvent"/> snapshots).</summary>
public sealed class GamePad
{
    public PadState Poll()
    {
        var packed = Game.Native?.PadPoll() ?? 0;
        return Unpack(packed);
    }

    internal static PadState Unpack(int packed)
    {
        var u = unchecked((uint)packed);
        return new PadState((int)(u & 0xFFFF), (int)((u >> 16) & 0xFF), (int)((u >> 24) & 0xFF));
    }
}
