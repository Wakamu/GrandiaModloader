namespace Grandia.Sdk;

/// <summary>Win32 virtual-key codes for <see cref="GameInput.KeyDown"/>.</summary>
public static class Key
{
    public const int F4 = 0x73;
    public const int F5 = 0x74;
    public const int F6 = 0x75;
    public const int F7 = 0x76;
    public const int F8 = 0x77;
    public const int RightControl = 0xA3;

    /// <summary>US tilde / OEM_3. Prefer <see cref="GameInput.ScanDown"/> with scan <c>0x29</c> for ².</summary>
    public const int Oem3 = 0xC0;
}
