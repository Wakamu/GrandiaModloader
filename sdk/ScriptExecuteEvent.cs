namespace Grandia.Sdk;

/// <summary>
/// Field VM is resolving a script id to an IP (<c>+0x6F0B0</c>).
/// Use <see cref="Use(string)"/> for a compiled catalog script,
/// <see cref="Replace"/> / <see cref="Script"/> to assemble live text,
/// or set <see cref="Bytecode"/>. <see cref="Skip"/> treats the id as missing.
/// </summary>
public sealed class ScriptExecuteEvent
{
    private readonly ModScriptSet? _scripts;

    public ScriptExecuteEvent(MapId map, int scriptId, ModScriptSet? scripts = null)
    {
        Map = map;
        ScriptId = scriptId;
        Script = new Script(scriptId);
        _scripts = scripts;
    }

    public MapId Map { get; }

    public int ScriptId { get; }

    /// <summary>
    /// Replacement script in the field assembler language (same ops as
    /// <see cref="Map.ReplaceScript"/>). Mutate in place, or call
    /// <see cref="Replace"/>. If <see cref="Script.Dirty"/> after mods run,
    /// this is assembled and used instead of vanilla / <see cref="Bytecode"/>.
    /// </summary>
    public Script Script { get; }

    /// <summary>Replacement u16-LE word stream. Null keeps the OnMapLoad cache or vanilla.</summary>
    public byte[]? Bytecode { get; set; }

    /// <summary>Catalog name last passed to <see cref="Use(string)"/>, if any.</summary>
    public string? EmbeddedName { get; private set; }

    /// <summary>Do not arm this script (VM sees id-not-found).</summary>
    public bool Skip { get; set; }

    /// <summary>Stop the vanilla script and run this assembler text instead.</summary>
    public void Replace(string disassembly) => Script.Replace(disassembly);

    /// <summary>
    /// Run a compiled script registered on <see cref="ModContext.Scripts"/>
    /// (same blob can be used for any map / script id).
    /// </summary>
    public bool Use(string name)
    {
        if (_scripts is null || !_scripts.TryGet(name, out var bytes))
        {
            return false;
        }

        Bytecode = bytes;
        EmbeddedName = name;
        return true;
    }

    /// <summary>Run this compiled bytecode instead of vanilla / assembler text.</summary>
    public void Use(byte[] bytecode)
    {
        Bytecode = bytecode;
        EmbeddedName = null;
    }
}
