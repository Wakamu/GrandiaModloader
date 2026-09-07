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
}
