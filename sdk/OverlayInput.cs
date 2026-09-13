namespace Grandia.Sdk;

/// <summary>
/// D3D text prompt. While open, keyboard and pad do not reach the game.
/// Enter / Cross / Start calls <see cref="OnSubmit"/> (or
/// <see cref="GameOverlay.TakeInput"/>); Escape / Circle calls
/// <see cref="OnCancel"/>.
/// </summary>
public sealed class OverlayInput
{
    public OverlayInput(string title = "", string value = "", int maxLength = 32,
        Action<string>? onSubmit = null, Action? onCancel = null)
    {
        Title = title ?? "";
        Value = value ?? "";
        MaxLength = maxLength;
        OnSubmit = onSubmit;
        OnCancel = onCancel;
    }

    public string Title { get; }

    public string Value { get; }

    public int MaxLength { get; }

    public Action<string>? OnSubmit { get; }

    public Action? OnCancel { get; }
}
