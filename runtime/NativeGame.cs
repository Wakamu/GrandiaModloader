using System.Runtime.InteropServices;
using Grandia.Sdk;

namespace Grandia.Runtime;

[StructLayout(LayoutKind.Sequential)]
public struct HostApiNative
{
    public nint StashAdd;
    public nint StashGet;
    public nint GoldAdd;
    public nint GoldGet;
    public nint FlagGet;
    public nint FlagSet;
    public nint PartyGet;
    public nint PartySetIds;
    public nint TurboGet;
    public nint TurboSet;
    public nint TurboOverrideGet;
    public nint TurboOverrideSet;
    public nint EncountersGet;
    public nint EncountersSet;
    public nint DebugGet;
    public nint DebugSet;
    public nint KeyDown;
    public nint ScanDown;
    public nint PadPoll;
    public nint OverlayReady;
    public nint OverlayToast;
    public nint OverlayClearToasts;
    public nint OverlaySetPanel;
    public nint OverlayClearPanel;
    public nint OverlayPanelActive;
    public nint PartyWalkGet;
    public nint SetText1;
    public nint MenuOpen;
    public nint MenuClose;
    public nint MenuIsOpen;
    public nint MenuCursor;
    public nint MenuTakeChoice;
    public nint MenuTakeCancel;
    public nint MenuOption;
    public nint StatusGet;
    public nint WarpTo;
    public nint OverlayInputOpen;
    public nint OverlayInputClose;
    public nint OverlayInputActive;
    public nint OverlayInputText;
    public nint OverlayInputTake;
    public nint OverlayInputTakeCancel;
    public nint SfxEmitterCount;
    public nint SfxEmitterRange;
    public nint SfxEmitterGet;
    public nint SfxEmitterMove;
    public nint SfxEmitterMute;
    public nint SfxEmitterSetSfx;
    public nint SfxEmitterAdd;
    public nint SfxEmitterRemove;
    public nint SfxEmitterSetFlags;
    public nint CameraWalkGet;
    public nint HashPs1Sprite;
    public nint ReadPs1Vram;
    public nint RunField;
    public nint Quit;
    public nint XpGet;
    public nint XpSet;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int StashAddFn(int itemId, int delta);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int StashGetFn(int itemId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int GoldAddFn(int amount);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int GoldGetFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FlagGetFn(uint eventId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int FlagSetFn(uint eventId, int value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int PartyGetFn(int slot);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int PartySetIdsFn(int a, int b, int c, int d);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int TurboGetFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int TurboSetFn(int level);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int TurboOverrideGetFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int TurboOverrideSetFn(int level);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int EncountersGetFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int EncountersSetFn(int off);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int DebugGetFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int DebugSetFn(int on);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int KeyDownFn(int vk);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int ScanDownFn(int scan);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int PadPollFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayReadyFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayToastFn(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string message, int durationMs, uint rgb);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayClearToastsFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlaySetPanelFn(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string joined,
    [In] uint[] rgbs,
    int count);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayClearPanelFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayPanelActiveFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int PartyWalkGetFn(out int x, out int y, out int z);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SetText1Fn(nint data, int len);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int MenuOpenFn(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string joined,
    int count);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int MenuCloseFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int MenuIsOpenFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int MenuCursorFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int MenuTakeChoiceFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int MenuTakeCancelFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int MenuOptionFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int StatusGetFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int WarpToFn(int dest, int spawn, int aux9, int auxA);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayInputOpenFn(
    [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string initial,
    int maxLength);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayInputCloseFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayInputActiveFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nint OverlayInputTextFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nint OverlayInputTakeFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int OverlayInputTakeCancelFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterCountFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterRangeFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterGetFn(int index, out int recId, out int sfx, out int flags,
    out int x, out int y, out int z, out int muted);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterMoveFn(int index, int x, int y, int z);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterMuteFn(int index, int muted);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterSetSfxFn(int index, int sfx);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterAddFn(int recId, int sfx, int flags, int x, int y, int z, int kind,
    int period, int bias);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterRemoveFn(int index);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SfxEmitterSetFlagsFn(int index, int flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int CameraWalkGetFn(out int x, out int y, out int z);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int HashPs1SpriteFn(int tpage, int u, int v, int width, int height, out uint key);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int ReadPs1VramFn(int which, nint dest, int destLen);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int RunFieldFn(int kind, int id, int table, nint bytes, int len);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int QuitFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int XpGetFn(int kind);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int XpSetFn(int kind, int multiplier);

internal sealed class NativeGame : INativeGame
{
    private readonly StashAddFn _stashAdd;
    private readonly StashGetFn _stashGet;
    private readonly GoldAddFn _goldAdd;
    private readonly GoldGetFn _goldGet;
    private readonly FlagGetFn _flagGet;
    private readonly FlagSetFn _flagSet;
    private readonly PartyGetFn _partyGet;
    private readonly PartySetIdsFn _partySetIds;
    private readonly TurboGetFn _turboGet;
    private readonly TurboSetFn _turboSet;
    private readonly TurboOverrideGetFn _turboOverrideGet;
    private readonly TurboOverrideSetFn _turboOverrideSet;
    private readonly EncountersGetFn _encountersGet;
    private readonly EncountersSetFn _encountersSet;
    private readonly DebugGetFn _debugGet;
    private readonly DebugSetFn _debugSet;
    private readonly KeyDownFn _keyDown;
    private readonly ScanDownFn _scanDown;
    private readonly PadPollFn _padPoll;
    private readonly OverlayReadyFn _overlayReady;
    private readonly OverlayToastFn _overlayToast;
    private readonly OverlayClearToastsFn _overlayClearToasts;
    private readonly OverlaySetPanelFn _overlaySetPanel;
    private readonly OverlayClearPanelFn _overlayClearPanel;
    private readonly OverlayPanelActiveFn _overlayPanelActive;
    private readonly PartyWalkGetFn _partyWalkGet;
    private readonly SetText1Fn? _setText1;
    private readonly MenuOpenFn? _menuOpen;
    private readonly MenuCloseFn? _menuClose;
    private readonly MenuIsOpenFn? _menuIsOpen;
    private readonly MenuCursorFn? _menuCursor;
    private readonly MenuTakeChoiceFn? _menuTakeChoice;
    private readonly MenuTakeCancelFn? _menuTakeCancel;
    private readonly MenuOptionFn? _menuOption;
    private readonly StatusGetFn? _statusGet;
    private readonly WarpToFn? _warpTo;
    private readonly OverlayInputOpenFn? _overlayInputOpen;
    private readonly OverlayInputCloseFn? _overlayInputClose;
    private readonly OverlayInputActiveFn? _overlayInputActive;
    private readonly OverlayInputTextFn? _overlayInputText;
    private readonly OverlayInputTakeFn? _overlayInputTake;
    private readonly OverlayInputTakeCancelFn? _overlayInputTakeCancel;
    private readonly SfxEmitterCountFn? _sfxEmitterCount;
    private readonly SfxEmitterRangeFn? _sfxEmitterRange;
    private readonly SfxEmitterGetFn? _sfxEmitterGet;
    private readonly SfxEmitterMoveFn? _sfxEmitterMove;
    private readonly SfxEmitterMuteFn? _sfxEmitterMute;
    private readonly SfxEmitterSetSfxFn? _sfxEmitterSetSfx;
    private readonly SfxEmitterAddFn? _sfxEmitterAdd;
    private readonly SfxEmitterRemoveFn? _sfxEmitterRemove;
    private readonly SfxEmitterSetFlagsFn? _sfxEmitterSetFlags;
    private readonly CameraWalkGetFn? _cameraWalkGet;
    private readonly HashPs1SpriteFn? _hashPs1Sprite;
    private readonly ReadPs1VramFn? _readPs1Vram;
    private readonly RunFieldFn? _runField;
    private readonly QuitFn? _quit;
    private readonly XpGetFn? _xpGet;
    private readonly XpSetFn? _xpSet;

    public NativeGame(HostApiNative api)
    {
        _stashAdd = Marshal.GetDelegateForFunctionPointer<StashAddFn>(api.StashAdd);
        _stashGet = Marshal.GetDelegateForFunctionPointer<StashGetFn>(api.StashGet);
        _goldAdd = Marshal.GetDelegateForFunctionPointer<GoldAddFn>(api.GoldAdd);
        _goldGet = Marshal.GetDelegateForFunctionPointer<GoldGetFn>(api.GoldGet);
        _flagGet = Marshal.GetDelegateForFunctionPointer<FlagGetFn>(api.FlagGet);
        _flagSet = Marshal.GetDelegateForFunctionPointer<FlagSetFn>(api.FlagSet);
        _partyGet = Marshal.GetDelegateForFunctionPointer<PartyGetFn>(api.PartyGet);
        _partySetIds = Marshal.GetDelegateForFunctionPointer<PartySetIdsFn>(api.PartySetIds);
        _turboGet = Marshal.GetDelegateForFunctionPointer<TurboGetFn>(api.TurboGet);
        _turboSet = Marshal.GetDelegateForFunctionPointer<TurboSetFn>(api.TurboSet);
        _turboOverrideGet = Marshal.GetDelegateForFunctionPointer<TurboOverrideGetFn>(api.TurboOverrideGet);
        _turboOverrideSet = Marshal.GetDelegateForFunctionPointer<TurboOverrideSetFn>(api.TurboOverrideSet);
        _encountersGet = Marshal.GetDelegateForFunctionPointer<EncountersGetFn>(api.EncountersGet);
        _encountersSet = Marshal.GetDelegateForFunctionPointer<EncountersSetFn>(api.EncountersSet);
        _debugGet = Marshal.GetDelegateForFunctionPointer<DebugGetFn>(api.DebugGet);
        _debugSet = Marshal.GetDelegateForFunctionPointer<DebugSetFn>(api.DebugSet);
        _keyDown = Marshal.GetDelegateForFunctionPointer<KeyDownFn>(api.KeyDown);
        _scanDown = Marshal.GetDelegateForFunctionPointer<ScanDownFn>(api.ScanDown);
        _padPoll = Marshal.GetDelegateForFunctionPointer<PadPollFn>(api.PadPoll);
        _overlayReady = Marshal.GetDelegateForFunctionPointer<OverlayReadyFn>(api.OverlayReady);
        _overlayToast = Marshal.GetDelegateForFunctionPointer<OverlayToastFn>(api.OverlayToast);
        _overlayClearToasts = Marshal.GetDelegateForFunctionPointer<OverlayClearToastsFn>(api.OverlayClearToasts);
        _overlaySetPanel = Marshal.GetDelegateForFunctionPointer<OverlaySetPanelFn>(api.OverlaySetPanel);
        _overlayClearPanel = Marshal.GetDelegateForFunctionPointer<OverlayClearPanelFn>(api.OverlayClearPanel);
        _overlayPanelActive = Marshal.GetDelegateForFunctionPointer<OverlayPanelActiveFn>(api.OverlayPanelActive);
        _partyWalkGet = Marshal.GetDelegateForFunctionPointer<PartyWalkGetFn>(api.PartyWalkGet);
        _setText1 = api.SetText1 != 0
            ? Marshal.GetDelegateForFunctionPointer<SetText1Fn>(api.SetText1)
            : null;
        _menuOpen = api.MenuOpen != 0
            ? Marshal.GetDelegateForFunctionPointer<MenuOpenFn>(api.MenuOpen)
            : null;
        _menuClose = api.MenuClose != 0
            ? Marshal.GetDelegateForFunctionPointer<MenuCloseFn>(api.MenuClose)
            : null;
        _menuIsOpen = api.MenuIsOpen != 0
            ? Marshal.GetDelegateForFunctionPointer<MenuIsOpenFn>(api.MenuIsOpen)
            : null;
        _menuCursor = api.MenuCursor != 0
            ? Marshal.GetDelegateForFunctionPointer<MenuCursorFn>(api.MenuCursor)
            : null;
        _menuTakeChoice = api.MenuTakeChoice != 0
            ? Marshal.GetDelegateForFunctionPointer<MenuTakeChoiceFn>(api.MenuTakeChoice)
            : null;
        _menuTakeCancel = api.MenuTakeCancel != 0
            ? Marshal.GetDelegateForFunctionPointer<MenuTakeCancelFn>(api.MenuTakeCancel)
            : null;
        _menuOption = api.MenuOption != 0
            ? Marshal.GetDelegateForFunctionPointer<MenuOptionFn>(api.MenuOption)
            : null;
        _statusGet = api.StatusGet != 0
            ? Marshal.GetDelegateForFunctionPointer<StatusGetFn>(api.StatusGet)
            : null;
        _warpTo = api.WarpTo != 0
            ? Marshal.GetDelegateForFunctionPointer<WarpToFn>(api.WarpTo)
            : null;
        _overlayInputOpen = api.OverlayInputOpen != 0
            ? Marshal.GetDelegateForFunctionPointer<OverlayInputOpenFn>(api.OverlayInputOpen)
            : null;
        _overlayInputClose = api.OverlayInputClose != 0
            ? Marshal.GetDelegateForFunctionPointer<OverlayInputCloseFn>(api.OverlayInputClose)
            : null;
        _overlayInputActive = api.OverlayInputActive != 0
            ? Marshal.GetDelegateForFunctionPointer<OverlayInputActiveFn>(api.OverlayInputActive)
            : null;
        _overlayInputText = api.OverlayInputText != 0
            ? Marshal.GetDelegateForFunctionPointer<OverlayInputTextFn>(api.OverlayInputText)
            : null;
        _overlayInputTake = api.OverlayInputTake != 0
            ? Marshal.GetDelegateForFunctionPointer<OverlayInputTakeFn>(api.OverlayInputTake)
            : null;
        _overlayInputTakeCancel = api.OverlayInputTakeCancel != 0
            ? Marshal.GetDelegateForFunctionPointer<OverlayInputTakeCancelFn>(api.OverlayInputTakeCancel)
            : null;
        _sfxEmitterCount = api.SfxEmitterCount != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterCountFn>(api.SfxEmitterCount)
            : null;
        _sfxEmitterRange = api.SfxEmitterRange != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterRangeFn>(api.SfxEmitterRange)
            : null;
        _sfxEmitterGet = api.SfxEmitterGet != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterGetFn>(api.SfxEmitterGet)
            : null;
        _sfxEmitterMove = api.SfxEmitterMove != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterMoveFn>(api.SfxEmitterMove)
            : null;
        _sfxEmitterMute = api.SfxEmitterMute != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterMuteFn>(api.SfxEmitterMute)
            : null;
        _sfxEmitterSetSfx = api.SfxEmitterSetSfx != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterSetSfxFn>(api.SfxEmitterSetSfx)
            : null;
        _sfxEmitterAdd = api.SfxEmitterAdd != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterAddFn>(api.SfxEmitterAdd)
            : null;
        _sfxEmitterRemove = api.SfxEmitterRemove != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterRemoveFn>(api.SfxEmitterRemove)
            : null;
        _sfxEmitterSetFlags = api.SfxEmitterSetFlags != 0
            ? Marshal.GetDelegateForFunctionPointer<SfxEmitterSetFlagsFn>(api.SfxEmitterSetFlags)
            : null;
        _cameraWalkGet = api.CameraWalkGet != 0
            ? Marshal.GetDelegateForFunctionPointer<CameraWalkGetFn>(api.CameraWalkGet)
            : null;
        _hashPs1Sprite = api.HashPs1Sprite != 0
            ? Marshal.GetDelegateForFunctionPointer<HashPs1SpriteFn>(api.HashPs1Sprite)
            : null;
        _readPs1Vram = api.ReadPs1Vram != 0
            ? Marshal.GetDelegateForFunctionPointer<ReadPs1VramFn>(api.ReadPs1Vram)
            : null;
        _runField = api.RunField != 0
            ? Marshal.GetDelegateForFunctionPointer<RunFieldFn>(api.RunField)
            : null;
        _quit = api.Quit != 0
            ? Marshal.GetDelegateForFunctionPointer<QuitFn>(api.Quit)
            : null;
        _xpGet = api.XpGet != 0
            ? Marshal.GetDelegateForFunctionPointer<XpGetFn>(api.XpGet)
            : null;
        _xpSet = api.XpSet != 0
            ? Marshal.GetDelegateForFunctionPointer<XpSetFn>(api.XpSet)
            : null;
    }

    public int StashAdd(int itemId, int delta) => _stashAdd(itemId, delta);

    public int StashGet(int itemId) => _stashGet(itemId);

    public int GoldAdd(int amount) => _goldAdd(amount);

    public int GoldGet() => _goldGet();

    public int FlagGet(uint eventId) => _flagGet(eventId);

    public int FlagSet(uint eventId, int value) => _flagSet(eventId, value);

    public int PartyGet(int slot) => _partyGet(slot);

    public int PartySetIds(int a, int b, int c, int d) => _partySetIds(a, b, c, d);

    public int TurboGet() => _turboGet();

    public int TurboSet(int level) => _turboSet(level);

    public int TurboOverrideGet() => _turboOverrideGet();

    public int TurboOverrideSet(int level) => _turboOverrideSet(level);

    public int EncountersGet() => _encountersGet();

    public int EncountersSet(int off) => _encountersSet(off);

    public int DebugGet() => _debugGet();

    public int DebugSet(int on) => _debugSet(on);

    public int KeyDown(int vk) => _keyDown(vk);

    public int ScanDown(int scan) => _scanDown(scan);

    public int PadPoll() => _padPoll();

    public int OverlayReady() => _overlayReady();

    public int OverlayToast(string message, int durationMs, uint rgb) =>
        _overlayToast(message, durationMs, rgb);

    public int OverlayClearToasts() => _overlayClearToasts();

    public int OverlaySetPanel(string joined, uint[] rgbs, int count) =>
        _overlaySetPanel(joined, rgbs, count);

    public int OverlayClearPanel() => _overlayClearPanel();

    public int OverlayPanelActive() => _overlayPanelActive();

    public int PartyWalkGet(out int x, out int y, out int z) => _partyWalkGet(out x, out y, out z);

    public int SetText1(nint data, int len) => _setText1?.Invoke(data, len) ?? 0;

    public int MenuOpen(string title, string joined, int count) =>
        _menuOpen?.Invoke(title, joined, count) ?? 0;

    public int MenuClose() => _menuClose?.Invoke() ?? 0;

    public int MenuIsOpen() => _menuIsOpen?.Invoke() ?? 0;

    public int MenuCursor() => _menuCursor?.Invoke() ?? 0;

    public int MenuTakeChoice() => _menuTakeChoice?.Invoke() ?? -1;

    public int MenuTakeCancel() => _menuTakeCancel?.Invoke() ?? 0;

    public int MenuOption() => _menuOption?.Invoke() ?? 0;

    public int StatusGet() => _statusGet?.Invoke() ?? 0;

    public int WarpTo(int dest, int spawn, int aux9, int auxA) =>
        _warpTo?.Invoke(dest, spawn, aux9, auxA) ?? 0;

    public int OverlayInputOpen(string title, string initial, int maxLength) =>
        _overlayInputOpen?.Invoke(title, initial, maxLength) ?? 0;

    public int OverlayInputClose() => _overlayInputClose?.Invoke() ?? 0;

    public int OverlayInputActive() => _overlayInputActive?.Invoke() ?? 0;

    public string? OverlayInputText()
    {
        var p = _overlayInputText?.Invoke() ?? 0;
        return p == 0 ? "" : Marshal.PtrToStringUTF8(p);
    }

    public string? OverlayInputTake()
    {
        var p = _overlayInputTake?.Invoke() ?? 0;
        return p == 0 ? null : Marshal.PtrToStringUTF8(p);
    }

    public int OverlayInputTakeCancel() => _overlayInputTakeCancel?.Invoke() ?? 0;

    public int SfxEmitterCount() => _sfxEmitterCount?.Invoke() ?? 0;

    public int SfxEmitterRange() => _sfxEmitterRange?.Invoke() ?? 0;

    public int SfxEmitterGet(int index, out int recId, out int sfx, out int flags, out int x,
        out int y, out int z, out int muted)
    {
        recId = sfx = flags = x = y = z = muted = 0;
        return _sfxEmitterGet?.Invoke(index, out recId, out sfx, out flags, out x, out y, out z,
            out muted) ?? 0;
    }

    public int SfxEmitterMove(int index, int x, int y, int z) =>
        _sfxEmitterMove?.Invoke(index, x, y, z) ?? 0;

    public int SfxEmitterMute(int index, int muted) => _sfxEmitterMute?.Invoke(index, muted) ?? 0;

    public int SfxEmitterSetSfx(int index, int sfx) => _sfxEmitterSetSfx?.Invoke(index, sfx) ?? 0;

    public int SfxEmitterAdd(int recId, int sfx, int flags, int x, int y, int z, int kind,
        int period, int bias) =>
        _sfxEmitterAdd?.Invoke(recId, sfx, flags, x, y, z, kind, period, bias) ?? -1;

    public int SfxEmitterRemove(int index) => _sfxEmitterRemove?.Invoke(index) ?? 0;

    public int SfxEmitterSetFlags(int index, int flags) =>
        _sfxEmitterSetFlags?.Invoke(index, flags) ?? 0;

    public int CameraWalkGet(out int x, out int y, out int z)
    {
        x = y = z = 0;
        return _cameraWalkGet?.Invoke(out x, out y, out z) ?? 0;
    }

    public int HashPs1Sprite(int tpage, int u, int v, int width, int height, out uint key)
    {
        key = 0;
        return _hashPs1Sprite?.Invoke(tpage, u, v, width, height, out key) ?? 0;
    }

    public int ReadPs1Vram(int which, nint dest, int destLen) =>
        _readPs1Vram?.Invoke(which, dest, destLen) ?? 0;

    public int RunField(int kind, int id, int table, nint bytes, int len) =>
        _runField?.Invoke(kind, id, table, bytes, len) ?? 0;

    public int Quit() => _quit?.Invoke() ?? 0;

    public int XpGet(int kind) => _xpGet?.Invoke(kind) ?? 1;

    public int XpSet(int kind, int multiplier) => _xpSet?.Invoke(kind, multiplier) ?? 1;
}
