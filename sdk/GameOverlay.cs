namespace Grandia.Sdk;

/// <summary>
/// D3D11 toasts (top-left) and a centered panel. Same GDI→texture path as
/// Grandiarchipelago. Panel replaces toast drawing while active. Rebuild the
/// panel each time the selection changes; <c>durationMs: 0</c> stays until
/// <see cref="ClearToasts"/>.
/// </summary>
public sealed class GameOverlay
{
    public const int MaxToasts = 8;
    public const int MaxPanelLines = 14;

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
}
