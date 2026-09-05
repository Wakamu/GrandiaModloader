namespace Grandia.Sdk;

/// <summary>
/// Field VM is about to run a type-1 or type-8 dialogue op (dispatch at
/// <c>+0x6F174</c>). <see cref="Markup"/> is decoded on first read and
/// encoded only if you assign it. Encode/decode is in-process C# —
/// no <c>field_tools</c>. Set <see cref="Skip"/> to drop this op and
/// keep decoding the rest of the script.
/// </summary>
public sealed class DialogueEvent
{
    private readonly byte[] _payload;
    private readonly string? _mapStem;
    private string? _markup;
    private bool _dirty;

    internal DialogueEvent(MapId map, int scriptId, int opIndex, DialogueKind kind, byte[] payload,
        string? mapStem)
    {
        Map = map;
        ScriptId = scriptId;
        OpIndex = opIndex;
        Kind = kind;
        _payload = payload ?? [];
        _mapStem = mapStem;
    }

    public MapId Map { get; }

    public int ScriptId { get; }

    /// <summary>0-based dialogue-op index in this script, or -1 if unknown.</summary>
    public int OpIndex { get; }

    public DialogueKind Kind { get; }

    /// <summary>
    /// Do not show this box. The decoder consumes the opcode and continues
    /// the script (same as stripping type-1 / type-8). Wins over
    /// <see cref="Markup"/>. A skipped menu line still leaves later
    /// <c>pick</c> rungs in the script.
    /// </summary>
    public bool Skip { get; set; }

    /// <summary>
    /// Linear markup for this op. First get tokenizes the payload; assign to
    /// rewrite this showing. Unread and unassigned is a no-op.
    /// </summary>
    public string Markup
    {
        get => _markup ??= DialogueMarkup.FromPayload(_payload, Kind == DialogueKind.Type8, _mapStem);
        set
        {
            _markup = value ?? "";
            _dirty = true;
        }
    }

    internal bool TryGetReplacement(out byte[] dest)
    {
        dest = [];
        if (!_dirty)
        {
            return false;
        }

        dest = DialogueMarkup.ToPayload(_payload, _markup ?? "", Kind == DialogueKind.Type8, _mapStem);
        return dest.Length > 0;
    }
}
