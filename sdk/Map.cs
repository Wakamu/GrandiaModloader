using System.Text;

namespace Grandia.Sdk;

public sealed class Map
{
    private readonly Dictionary<int, Script> _scripts = [];
    private IReadOnlyList<MapEncounter> _encounters = [];

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

    /// <summary>
    /// Fights on this map: sec[7] handler 0x19 (<c>scripted_battle</c>) and
    /// field wanderers (sec[30] + sec[8] kind 2). Scripted rows include
    /// table-2 <c>call_hook N</c>, table-3 <c>call_hook N alt</c>, and table-1
    /// AABB. Wanderers have <see cref="MapEncounter.Scripted"/> false,
    /// <see cref="MapEncounter.EncounterRow"/> (Bugs = 9), position, and
    /// pack pairs from sec[8] <c>+0x10</c>. Species form-rows need
    /// <c>SpeciesMap</c>, which is not ready at <c>OnMapLoad</c> — pairs are
    /// catalog index × count.
    /// </summary>
    public IReadOnlyList<MapEncounter> Encounters
    {
        get
        {
            EnsureEncounters?.Invoke();
            return _encounters;
        }
    }

    /// <summary>Runtime sets this so <see cref="Scripts"/> / <see cref="GetScript"/> dump SCN only if used.</summary>
    internal Action? EnsureScripts { get; set; }

    /// <summary>Runtime sets this so <see cref="Encounters"/> parse sec[7]/[8]/[30] only if used.</summary>
    internal Action? EnsureEncounters { get; set; }

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

    internal void HydrateEncounters(IEnumerable<MapEncounter> stock)
    {
        EnsureEncounters = null;
        _encounters = stock.ToList();
    }

    /// <summary>Attach vanilla bytecode without decoding. First <see cref="Script.Lines"/> / mutate decodes.</summary>
    internal void AttachBytecode(IEnumerable<(int Id, byte[] Bytes)> scripts, string stem)
    {
        EnsureScripts = null;
        foreach (var (id, bytes) in scripts)
        {
            if (!_scripts.TryGetValue(id, out var script))
            {
                script = new Script(id);
                _scripts[id] = script;
            }

            script.AttachVanilla(bytes, stem);
        }
    }

    /// <summary>First unused table-2 hook id on this map (1–255).</summary>
    public int NextHookId() => Hooks.NextId();

    /// <summary>
    /// Append a table-2 row. Omit <paramref name="id"/> to take the first unused id.
    /// The <c>hook N</c> token in <paramref name="line"/> is rewritten to match.
    /// Scripted fights: <c>scripted_battle table=0x61 5x1</c>, then <c>call_hook N</c>
    /// (use <c>alt</c> only when replacing a table-3 stock row).
    /// </summary>
    public Hook AddHook(string line, int? id = null)
    {
        var hid = id ?? Hooks.NextId();
        var hook = Hooks.Add(hid);
        hook.Line = FieldHookAsm.NormalizeHookLine(line, hid);
        return hook;
    }

    public Hook AddHook(int id, string line) => AddHook(line, id);

    /// <summary>
    /// Append a table-1 zone. <paramref name="dest"/> is only the match key for
    /// <see cref="ReplaceZone"/> (setup warps). Cam-word / chest rows use dest 0.
    /// The line is the same grammar as <c>field_hook_asm</c>
    /// (<c>zone 0 setup dest=… aabb=…</c> / <c>cam_word</c> / <c>chest</c>).
    /// </summary>
    public Zone AddZone(int dest, string line)
    {
        var zone = Zones.Add(dest);
        zone.Line = FieldHookAsm.NormalizeZoneLine(line);
        return zone;
    }

    /// <summary>Append a table-1 zone from a full <c>zone …</c> line (or the payload after <c>zone N</c>).</summary>
    public Zone AddZone(string line)
    {
        var text = FieldHookAsm.NormalizeZoneLine(line);
        var dest = 0;
        try
        {
            dest = FieldHookAsm.SetupDest(FieldHookAsm.AssembleZone(text));
            if (dest < 0)
            {
                dest = 0;
            }
        }
        catch (ArgumentException)
        {
        }

        return AddZone(dest, text);
    }

    /// <summary>
    /// Replace the table-1 setup-warp whose dest map is <paramref name="dest"/>.
    /// For cam-word / chest rows, set <see cref="Zone.Line"/> on the hydrated item instead.
    /// </summary>
    public Zone ReplaceZone(int dest, string line)
    {
        var zone = Zones.Replace(dest);
        zone.Line = FieldHookAsm.NormalizeZoneLine(line);
        return zone;
    }

    /// <summary>Drop a hydrated table-1 row (by index, or the zone object from <see cref="ZoneTable.Items"/>).</summary>
    public void RemoveZone(Zone zone) => zone.Remove();

    public void RemoveZone(int index) => Zones.RemoveAt(index);

    /// <summary>Replace an existing table-2 row by id.</summary>
    public Hook ReplaceHook(int id, string line)
    {
        var hook = Hooks.Replace(id);
        hook.Line = FieldHookAsm.NormalizeHookLine(line, id);
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
            foreach (var zone in Zones.Items.Where(z => z.Dirty && !z.Removed && !string.IsNullOrWhiteSpace(z.Line)))
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
}
