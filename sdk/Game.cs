namespace Grandia.Sdk;

/// <summary>
/// Live game RAM: stash, gold, flags, party, turbo, encounters, and debug.
/// Bound by GrandiaMod.dll after a save is in memory. Safe to call from
/// hook methods (<see cref="OnTickAttribute"/>, <see cref="OnBattleLoadAttribute"/>, …).
/// </summary>
public static class Game
{
    public static GameStash Stash { get; } = new();

    public static GameGold Gold { get; } = new();

    public static GameFlags Flags { get; } = new();

    public static GameParty Party { get; } = new();

    public static GameTurbo Turbo { get; } = new();

    public static GameEncounters Encounters { get; } = new();

    public static GameDebug Debug { get; } = new();

    public static GameInput Input { get; } = new();

    public static GamePad Pad { get; } = new();

    public static GameOverlay Overlay { get; } = new();

    /// <summary>
    /// Append to <c>GrandiaMod.log</c> next to <c>grandia.exe</c> (same file as
    /// the host). Safe from <see cref="InitAttribute"/> and any hook.
    /// </summary>
    public static GameLog Log { get; } = new();

    internal static INativeGame? Native { get; set; }
}

/// <summary>
/// Party stash bytes at <c>stash_base + itemId - 1</c> (vanilla <see cref="Item"/> ids).
/// Cap 99. Resolves after a save is loaded.
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
}
