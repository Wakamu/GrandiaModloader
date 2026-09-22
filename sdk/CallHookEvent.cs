namespace Grandia.Sdk;

/// <summary>
/// Field VM is dispatching <c>call_hook</c> (<c>+0x53560</c> → <c>+0x53830</c>).
/// Set <see cref="Row"/> to a 20-byte table-2 record to run that instead of
/// the relocated heap table. Set <see cref="Skip"/> to ignore the call.
/// </summary>
public sealed class CallHookEvent
{
    public CallHookEvent(MapId map, int hookId, int table)
    {
        Map = map;
        HookId = hookId;
        Table = table;
    }

    public MapId Map { get; }

    public int HookId { get; }

    /// <summary>
    /// Engine table argument: 1 = normal <c>call_hook N</c> (sec[7] table 2),
    /// 2 = <c>call_hook N alt</c> (table 3).
    /// </summary>
    public int Table { get; }

    /// <summary>Replacement 20-byte hook row. Null keeps the OnMapLoad cache or vanilla.</summary>
    public byte[]? Row { get; set; }

    /// <summary>
    /// Assembler line (same grammar as <c>Map.AddHook</c>), e.g.
    /// <c>hook 888 setup dest=0xCC15 spawn=1</c>. Set by <see cref="Replace"/>.
    /// </summary>
    public string? Assembler { get; private set; }

    public bool Dirty { get; private set; }

    /// <summary>Do not dispatch this hook.</summary>
    public bool Skip { get; set; }

    /// <summary>
    /// Stop the vanilla hook and run this assembler line instead.
    /// Assembled in-process (no <c>field_tools</c>).
    /// </summary>
    public void Replace(string assembler)
    {
        Assembler = assembler ?? "";
        Dirty = true;
    }
}
