namespace Grandia.Sdk;

/// <summary>
/// Boot title at +0x7700 — the screen that loads TITLE.DAT and shows
/// "Press Start button". Fires once each time that program starts
/// (first boot and return-to-title), not every frame, and not for the
/// in-game BA38 "Grandia" title card.
/// </summary>
public sealed class TitleScreenEvent
{
}
