namespace Grandia.Sdk;

public sealed class Hook
{
    private string? _line;

    public Hook(int id) => Id = id;

    public int Id { get; }

    public int Handler { get; internal set; }

    public byte[]? Raw { get; internal set; }

    /// <summary>Full assembler <c>hook …</c> line, without the replace/add prefix.</summary>
    public string? Line
    {
        get => _line;
        set
        {
            _line = value;
            Dirty = true;
        }
    }

    public bool Dirty { get; set; }

    /// <summary>True = append a new row; false = <c>replace id=</c>.</summary>
    public bool Append { get; set; }

    internal void HydrateLine(string line)
    {
        _line = line;
        Dirty = false;
    }

    public override string ToString() =>
        !string.IsNullOrWhiteSpace(_line) ? _line! : $"hook {Id}";

    internal static Hook FromRaw(byte[] raw)
    {
        var hook = new Hook(raw.Length > 0 ? raw[0] : 0)
        {
            Handler = raw.Length > 1 ? raw[1] & 0x3F : 0,
            Raw = raw,
        };
        hook.HydrateLine(FieldHookAsm.FormatHook(raw));
        return hook;
    }
}
