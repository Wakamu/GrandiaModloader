namespace Grandia.Sdk;

/// <summary>
/// Live game RAM: stash, gold, flags, party walk pos, turbo, encounters, save extra, and debug.
/// Bound by GrandiaMod.dll after a save is in memory. Safe to call from
/// hook methods (<see cref="OnTickAttribute"/>, <see cref="OnBattleLoadAttribute"/>, …).
/// </summary>
public static class Game
{
    public static GameStash Stash { get; } = new();

    public static GameGold Gold { get; } = new();

    public static GameFlags Flags { get; } = new();

    public static GameParty Party { get; } = new();

    public static GameCamera Camera { get; } = new();

    public static GameTurbo Turbo { get; } = new();

    public static GameXp Xp { get; } = new();

    public static GameEncounters Encounters { get; } = new();

    public static GameDebug Debug { get; } = new();

    public static GameInput Input { get; } = new();

    public static GamePad Pad { get; } = new();

    public static GameOverlay Overlay { get; } = new();

    /// <summary>
    /// Positional field SFX (sec[29]). <see cref="GameFieldSfx.List"/> /
    /// <see cref="GameFieldSfx.Mute"/> / <see cref="GameFieldSfx.Move"/> /
    /// <see cref="GameFieldSfx.Add"/> / <see cref="GameFieldSfx.Remove"/>.
    /// </summary>
    public static GameFieldSfx FieldSfx { get; } = new();

    /// <summary>
    /// Custom Start→Options-style list. <see cref="GameMenu.Open"/> from a hook;
    /// poll <see cref="GameMenu.TakeChoice"/> / <see cref="GameMenu.TakeCancel"/>.
    /// Each row is a label plus two or more values; Left/Right cycles them.
    /// Compose a menu with <see cref="Ui"/> (<see cref="UiBox"/>, <see cref="UiRow"/>, …).
    /// </summary>
    public static GameMenu Menu { get; } = new();

    /// <summary>
    /// Layout kit: <see cref="GameUi.Show"/> a tree of box / row / column / label / item.
    /// </summary>
    public static GameUi Ui { get; } = new();

    /// <summary>
    /// Append to <c>GrandiaMod.log</c> next to <c>grandia.exe</c> (same file as
    /// the host). Safe from <see cref="InitAttribute"/> and any hook.
    /// </summary>
    public static GameLog Log { get; } = new();

    /// <summary>
    /// Extra save bag shared by every mod. The host writes it on every
    /// slot save and replaces it on every slot load (also with no mods
    /// loaded). Prefix keys with your mod id.
    /// </summary>
    public static GameSaveData SaveData { get; } = new();

    /// <summary>
    /// Field / menu / world map / battle. Battle wins over a leftover menu
    /// byte. Title reports <see cref="GameStatus.Field"/>.
    /// </summary>
    public static GameStatus Status => GetGameStatus();

    public static GameStatus GetGameStatus()
    {
        var n = Native?.StatusGet() ?? 0;
        return n is >= 0 and <= 3 ? (GameStatus)n : GameStatus.Field;
    }

    /// <summary>
    /// Queue a vanilla <c>+0x614D0</c> MapTravel (fires
    /// <see cref="OnMapTravelAttribute"/>). Same stack as a field door:
    /// <paramref name="aux9"/> / <paramref name="auxA"/> (default 1 / 30).
    /// <paramref name="aux9"/> 0 skips the fade. On the world map, dest /
    /// spawn go through the confirm FSM (stock fade 1 / 0x3C) so AMAP
    /// tears down. Applies on the next idle field tick. False in battle
    /// or if the host is not bound.
    /// </summary>
    public static bool WarpTo(MapId dest, int spawn = 0, int aux9 = 1, int auxA = 30) =>
        WarpTo((int)dest.Value, spawn, aux9, auxA);

    public static bool WarpTo(int dest, int spawn = 0, int aux9 = 1, int auxA = 30)
    {
        return Native?.WarpTo(dest, spawn, aux9, auxA) > 0;
    }

    /// <summary>
    /// Close <c>grandia.exe</c> (WM_CLOSE, then <c>ExitProcess</c>). False if
    /// the host is not bound.
    /// </summary>
    public static bool Quit() => Native?.Quit() > 0;

