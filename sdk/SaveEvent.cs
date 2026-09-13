namespace Grandia.Sdk;

/// <summary>
/// After the vanilla 0xE80 slot body is written. Prefer
/// <see cref="Game.SaveData"/> — the host serializes that bag after this
/// hook. <see cref="Trailer"/> is the JSON snapshot at hook start; a
/// binary overwrite is kept as a reserved legacy blob and does not wipe
/// other keys.
/// </summary>
public sealed class SaveEvent
{
    public SaveEvent(int slot, byte[] trailer)
    {
        Slot = slot;
        Trailer = trailer;
    }

    public int Slot { get; }

    /// <summary>
    /// GMOD payload at hook start (JSON object, max
    /// <see cref="GameSaveData.MaxBytes"/>). Null is empty. Prefer
    /// <see cref="Game.SaveData"/>.
    /// </summary>
    public byte[] Trailer
    {
        get => _trailer;
        set => _trailer = value ?? [];
    }

    private byte[] _trailer = [];
}

public enum LoadPhase
{
    /// <summary>Confirm-Yes (FSM op=3), before Loading UI. Set <see cref="LoadEvent.Allow"/> to veto.</summary>
    ConfirmPeek = 0,

    /// <summary>Vanilla 0xE80 body is in RAM. <see cref="Game.SaveData"/> is already replaced; <see cref="LoadEvent.Data"/> is that bag.</summary>
    Applied = 1,
}

/// <summary>
/// Slot load. Peek can veto; Applied is the safe time to read
/// <see cref="Game.SaveData"/> (already replaced from this slot).
/// </summary>
public sealed class LoadEvent
{
    public LoadEvent(int slot, LoadPhase phase, byte[] trailer, bool allow = true,
        GameSaveData? data = null)
    {
        Slot = slot;
        Phase = phase;
        Trailer = trailer ?? [];
        Allow = allow;
        Data = data ?? GameSaveData.Parse(Trailer);
    }

    public int Slot { get; }

    public LoadPhase Phase { get; }

    /// <summary>Raw GMOD payload (JSON object, or a v1 blob).</summary>
    public byte[] Trailer { get; }

    /// <summary>
    /// Parsed extra bag for this slot. Peek is a detached copy (live
    /// <see cref="Game.SaveData"/> is still the current session). Applied
    /// is the live bag.
    /// </summary>
    public GameSaveData Data { get; }

    public bool Allow { get; set; }
}
