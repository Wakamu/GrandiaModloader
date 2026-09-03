using System.Text;

namespace Grandia.Sdk;

public sealed class Map
{
    private readonly Dictionary<int, Script> _scripts = [];

    public Map(MapId id)
    {
        Id = id;
        Stem = id.ToString();
        Hooks = new HookTable();
        Zones = new ZoneTable();
    }

    public Map(string stem)
    {
        Stem = stem.Trim().ToUpperInvariant();
        Id = MapId.Parse(Stem);
        Hooks = new HookTable();
        Zones = new ZoneTable();
    }

    public MapId Id { get; }

    public string Stem { get; }

    public HookTable Hooks { get; }

    public ZoneTable Zones { get; }

    /// <summary>Runtime sets this so <see cref="Scripts"/> / <see cref="GetScript"/> dump SCN only if used.</summary>
    internal Action? EnsureScripts { get; set; }

    public IReadOnlyList<Script> Scripts
    {
        get
        {
            EnsureScripts?.Invoke();
            return _scripts.Values.OrderBy(s => s.Id).ToList();
        }
    }

    public bool Dirty =>
        _scripts.Values.Any(s => s.Dirty) || Hooks.Dirty || Zones.Dirty;

    public Script GetScript(int id)
    {
        EnsureScripts?.Invoke();
        if (!_scripts.TryGetValue(id, out var script))
        {
            script = new Script(id);
            _scripts[id] = script;
        }

        return script;
    }

    public Script AddScript(int id) => GetScript(id);

    /// <summary>Load existing SCN scripts (disassembly). Does not mark the map dirty.</summary>
    public void HydrateScripts(IEnumerable<(int Id, string Text)> scripts)
    {
        EnsureScripts = null;
        foreach (var (id, text) in scripts)
        {
            if (!_scripts.TryGetValue(id, out var script))
            {
                script = new Script(id);
                _scripts[id] = script;
            }

            script.Hydrate(text);
        }
    }

    /// <summary>First unused table-2 hook id on this map (1–255).</summary>
    public int NextHookId() => Hooks.NextId();

    /// <summary>
    /// Append a table-2 row. Omit <paramref name="id"/> to take the first unused id.
    /// The <c>hook N</c> token in <paramref name="line"/> is rewritten to match.
    /// </summary>
    public Hook AddHook(string line, int? id = null)
    {
        var hid = id ?? Hooks.NextId();
        var hook = Hooks.Add(hid);
        hook.Line = WithHookId(line, hid);
        return hook;
    }

    public Hook AddHook(int id, string line) => AddHook(line, id);

    /// <summary>Replace an existing table-2 row by id.</summary>
    public Hook ReplaceHook(int id, string line)
    {
        var hook = Hooks.Replace(id);
        hook.Line = line.Trim();
        return hook;
    }

    public Script ReplaceScript(int id, string disassembly)
    {
        var script = GetScript(id);
        script.Replace(disassembly);
        return script;
    }

    public string ToPatchText()
    {
        if (!Dirty)
        {
            return "";
        }

        var sb = new StringBuilder();
        sb.Append("patch map=").Append(Stem).AppendLine();
        sb.AppendLine();

        if (Zones.Dirty)
        {
            sb.AppendLine("table 1 {");
            foreach (var zone in Zones.Items.Where(z => z.Dirty && !string.IsNullOrWhiteSpace(z.Line)))
            {
                sb.AppendLine(zone.Append ? "add" : $"replace dest=0x{zone.Dest:X}");
                sb.AppendLine(zone.Line);
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (Hooks.Dirty)
        {
            sb.AppendLine("table 2 {");
            foreach (var hook in Hooks.Items.Where(h => h.Dirty && !string.IsNullOrWhiteSpace(h.Line)))
            {
                sb.AppendLine(hook.Append ? "add" : $"replace id={hook.Id}");
                sb.AppendLine(hook.Line);
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        foreach (var script in _scripts.Values.Where(s => s.Dirty).OrderBy(s => s.Id))
        {
            sb.Append("script 0x").Append(script.Id.ToString("X4")).AppendLine(" {");
            sb.Append(script.ToAsm());
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string WithHookId(string line, int id)
    {
        var trimmed = (line ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return $"hook {id}";
        }

        var toks = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (toks[0].Equals("hook", StringComparison.OrdinalIgnoreCase))
        {
            if (toks.Length >= 2 && LooksLikeInt(toks[1]))
            {
                toks[1] = id.ToString();
                return string.Join(' ', toks);
            }

            return toks.Length == 1
                ? $"hook {id}"
                : $"hook {id} " + string.Join(' ', toks.Skip(1));
        }

        return $"hook {id} {trimmed}";
    }

    private static bool LooksLikeInt(string token)
    {
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(token.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out _);
        }

        return int.TryParse(token, out _);
    }
}
