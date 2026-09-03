namespace Grandia.Sdk;

public readonly struct OverlayLine
{
    public OverlayLine(string text, uint rgb = OverlayColor.Gold)
    {
        Text = text ?? "";
        Rgb = rgb;
    }

    public string Text { get; }
    public uint Rgb { get; }
}

public static class OverlayColor
{
    public const uint Gold = 0xFFE528;
    public const uint Green = 0x7CFC00;
    public const uint Salmon = 0xFA8072;
    public const uint Gray = 0xA0A0A0;
    public const uint White = 0xE0E0E0;
    public const uint Slate = 0x6A5ACD;
}
