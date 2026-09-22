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
    int OverlayInputOpen(string title, string initial, int maxLength);
    int OverlayInputClose();
    int OverlayInputActive();
    string? OverlayInputText();
    string? OverlayInputTake();
    int OverlayInputTakeCancel();
    int PartyWalkGet(out int x, out int y, out int z);
    int SetText1(nint data, int len);
    int MenuOpen(string title, string joined, int count);
    int MenuClose();
    int MenuIsOpen();
    int MenuCursor();
    int MenuTakeChoice();
    int MenuTakeCancel();
    int MenuOption();
    int StatusGet();
    int WarpTo(int dest, int spawn, int aux9, int auxA);
    int SfxEmitterCount();
    int SfxEmitterRange();
    int SfxEmitterGet(int index, out int recId, out int sfx, out int flags, out int x, out int y,
        out int z, out int muted);
    int SfxEmitterMove(int index, int x, int y, int z);
    int SfxEmitterMute(int index, int muted);
    int SfxEmitterSetSfx(int index, int sfx);
    int SfxEmitterAdd(int recId, int sfx, int flags, int x, int y, int z, int kind, int period,
        int bias);
    int SfxEmitterRemove(int index);
    int SfxEmitterSetFlags(int index, int flags);
    int CameraWalkGet(out int x, out int y, out int z);
    int HashPs1Sprite(int tpage, int u, int v, int width, int height, out uint key);
    int ReadPs1Vram(int which, nint dest, int destLen);
    int RunField(int kind, int id, int table, nint bytes, int len);
    int Quit();
    int XpGet(int kind);
    int XpSet(int kind, int multiplier);
    int CompassGet();
    int CompassSet(int visible);
}
