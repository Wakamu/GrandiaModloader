namespace Grandia.Sdk;

/// <summary>
/// After the vanilla 0xE80 slot body is written. Set <see cref="Trailer"/> to persist
/// extra bytes (GMOD envelope after the body). Start value is the last loaded trailer.
/// </summary>
public sealed class SaveEvent
{
    public SaveEvent(int slot, byte[] trailer)
    {
        Slot = slot;
        Trailer = trailer;
    }

    public int Slot { get; }

    /// <summary>Payload after the GMOD envelope (max 1024). Null is treated as empty.</summary>
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

    /// <summary>Vanilla 0xE80 body is in RAM. Restore party / AP index from <see cref="LoadEvent.Trailer"/>.</summary>
    Applied = 1,
}

/// <summary>
/// Slot load. Peek can veto; Applied is the safe time to restore RAM from the trailer.
/// </summary>
public sealed class LoadEvent
{
    public LoadEvent(int slot, LoadPhase phase, byte[] trailer, bool allow = true)
    {
        Slot = slot;
        Phase = phase;
        Trailer = trailer ?? [];
        Allow = allow;
    }

    public int Slot { get; }

    public LoadPhase Phase { get; }

    public byte[] Trailer { get; }

    public bool Allow { get; set; }
}