    /// <summary>
    /// Queue a field script for the next idle field tick (token
    /// <c>0xFFFF</c>, not battle / menu). Uses stock arm <c>+0x57550</c>
    /// so <see cref="OnScriptExecuteAttribute"/> still runs.
    /// </summary>
    public static bool RunScript(int scriptId) =>
        Native?.RunField(0, scriptId, 0, 0, 0) > 0;

    /// <summary>
    /// Queue assembler text as a one-shot script. Missing
    /// <c>script 0xNNNN</c> / <c>yield</c> are filled in.
    /// </summary>
    public static bool RunScript(string assembler, int scriptId = 0xFFFE) =>
        RunScript(AssembleScript(assembler, scriptId), scriptId);

    /// <summary>Queue a <see cref="Script"/> (same assembler as OnMapLoad).</summary>
    public static bool RunScript(Script script)
    {
        var id = script.Id != 0 ? script.Id : 0xFFFE;
        return RunScript(script.ToAsm(), id);
    }

    /// <summary>Queue already-assembled SCN words. Appends <c>yield</c> if missing.</summary>
    public static bool RunScript(byte[] bytecode, int scriptId = 0xFFFE)
    {
        var bytes = EnsureScriptYield(bytecode);
        if (bytes.Length == 0)
        {
            return false;
        }

        var pin = System.Runtime.InteropServices.GCHandle.Alloc(
            bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            return Native?.RunField(1, scriptId, 0, pin.AddrOfPinnedObject(), bytes.Length) > 0;
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>
    /// Queue <c>call_hook N</c> (table 1) or <c>call_hook N alt</c> (table 2)
    /// for the next idle field tick. Goes through <c>+0x53560</c> so
    /// <see cref="OnCallHookAttribute"/> still runs.
    /// </summary>
    public static bool RunHook(int hookId, int table = 1) =>
        Native?.RunField(2, hookId, table == 2 ? 2 : 1, 0, 0) > 0;

    /// <summary>
    /// Queue a table-2 assembler line, e.g.
    /// <c>hook 888 setup dest=0xCC15 spawn=1</c>.
    /// </summary>
    public static bool RunHook(string assembler, int table = 1) =>
        RunHook(FieldHookAsm.AssembleHook(assembler), table);

    /// <summary>Queue a raw 20-byte hook row (<see cref="FieldHookAsm.HookRowSize"/>).</summary>
    public static bool RunHook(byte[] row, int table = 1)
    {
        if (row is not { Length: FieldHookAsm.HookRowSize })
        {
            return false;
        }

        var pin = System.Runtime.InteropServices.GCHandle.Alloc(
            row, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            return Native?.RunField(3, 0, table == 2 ? 2 : 1, pin.AddrOfPinnedObject(), row.Length) > 0;
        }
        finally
        {
            pin.Free();
        }
    }

    private static byte[] AssembleScript(string assembler, int scriptId)
    {
        var text = assembler ?? "";
        if (!text.TrimStart().StartsWith("script", StringComparison.OrdinalIgnoreCase))
        {
            text = $"script 0x{scriptId:X4}\n{text}";
        }

        return EnsureScriptYield(FieldScriptAsm.Assemble(text, scriptId));
    }

    private static byte[] EnsureScriptYield(byte[]? bytecode)
    {
        if (bytecode is not { Length: > 0 })
        {
            return [];
        }

        if (bytecode.Length >= 2 && (bytecode[^1] & 0xF0) == 0xF0)
        {
            return bytecode;
        }

        var n = new byte[bytecode.Length + 2];
        Buffer.BlockCopy(bytecode, 0, n, 0, bytecode.Length);
        n[^2] = (byte)(FieldScriptAsm.YieldWord & 0xFF);
        n[^1] = (byte)(FieldScriptAsm.YieldWord >> 8);
        return n;
    }

    /// <summary>
    /// SoftHD <c>+0x1A620</c>: FNV-1a of the live PS1 VRAM texels at
    /// tpage/UV. Same 32-bit value as the anim spriteinfo footer key
    /// (sign-extended). False if VRAM is not mapped yet.
    /// </summary>
    public static bool TryHashPs1Sprite(int tpage, int u, int v, int width, int height, out uint key)
    {
        key = 0;
        return Native?.HashPs1Sprite(tpage, u, v, width, height, out key) > 0 && key != 0;
    }

    /// <summary>
    /// Copy one 1024×512 16-bit VRAM buffer (1 MiB).
    /// <paramref name="which"/> 0 is <c>[0x63F880]</c>, 1 is <c>[0x63F884]</c>.
    /// </summary>
    public static bool TryReadPs1Vram(int which, out ushort[] words)
    {
        words = [];
        const int bytes = 0x100000;
        var raw = new byte[bytes];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(raw, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (Native?.ReadPs1Vram(which, pin.AddrOfPinnedObject(), bytes) is not 0x100000)
            {
                return false;
            }
        }
        finally
        {
            pin.Free();
        }

        words = new ushort[bytes / 2];
        Buffer.BlockCopy(raw, 0, words, 0, bytes);
        return true;
    }

    /// <summary>
    /// Hash a sec[23] part: cookie <c>u,v</c> + part size/tpage.
    /// Tries raw / <c>&amp; 0x1FF</c> tpage and SPRT width <c>w</c> / <c>w-1</c>.
    /// </summary>
    public static bool TryHashSpritePart(MapSpritePart part, out uint key)
    {
        key = 0;
        if (!HdSpriteKey.TryCookieUv(part.Cookie, out var u, out var v))
        {
            return false;
        }

        var tpages = new[] { part.Tpage, part.Tpage & 0x1FF, part.Tpage & HdSpriteKey.TpageMask };
        var widths = part.Width > 1 ? new[] { part.Width, part.Width - 1 } : [part.Width];
        foreach (var tp in tpages)
        {
            foreach (var w in widths)
            {
                if (TryHashPs1Sprite(tp, u, v, w, part.Height, out key))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Hash a part and return the first footer row that matches.
    /// </summary>
    public static bool TryResolveSpritePart(
        MapSpritePart part,
        IReadOnlyList<HdAnimMatch> lookup,
        out int index,
        out uint key)
    {
        index = -1;
        key = 0;
        if (!HdSpriteKey.TryCookieUv(part.Cookie, out var u, out var v))
        {
            return false;
        }

        var tpages = new[] { part.Tpage, part.Tpage & 0x1FF, part.Tpage & HdSpriteKey.TpageMask };
        var widths = part.Width > 1 ? new[] { part.Width, part.Width - 1 } : [part.Width];
        foreach (var tp in tpages)
        {
            foreach (var w in widths)
            {
                if (!TryHashPs1Sprite(tp, u, v, w, part.Height, out key))
                {
                    continue;
                }

                if (HdSpriteKey.TryFindIndex(lookup, key, out index))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static INativeGame? Native { get; set; }
}

/// <summary>
/// Party stash bytes at <c>stash_base + itemId - 1</c> (vanilla <see cref="Item"/> ids).
/// Cap 99. Re-resolves after title and slot load (do not cache a pointer across loads).
/// </summary>
public sealed class GameStash
{
    public const int QuantityCap = 99;

    public bool Add(int itemId, int quantity = 1)
    {
        return Game.Native?.StashAdd(itemId, quantity) > 0;
    }

    public bool Add(Item item, int quantity = 1) => Add((int)item, quantity);

    /// <summary>Current quantity, or 0 if the stash is not resolved.</summary>
    public int Get(int itemId)
    {
        var n = Game.Native?.StashGet(itemId) ?? -1;
        return n < 0 ? 0 : n;
    }

    public int Get(Item item) => Get((int)item);
}

/// <summary>Party gold dword at GoldPtr+4. Cap 9,999,999.</summary>
public sealed class GameGold
{
    public const int Cap = 9_999_999;

    public bool Add(int amount)
    {
        return Game.Native?.GoldAdd(amount) > 0;
    }

    public int Get()
    {
        var n = Game.Native?.GoldGet() ?? -1;
        return n < 0 ? 0 : n;
    }
}

/// <summary>
/// Scenario flags: byte <c>eventId &gt;&gt; 3</c>, bit <c>1 &lt;&lt; (7 - (eventId &amp; 7))</c>
/// in the blob at <c>[grandia.exe+0x318BD8]</c> (also captured from flag writes).
/// </summary>
public sealed class GameFlags
{
    public bool IsSet(uint eventId)
    {
        return Game.Native?.FlagGet(eventId) > 0;
    }

    public bool Set(uint eventId, bool value = true)
    {
        return Game.Native?.FlagSet(eventId, value ? 1 : 0) > 0;
    }
}

/// <summary>
/// Field roster at MapObj+0x0A (1 Justin, 2 Feena, 3 Sue, 4 Gadwin, 5 Rapp,
/// 6 Milda, 7 Guido, 8 Liete). <see cref="SetIds"/> seeds
/// <see cref="OnBattleLoadAttribute"/> only — it does not write the field object
/// and does not change Status/stash/assign. Menus use <see cref="OnMenuOpenAttribute"/>.
/// </summary>
public sealed class GameParty
{
    public const int SlotCount = 4;
    public const int MinCharId = 1;
    public const int MaxCharId = 8;

    /// <summary>Live field slot 0..3, or 0 if unresolved.</summary>
    public int Get(int slot)
    {
        var n = Game.Native?.PartyGet(slot) ?? -1;
        return n < 0 ? 0 : n;
    }

    public int[] GetIds()
    {
        return [Get(0), Get(1), Get(2), Get(3)];
    }

    /// <summary>
    /// Default seed for every <see cref="OnBattleLoadAttribute"/> until cleared.
    /// Empty or all-zero clears it. Contiguous 1..4 ids (1–8).
    /// Mutating <see cref="BattleLoadEvent.Party"/> does not change this seed.
    /// Menus are <see cref="OnMenuOpenAttribute"/>.
    /// </summary>
    public bool SetIds(params int[] ids)
    {
        var a = 0;
        var b = 0;
        var c = 0;
        var d = 0;
        if (ids is { Length: > 0 })
        {
            a = ids.Length > 0 ? ids[0] : 0;
            b = ids.Length > 1 ? ids[1] : 0;
            c = ids.Length > 2 ? ids[2] : 0;
            d = ids.Length > 3 ? ids[3] : 0;
        }

        return Game.Native?.PartySetIds(a, b, c, d) > 0;
    }

    public void ClearOverride()
    {
        SetIds();
    }

    /// <summary>
    /// Leader walk XYZ in table-1 <c>aabb=</c> units (actor 16.16 at +0x68/+0x6C/+0x70).
    /// False off the field or before actors are live.
    /// </summary>
    public bool TryGetPosition(out WalkPos pos)
    {
        pos = default;
        if (Game.Native?.PartyWalkGet(out var x, out var y, out var z) is not > 0)
        {
            return false;
        }

        pos = new WalkPos(x, y, z);
        return true;
    }

    public WalkPos? GetPosition() => TryGetPosition(out var pos) ? pos : null;
}

/// <summary>
/// Field camera look-at in the same walk units as <see cref="GameParty.TryGetPosition"/>.
/// 16.16 at <c>[0x71931C]/[0x719320]/[0x719324]</c> (RVA <c>0x31931C</c>).
/// The SFX mixer listens from this point (plus a small look-ahead), so
/// volume follows the camera, not the party.
/// </summary>
public sealed class GameCamera
{
    /// <summary>
    /// Camera look-at XYZ in table-1 <c>aabb=</c> units. False if the host
    /// is not bound. Off the field the last values may still read.
    /// </summary>
    public bool TryGetPosition(out WalkPos pos)
    {
        pos = default;
        if (Game.Native?.CameraWalkGet(out var x, out var y, out var z) is not > 0)
        {
            return false;
        }

        pos = new WalkPos(x, y, z);
        return true;
    }

    public WalkPos? GetPosition() => TryGetPosition(out var pos) ? pos : null;
}

/// <summary>Field walk coordinates; same space as zone <c>aabb=</c>.</summary>
public readonly record struct WalkPos(int X, int Y, int Z)
{
    public override string ToString() => $"{X},{Y},{Z}";
}
