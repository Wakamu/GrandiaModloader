using System.Reflection;

namespace Grandia.Sdk;

/// <summary>
/// Named field-script bytecode shipped inside a mod. Assemble at
/// <b>mod build</b> (<c>scripts/hub_save.asm</c> → <c>hub_save.bin</c>).
/// Does not replace a vanilla script by itself — call
/// <see cref="ScriptExecuteEvent.Use(string)"/> from
/// <see cref="OnScriptExecuteAttribute"/> for whichever ids you want.
/// Last <see cref="ModScriptSet.Add"/> for the same name wins.
/// </summary>
public sealed class ModScript
{
    public ModScript(string name, byte[] bytecode)
    {
        Name = (name ?? "").Trim();
        Bytecode = bytecode is { Length: > 0 } ? bytecode : [];
    }

    public string Name { get; }

    public byte[] Bytecode { get; }

    public bool HasBytes => Bytecode.Length > 0;
}

/// <summary>
/// Compiled scripts registered from <see cref="InitAttribute"/>. Shared
/// across plugins in launcher order.
/// </summary>
public sealed class ModScriptSet
{
    private readonly Dictionary<string, ModScript> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool HasAny
    {
        get
        {
            lock (_gate)
            {
                return _scripts.Count > 0;
            }
        }
    }

    public bool TryGet(string name, out byte[] bytecode)
    {
        lock (_gate)
        {
            if (_scripts.TryGetValue(Normalize(name), out var row) && row.HasBytes)
            {
                bytecode = row.Bytecode;
                return true;
            }
        }

        bytecode = [];
        return false;
    }

    public byte[]? Get(string name) => TryGet(name, out var bytes) ? bytes : null;

    public void Add(string name, byte[]? bytecode)
    {
        if (bytecode is not { Length: > 0 })
        {
            return;
        }

        Add(new ModScript(name, bytecode));
    }

    public void Add(ModScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var name = Normalize(script.Name);
        if (name.Length == 0 || !script.HasBytes)
        {
            return;
        }

        lock (_gate)
        {
            _scripts[name] = script.Name == name ? script : new ModScript(name, script.Bytecode);
        }
    }

    /// <summary>
    /// Load embedded <c>scripts/*.bin</c>. The file name (without
    /// extension) is the catalog name used by
    /// <see cref="ScriptExecuteEvent.Use(string)"/>. Omit
    /// <paramref name="name"/> to load every script in
    /// <paramref name="folder"/>.
    /// </summary>
    public void AddFromEmbedded(Assembly assembly, string? name = null, string folder = "scripts")
    {
        ArgumentNullException.ThrowIfNull(assembly);
        folder = (folder ?? "scripts").Replace('\\', '/').Trim('/');
        var want = string.IsNullOrWhiteSpace(name) ? null : Normalize(name);
        if (want != null)
        {
            Add(want, EmbeddedResource.Read(assembly, $"{folder}/{want}.bin"));
            return;
        }

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (folder.Length > 0 &&
                resource.IndexOf(folder, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (!TryNameFromResource(resource, out var parsed))
            {
                continue;
            }

            Add(parsed, EmbeddedResource.Read(assembly, resource));
        }
    }

    public static bool TryNameFromResource(string resource, out string name)
    {
        name = "";
        var path = (resource ?? "").Replace('\\', '/');
        if (!path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var noExt = path[..^4];
        var slash = noExt.LastIndexOf('/');
        var leaf = slash >= 0 ? noExt[(slash + 1)..] : noExt;
        var dot = leaf.LastIndexOf('.');
        name = Normalize(dot >= 0 ? leaf[(dot + 1)..] : leaf);
        return name.Length > 0;
    }

    private static string Normalize(string name) => (name ?? "").Trim();
}
