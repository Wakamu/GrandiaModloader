namespace Grandia.Sdk;

/// <summary>
/// D3D11 toasts (top-left), a centered panel, and a text
/// <see cref="Prompt"/>. Same GDI→texture path as Grandiarchipelago.
/// Toasts draw on their own; panel and prompt do not hide them.
/// A prompt replaces the panel until Enter / Escape. Rebuild the
/// panel each time the selection changes; <c>durationMs: 0</c> stays
/// until <see cref="ClearToasts"/>.
/// </summary>
public sealed class GameOverlay
{
    public const int MaxToasts = 8;
    public const int MaxPanelLines = 14;
    public const int MaxInputLength = 64;

    public bool Available => Game.Native?.OverlayReady() > 0;

    public bool PanelActive => Game.Native?.OverlayPanelActive() > 0;

    public void Toast(string message, int durationMs = 5000, uint rgb = OverlayColor.Gold)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        Game.Native?.OverlayToast(message, durationMs, rgb);
    }

    public void ClearToasts()
    {
        Game.Native?.OverlayClearToasts();
    }

    public void SetPanel(params OverlayLine[] lines)
    {
        SetPanel((IReadOnlyList<OverlayLine>)lines);
    }

    public void SetPanel(IReadOnlyList<OverlayLine> lines)
    {
        if (lines is null || lines.Count == 0)
        {
            ClearPanel();
            return;
        }

        var n = Math.Min(lines.Count, MaxPanelLines);
        var texts = new string[n];
        var rgbs = new uint[n];
        for (var i = 0; i < n; i++)
        {
            texts[i] = lines[i].Text.Replace('\n', ' ');
            rgbs[i] = lines[i].Rgb;
        }

        Game.Native?.OverlaySetPanel(string.Join('\n', texts), rgbs, n);
    }

    public void SetPanel(IReadOnlyList<string> lines, uint rgb = OverlayColor.Gold)
    {
        if (lines is null || lines.Count == 0)
        {
            ClearPanel();
            return;
        }

        var n = Math.Min(lines.Count, MaxPanelLines);
        var packed = new OverlayLine[n];
        for (var i = 0; i < n; i++)
        {
            packed[i] = new OverlayLine(lines[i], rgb);
        }

        SetPanel(packed);
    }

    public void ClearPanel()
    {
        Game.Native?.OverlayClearPanel();
    }

    public bool InputActive => Game.Native?.OverlayInputActive() > 0;

    /// <summary>Live buffer while the prompt is open.</summary>
    public string Text => Game.Native?.OverlayInputText() ?? "";

    private Action<string>? _onSubmit;
    private Action? _onCancel;

    /// <summary>
    /// Open a text field on the D3D overlay. Input is swallowed until
    /// Enter submits or Escape cancels. False if the host is not bound.
    /// </summary>
    public bool Prompt(string title, string initial = "", int maxLength = 32) =>
        Prompt(new OverlayInput(title, initial, maxLength));

    public bool Prompt(string title, Action<string> onSubmit, string initial = "", int maxLength = 32) =>
        Prompt(new OverlayInput(title, initial, maxLength), onSubmit);

    public bool Prompt(string title, string initial, Action<string> onSubmit, Action? onCancel = null) =>
        Prompt(new OverlayInput(title, initial), onSubmit, onCancel);

    public bool Prompt(OverlayInput input, Action<string>? onSubmit = null, Action? onCancel = null)
    {
        if (Game.Native is null || input is null)
        {
            return false;
        }

        _onSubmit = onSubmit ?? input.OnSubmit;
        _onCancel = onCancel ?? input.OnCancel;
        if (Game.Native.OverlayInputOpen(input.Title ?? "", input.Value ?? "", input.MaxLength) > 0)
        {
            return true;
        }

        _onSubmit = null;
        _onCancel = null;
        return false;
    }

    /// <summary>Close without submitting. Does not raise <see cref="TakeInputCancel"/> or callbacks.</summary>
    public void CloseInput()
    {
        _onSubmit = null;
        _onCancel = null;
        Game.Native?.OverlayInputClose();
    }

    /// <summary>Submitted text since the last take; null if none. Unused when a submit callback is set.</summary>
    public string? TakeInput() => Game.Native?.OverlayInputTake();

    /// <summary>True once if Escape / Circle closed the prompt since the last take.</summary>
    public bool TakeInputCancel() => Game.Native?.OverlayInputTakeCancel() > 0;

    internal void DispatchPromptCallbacks()
    {
        if (_onSubmit is null && _onCancel is null)
        {
            return;
        }

        try
        {
            var text = Game.Native?.OverlayInputTake();
            if (text is not null)
            {
                var submit = _onSubmit;
                _onSubmit = null;
                _onCancel = null;
                submit?.Invoke(text);
                return;
            }

            if (Game.Native?.OverlayInputTakeCancel() > 0)
            {
                var cancel = _onCancel;
                _onSubmit = null;
                _onCancel = null;
                cancel?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _onSubmit = null;
            _onCancel = null;
            Game.Log.Warn($"Overlay.Prompt: {ex.Message}");
        }
    }
}
