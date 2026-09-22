namespace Grandia.Sdk;

/// <summary>
/// About 60 Hz. Poll <see cref="Pad"/> / <see cref="Game.Input"/> and call
/// <see cref="Game.Turbo"/> / <see cref="Game.Encounters"/> / <see cref="Game.Debug"/> /
/// <see cref="Game.Compass"/>.
/// Set <see cref="BlockGameInput"/> (or <see cref="Consume"/>) so this pad
/// update is not applied to field / menus — use while an overlay panel is open.
/// <see cref="GameOverlay.Prompt"/> blocks on its own until Enter / Escape.
/// </summary>
public sealed class TickEvent
{
    public TickEvent(PadState pad)
    {
        Pad = pad;
    }

    public PadState Pad { get; }

    /// <summary>
    /// Swallow this pad update so field, menus, and the title New Game /
    /// Continue / Options cursor do not see it. <see cref="Pad"/> is still the
    /// real XInput state.
    /// </summary>
    public bool BlockGameInput { get; set; }

    public void Consume() => BlockGameInput = true;
}
