using System.Text;

namespace Grandia.Sdk;

/// <summary>
/// One SCN script. Ops are the existing field assembler mnemonics — not a new language.
/// Vanilla bytes decode on first read; assemble only after a write (in-process C#).
/// </summary>
public sealed class Script
{
    private readonly List<string> _lines = [];
    private byte[]? _vanilla;
    private string? _mapStem;
    private bool _decoded;
    private bool _replaced;

    public Script(int id) => Id = id;

    public int Id { get; }

    public bool Dirty { get; private set; }

    public IReadOnlyList<string> Lines
    {
        get
        {
            EnsureDecoded();
            return _lines;
        }
    }

    public void Clear()
    {
        _lines.Clear();
        _decoded = true;
        _replaced = true;
        Dirty = true;
    }

    /// <summary>Fill from a disassembly dump without marking the script dirty.</summary>
    public void Hydrate(string disassembly)
    {
        Replace(disassembly);
        Dirty = false;
        _decoded = true;
        _replaced = false;
    }

    internal void AttachVanilla(byte[] bytecode, string? mapStem)
    {
        _vanilla = bytecode;
        _mapStem = mapStem;
        if (!_replaced && !Dirty)
        {
            _decoded = false;
        }
    }

    public void Replace(string disassembly)
    {
        _lines.Clear();
        Dirty = true;
        _decoded = true;
        _replaced = true;
        if (string.IsNullOrWhiteSpace(disassembly))
        {
            return;
        }

        var skippedEnvelope = false;
        foreach (var raw in disassembly.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            var toks = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length >= 2 && toks[0].Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                skippedEnvelope |= trimmed.Contains('{');
                continue;
            }

            _lines.Add(line);
        }

        if (skippedEnvelope && _lines.Count > 0 && _lines[^1].Trim() == "}")
        {
            _lines.RemoveAt(_lines.Count - 1);
        }
    }

    public void CameraOverlay(int catalogId) => Add($"camera overlay 0x{catalogId:X}");

    public void CameraDeactivateOverlay(int catalogId) =>
        Add($"camera deactivate_overlay 0x{catalogId:X}");

    public void CameraActivate(int sceneId) => Add($"camera activate 0x{sceneId:X}");

    public void CameraDeactivate(int sceneId) => Add($"camera deactivate 0x{sceneId:X}");

    public void Wait(int ticks) => Add($"wait {ticks}");

    public void Yield() => Add("yield");

    public void CallHook(int hookId) => Add($"call_hook {hookId}");

    public void AddLine(string assemblerLine)
    {
        if (string.IsNullOrWhiteSpace(assemblerLine))
        {
            return;
        }

        Add(assemblerLine.Trim());
    }

    public string ToAsm()
    {
        EnsureDecoded();
        var sb = new StringBuilder();
        sb.Append("script 0x").Append(Id.ToString("X4")).AppendLine();
        foreach (var line in _lines)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    internal byte[] ToBytecode() => FieldScriptAsm.Assemble(ToAsm(), Id, _mapStem);

    private void EnsureDecoded()
    {
        if (_decoded || _replaced || _vanilla is not { Length: > 0 })
        {
            return;
        }

        var text = FieldScriptAsm.Disassemble(_vanilla, Id, _mapStem);
        Replace(text);
        Dirty = false;
        _decoded = true;
        _replaced = false;
    }

    private void Add(string line)
    {
        if (!_replaced)
        {
            EnsureDecoded();
        }

        _lines.Add(line);
        Dirty = true;
        _decoded = true;
    }
}
