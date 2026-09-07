namespace Grandia.Sdk;

internal interface INativeGame
{
    int StashAdd(int itemId, int delta);
    int StashGet(int itemId);
    int GoldAdd(int amount);
    int GoldGet();
    int FlagGet(uint eventId);
    int FlagSet(uint eventId, int value);
    int PartyGet(int slot);
    int PartySetIds(int a, int b, int c, int d);
    int TurboGet();
    int TurboSet(int level);
    int TurboOverrideGet();
    int TurboOverrideSet(int level);
    int EncountersGet();
    int EncountersSet(int off);
    int DebugGet();
    int DebugSet(int on);
    int KeyDown(int vk);
    int ScanDown(int scan);
    int PadPoll();
    int OverlayReady();
    int OverlayToast(string message, int durationMs, uint rgb);
    int OverlayClearToasts();
    int OverlaySetPanel(string joined, uint[] rgbs, int count);
    int OverlayClearPanel();
    int OverlayPanelActive();
    int PartyWalkGet(out int x, out int y, out int z);
    int SetText1(nint data, int len);
}
