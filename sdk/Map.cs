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
        AltHooks = new HookTable();
        Zones = new ZoneTable();
        Sfx = new MapSfxTable();
        Npcs = new MapNpcTable();
        Anims = new MapAnimTable();
        SpriteClips = new MapSpriteClipTable();
        Poses = new MapSpritePoseTable();
        Sprites = new MapSpriteBank();
        Textures = new MapTextureTable();
        CameraPaths = new MapCameraPathTable();
        Camera = new MapCamera();
    }

    public Map(string stem)
    {
        Stem = stem.Trim().ToUpperInvariant();
        Id = MapId.Parse(Stem);
        Hooks = new HookTable();
        AltHooks = new HookTable();
        Zones = new ZoneTable();
        Sfx = new MapSfxTable();
        Npcs = new MapNpcTable();
        Anims = new MapAnimTable();
        SpriteClips = new MapSpriteClipTable();
        Poses = new MapSpritePoseTable();
        Sprites = new MapSpriteBank();
        Textures = new MapTextureTable();
        CameraPaths = new MapCameraPathTable();
        Camera = new MapCamera();
    }

    public MapId Id { get; }

    public string Stem { get; }

    public HookTable Hooks { get; }

    /// <summary>
    /// Sec[7] table 3 (<c>call_hook N alt</c>). Same 20-byte rows /
    /// <see cref="Hook.Line"/> as <see cref="Hooks"/>. Ids are per-table:
    /// hook 5 and alt hook 5 can both exist.
    /// </summary>
    public HookTable AltHooks { get; }

    public ZoneTable Zones { get; }

    /// <summary>
    /// Positional field SFX from MDP sec[29] (river, frogs, town beds).
    /// Same lazy fopen hydrate as <see cref="Zones"/>. Live mixer poke is
    /// <see cref="Game.FieldSfx"/> after field setup.
    /// </summary>
    public MapSfxTable Sfx { get; }

    /// <summary>
    /// Town talkers from MDP sec[8] (kind 0 stands, kind 4 walks, talk ≠ 0). Same lazy
    /// fopen hydrate as <see cref="Zones"/>. Kind-2 wanderers stay on
    /// <see cref="Encounters"/>. Mutate / <see cref="AddNpc"/> /
    /// <see cref="MapNpc.Remove"/> recopies the 4 KiB instance heap after
    /// the field-setup word-copy. Add clones an existing talker's CLUT /
    /// flags — it does not graft a new hdr+4 body.
    /// </summary>
    public MapNpcTable Npcs { get; }

    /// <summary>
    /// This map's sec[21] clip directory (id + streams / frames / cues).
    /// Same lazy fopen hydrate as <see cref="Zones"/>. Play with
    /// <c>anim {id} talk={TalkId} mode=2</c>. Ids missing here are the shared
    /// bank, not NPC data. Dirty rows are emitted and swapped onto
    /// <c>[0x71CAE0]</c> after bind.
    /// </summary>
    public MapAnimTable Anims { get; }

    /// <summary>
    /// This map's sec[15] camera-path directory (1-based id + bytecode).
    /// Same lazy fopen hydrate as <see cref="Zones"/>. Play with
    /// <c>camera_path {id}</c> (BA38 hook 27 is id 1). The engine
    /// <c>dec</c>s the id and reads a self-relative u32 into IP
    /// <c>[0x719934]</c>. Dirty rows are emitted and swapped onto
    /// <c>[0x71A644]</c> after bind. Removed ids stay as <c>0xFF</c>
    /// stubs so <c>camera_path N</c> numbers do not shift.
    /// Edit with <see cref="MapCameraPath.Replace"/> / <see cref="MapCameraPath.ToAsm"/>.
    /// </summary>
    public MapCameraPathTable CameraPaths { get; }

    /// <summary>
    /// This map's sec[10] camera params (mode, pitch reset, Select pan
    /// AABB, Select height via <see cref="MapSelectPan.Distance"/>,
    /// minimap clip at +0xE4/+0xE8, follow-cam / proj words).
    /// Same lazy fopen hydrate as
    /// <see cref="Zones"/>. Dirty fields write the live field-params
    /// heap at <c>[0x63FA9C]</c> after the field-setup word-copy.
    /// Select pan also pokes <c>713F44/3E/40/42</c>; Distance hooks the
    /// Select-enter p28 write (<c>+0x7D028</c> / script 0 +0x20).
    /// </summary>
    public MapCamera Camera { get; }

    /// <summary>
    /// This map's sec[23] sprite-clip directory (id + timed pose frames).
    /// Same lazy fopen hydrate as <see cref="Zones"/>. Play with
    /// <c>unit_bind {id} {talkId}</c> (field_talk; x is the clip id, not a
    /// bone). Not <see cref="Anims"/> (sec[21] xyz polylines). Read-only —
    /// does not mark the map dirty. Maps with no sec[23] (or wrong magic)
    /// hydrate empty.
    /// </summary>
    public MapSpriteClipTable SpriteClips { get; }

    /// <summary>
    /// This map's sec[23] pose directory. Indexer is the pose index a
    /// <see cref="MapSpriteClipFrame.Pose"/> names. Each pose is a list of
    /// 16-byte parts (channel + sprite cookie). Read-only.
    /// </summary>
    public MapSpritePoseTable Poses { get; }

    /// <summary>
    /// SoftHD spriteinfo catalogs plus sec[32] UV cells
    /// (<see cref="MapSpriteBank.Anim"/> / <see cref="MapSpriteBank.Tenants"/> /
    /// <see cref="MapSpriteBank.Maps"/> / <see cref="MapSpriteBank.MapEff"/>).
    /// This is the HD atlas table, not the PS1 TIM. Original texels are
    /// <see cref="Textures"/>. Same lazy fopen hydrate as <see cref="Zones"/>.
    /// Read-only.
    /// </summary>
    public MapSpriteBank Sprites { get; }

    /// <summary>
    /// Original PS1 field TIM (sec[1] + sec[27], rare sec[16]) decoded
    /// into a 1024×512 word sheet. Crop a pose part or sec[32] UV with
    /// <see cref="MapTextureTable.TryCrop(MapSpritePart, out MapTextureCrop)"/>.
    /// Same lazy fopen hydrate as <see cref="Zones"/>. Read-only.
    /// </summary>
    public MapTextureTable Textures { get; }

    /// <summary>
    /// HD sprite → original TIM crop. v4 uses <see cref="MapSprite.Source"/>;
    /// anim hashes <see cref="MapSpriteBank.Uv"/> against <see cref="MapSprite.Key"/>.
    /// </summary>
    public bool TryCrop(MapSprite sprite, out MapTextureCrop crop) =>
        Textures.TryCrop(sprite, Sprites.Uv, out crop);

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
        _scripts.Values.Any(s => s.Dirty) || Hooks.Dirty || AltHooks.Dirty || Zones.Dirty ||
        Sfx.Dirty || Npcs.Dirty || Anims.Dirty || CameraPaths.Dirty || Camera.Dirty;

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

    /// <summary>First unused table-3 alt-hook id on this map (1–255).</summary>
    public int NextAltHookId() => AltHooks.NextId();

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
    /// Append a table-3 row (<c>call_hook {id} alt</c>). Omit
    /// <paramref name="id"/> to take the first unused alt id. Same assembler
    /// grammar as <see cref="AddHook"/>.
    /// </summary>
    public Hook AddAltHook(string line, int? id = null)
    {
        var hid = id ?? AltHooks.NextId();
        var hook = AltHooks.Add(hid);
        hook.Line = FieldHookAsm.NormalizeHookLine(line, hid);
        return hook;
    }

    public Hook AddAltHook(int id, string line) => AddAltHook(line, id);

    /// <summary>Replace an existing table-3 row by id.</summary>
    public Hook ReplaceAltHook(int id, string line)
    {
        var hook = AltHooks.Replace(id);
        hook.Line = FieldHookAsm.NormalizeHookLine(line, id);
        return hook;
    }

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

    /// <summary>
    /// Append a sec[29] emitter. Heap copy holds at most
    /// <see cref="MdpSec29.MaxLive"/> live rows.
    /// </summary>
    public MapSfx AddSfx(int sfx, int x, int y, int z, bool looping = true) =>
        Sfx.Add(sfx, x, y, z, looping);

    public MapSfx AddSfx(int sfx, WalkPos pos, bool looping = true) =>
        AddSfx(sfx, pos.X, pos.Y, pos.Z, looping);

    public void RemoveSfx(MapSfx row) => row.Remove();

    public void RemoveSfx(int index) => Sfx.RemoveAt(index);

    /// <summary>
    /// Append a town talker. Reuse a talk id that already has a body mesh;
    /// a new id without hdr+4 <c>100+(talk−1)</c> will not draw a townsfolk.
    /// Heap copy holds at most <see cref="MdpSec8.MaxLive"/> live rows.
    /// </summary>
    public MapNpc AddNpc(int talk, int x, int y, int z) =>
        Npcs.Add(talk, x, y, z);

    public MapNpc AddNpc(int talk, WalkPos pos) =>
        AddNpc(talk, pos.X, pos.Y, pos.Z);

    public void RemoveNpc(MapNpc row) => row.Remove();

    public void RemoveNpc(int index) => Npcs.RemoveAt(index);

    /// <summary>
    /// Append a sec[21] clip. Play with
    /// <c>anim {id} talk={TalkId} mode=2</c>. Shared-bank ids stay omitted
    /// until added here.
    /// </summary>
    public MapAnim AddAnim(int id) => Anims.Add(id);

    public void RemoveAnim(MapAnim row) => row.Remove();

    public void RemoveAnim(int index) => Anims.RemoveAt(index);

    /// <summary>
    /// Append a sec[15] camera path (or reuse a removed id). Play with
    /// <c>camera_path {id}</c>.
    /// </summary>
    public MapCameraPath AddCameraPath(byte[]? raw = null) => CameraPaths.Add(raw);

    public MapCameraPath AddCameraPath(IEnumerable<MapCameraPathOp> ops) =>
        CameraPaths.Add(ops);

    public MapCameraPath AddCameraPath(string disassembly) =>
        CameraPaths.Add(disassembly);

    public void RemoveCameraPath(MapCameraPath row) => row.Remove();

    public void RemoveCameraPath(int index) => CameraPaths.RemoveAt(index);

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

        if (AltHooks.Dirty)
        {
            sb.AppendLine("table 3 {");
            foreach (var hook in AltHooks.Items.Where(h => h.Dirty && !string.IsNullOrWhiteSpace(h.Line)))
            {
                sb.AppendLine(hook.Append ? "add" : $"replace id={hook.Id}");
                sb.AppendLine(hook.Line);
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (Sfx.Dirty)
        {
            sb.AppendLine("sfx {");
            sb.Append("  range ").Append(Sfx.Range).AppendLine();
            foreach (var row in Sfx.Items.Where(e => e.Dirty && !e.Removed))
            {
                sb.Append(row.Append ? "  add" : "  replace");
                sb.Append(" id=").Append(row.Id);
                sb.Append(" sfx=").Append(row.Sfx);
                sb.Append(" flags=0x").Append(row.Flags.ToString("X"));
                sb.Append(" x=").Append(row.X);
                sb.Append(" y=").Append(row.Y);
                sb.Append(" z=").Append(row.Z);
                sb.AppendLine();
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (Npcs.Dirty)
        {
            sb.AppendLine("npcs {");
            foreach (var row in Npcs.Items.Where(e => e.Dirty && !e.Removed))
            {
                sb.Append(row.Append ? "  add" : "  replace");
                sb.Append(" talk=").Append(row.TalkId);
                sb.Append(" kind=").Append(row.Kind);
                sb.Append(" x=").Append(row.X);
                sb.Append(" y=").Append(row.Y);
                sb.Append(" z=").Append(row.Z);
                sb.AppendLine();
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (Camera.Dirty && Camera.Present)
        {
            sb.AppendLine("camera {");
            sb.Append("  mode ").Append((byte)Camera.Mode).AppendLine();
            sb.Append("  pitch ").Append(Camera.Pitch).AppendLine();
            sb.Append("  follow ").Append(Camera.Follow).AppendLine();
            sb.Append("  follow_term 0x").Append(Camera.FollowTerm.ToString("X")).AppendLine();
            sb.Append("  proj 0x").Append(Camera.ProjA.ToString("X"));
            sb.Append(" 0x").Append(Camera.ProjB.ToString("X"));
            sb.Append(" 0x").Append(Camera.ProjC.ToString("X")).AppendLine();
            sb.Append("  view ").Append(Camera.ViewX).Append(' ')
                .Append(Camera.ViewY).Append(' ').Append(Camera.ViewZ).AppendLine();
            sb.Append("  select_pan ").Append(Camera.SelectPan.Enabled ? "on" : "off");
            sb.Append(" xmin=").Append(Camera.SelectPan.XMin);
            sb.Append(" zmin=").Append(Camera.SelectPan.ZMin);
            sb.Append(" xmax=").Append(Camera.SelectPan.XMax);
            sb.Append(" zmax=").Append(Camera.SelectPan.ZMax);
            sb.Append(" distance=").Append(Camera.SelectPan.Distance);
            sb.Append(" clip=").Append(Camera.SelectPan.ClipLo);
            sb.Append(',').Append(Camera.SelectPan.ClipHi);
            sb.Append(" scale=").Append(Camera.SelectPan.Scale).AppendLine();
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
