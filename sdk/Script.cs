using System.Text;

namespace Grandia.Sdk;

/// <summary>
/// One SCN script. Ops are the existing field assembler mnemonics — not a new language.
/// </summary>
public sealed class Script
{
    private readonly List<string> _lines = [];

    public Script(int id) => Id = id;

    public int Id { get; }

    public bool Dirty { get; private set; }

    public IReadOnlyList<string> Lines => _lines;

    public void Clear()
    {
        _lines.Clear();
        Dirty = true;
    }

    /// <summary>
    /// Replace this script with a full disassembly dump (the
    /// <c>script 0xNNNN</c> header is optional and ignored; this script's id wins).
    /// </summary>
    /// <summary>Fill from a disassembly dump without marking the script dirty.</summary>
    public void Hydrate(string disassembly)
    {
        Replace(disassembly);
        Dirty = false;
    }

    public void Replace(string disassembly)
    {
        _lines.Clear();
        Dirty = true;
        if (string.IsNullOrWhiteSpace(disassembly))
        {
            return;
        }

        var skippedEnvelope = false;
        foreach (var raw in disassembly.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
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

        // A pasted .patch envelope is `script 0xNNNN {` … `}`. Keep menu/if closers.
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
        var sb = new StringBuilder();
        sb.Append("script 0x").Append(Id.ToString("X4")).AppendLine();
        foreach (var line in _lines)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private void Add(string line)
    {
        _lines.Add(line);
        Dirty = true;
    }
}
