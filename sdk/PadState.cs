namespace Grandia.Sdk;

/// <summary>
/// First connected XInput pad. Same bits as the Archipelago Select+shoulder binds.
/// </summary>
public readonly struct PadState
{
    public const int TriggerThreshold = 30;

    public PadState(int buttons, int leftTrigger, int rightTrigger)
    {
        Buttons = (ushort)buttons;
        LeftTrigger = (byte)leftTrigger;
        RightTrigger = (byte)rightTrigger;
    }

    public ushort Buttons { get; }
    public byte LeftTrigger { get; }
    public byte RightTrigger { get; }

    public bool DpadUp => (Buttons & 0x0001) != 0;
    public bool DpadDown => (Buttons & 0x0002) != 0;
    public bool DpadLeft => (Buttons & 0x0004) != 0;
    public bool DpadRight => (Buttons & 0x0008) != 0;
    public bool Start => (Buttons & 0x0010) != 0;
    public bool Select => (Buttons & 0x0020) != 0;
    public bool L3 => (Buttons & 0x0040) != 0;
    public bool R3 => (Buttons & 0x0080) != 0;
    public bool L1 => (Buttons & 0x0100) != 0;
    public bool R1 => (Buttons & 0x0200) != 0;
    public bool Cross => (Buttons & 0x1000) != 0;
    public bool Circle => (Buttons & 0x2000) != 0;
    public bool Square => (Buttons & 0x4000) != 0;
    public bool Triangle => (Buttons & 0x8000) != 0;
    public bool L2 => LeftTrigger >= TriggerThreshold;
    public bool R2 => RightTrigger >= TriggerThreshold;
}
