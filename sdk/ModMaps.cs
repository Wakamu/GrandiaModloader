using System.Reflection;

namespace Grandia.Sdk;

/// <summary>
/// One field stem shipped inside a mod (MDP + SoftHD SCN/OFS). Last
/// <see cref="ModMapSet.Add"/> for a stem wins. Bytes stay in the DLL;
/// the host serves them at fopen — no copy into the game folder.
/// </summary>
public sealed class ModMap
{
    public ModMap(string stem, byte[]? mdp = null, byte[]? scn = null, byte[]? ofs = null)
    {
        Stem = (stem ?? "").Trim().ToUpperInvariant();
        Mdp = mdp is { Length: > 0 } ? mdp : null;
        Scn = scn is { Length: > 0 } ? scn : null;
        Ofs = ofs is { Length: > 0 } ? ofs : null;
    }

    public string Stem { get; }

    public byte[]? Mdp { get; }

    public byte[]? Scn { get; }

    public byte[]? Ofs { get; }

    public bool HasAny => Mdp != null || Scn != null || Ofs != null;
}

/// <summary>
/// Maps registered from <see cref="InitAttribute"/>. Shared across plugins
/// in launcher order (later <see cref="Add"/> replaces the same stem).
/// </summary>
public sealed class ModMapSet
{
    private readonly Dictionary<string, ModMap> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MapPins> _pins = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool Has(string stem)
    {
        lock (_gate)
        {
            return _maps.ContainsKey(Normalize(stem));
        }
    }

    public bool TryGet(string stem, out ModMap map)
    {
        lock (_gate)
        {
            return _maps.TryGetValue(Normalize(stem), out map!);
        }
    }

    public IReadOnlyList<ModMap> All
    {
        get
        {
            lock (_gate)
            {
                return _maps.Values.ToArray();
            }
        }
    }

    public void Add(string stem, byte[]? mdp = null, byte[]? scn = null, byte[]? ofs = null)
    {
        Add(new ModMap(stem, mdp, scn, ofs));
    }

    public void Add(ModMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (string.IsNullOrWhiteSpace(map.Stem) || !map.HasAny)
        {
            return;
        }

        lock (_gate)
        {
            _maps[map.Stem] = map;
            if (_pins.Remove(map.Stem, out var old))
            {
                old.Free();
            }
        }
    }

    /// <summary>
    /// Load <c>maps/CC15.mdp</c> (and .scn / .ofs) compiled in as
    /// <c>EmbeddedResource</c>. Matches any resource whose name ends with
    /// those file names.
    /// </summary>
    public void AddFromEmbedded(Assembly assembly, string stem, string folder = "maps")
    {
        ArgumentNullException.ThrowIfNull(assembly);
        stem = Normalize(stem);
        folder = (folder ?? "maps").Replace('\\', '/').Trim('/');
        Add(stem,
            EmbeddedResource.Read(assembly, $"{folder}/{stem}.mdp"),
            EmbeddedResource.Read(assembly, $"{folder}/{stem}.scn"),
            EmbeddedResource.Read(assembly, $"{folder}/{stem}.ofs"));
    }

    internal bool TryPin(string stem, int kind, out nint ptr, out int len)
    {
        ptr = 0;
        len = 0;
        lock (_gate)
        {
            if (!_maps.TryGetValue(Normalize(stem), out var map))
            {
                return false;
            }

            if (!_pins.TryGetValue(map.Stem, out var pins))
            {
                pins = new MapPins(map);
                _pins[map.Stem] = pins;
            }

            return pins.TryGet(kind, out ptr, out len);
        }
    }

    private static string Normalize(string stem) => (stem ?? "").Trim().ToUpperInvariant();

    private sealed class MapPins
    {
        private System.Runtime.InteropServices.GCHandle _mdp;
        private System.Runtime.InteropServices.GCHandle _scn;
        private System.Runtime.InteropServices.GCHandle _ofs;

        public MapPins(ModMap map)
        {
            if (map.Mdp != null)
            {
                _mdp = System.Runtime.InteropServices.GCHandle.Alloc(map.Mdp,
                    System.Runtime.InteropServices.GCHandleType.Pinned);
            }

            if (map.Scn != null)
            {
                _scn = System.Runtime.InteropServices.GCHandle.Alloc(map.Scn,
                    System.Runtime.InteropServices.GCHandleType.Pinned);
            }

            if (map.Ofs != null)
            {
                _ofs = System.Runtime.InteropServices.GCHandle.Alloc(map.Ofs,
                    System.Runtime.InteropServices.GCHandleType.Pinned);
            }
        }

        public bool TryGet(int kind, out nint ptr, out int len)
        {
            var handle = kind switch
            {
                0 => _mdp,
                1 => _scn,
                2 => _ofs,
                _ => default,
            };
            if (!handle.IsAllocated || handle.Target is not byte[] bytes || bytes.Length == 0)
            {
                ptr = 0;
                len = 0;
                return false;
            }

            ptr = handle.AddrOfPinnedObject();
            len = bytes.Length;
            return true;
        }

        public void Free()
        {
            if (_mdp.IsAllocated)
            {
                _mdp.Free();
            }

            if (_scn.IsAllocated)
            {
                _scn.Free();
            }

            if (_ofs.IsAllocated)
            {
                _ofs.Free();
            }
        }
    }
}

/// <summary>Resolve a logical path like <c>maps/CC15.mdp</c> in an assembly's embedded resources.</summary>
public static class EmbeddedResource
{
    public static byte[]? Read(Assembly assembly, string logicalPath)
    {
        using var stream = Open(assembly, logicalPath);
        if (stream is null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static Stream? Open(Assembly assembly, string logicalPath)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var want = (logicalPath ?? "").Replace('\\', '/').Trim().TrimStart('/');
        if (want.Length == 0)
        {
            return null;
        }

        var dotted = want.Replace('/', '.');
        var file = Path.GetFileName(want);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var norm = name.Replace('\\', '/');
            var resFile = Path.GetFileName(norm);
            if (name.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                norm.Equals(want, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(dotted, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("." + dotted, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("." + file, StringComparison.OrdinalIgnoreCase) ||
                norm.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resFile, file, StringComparison.OrdinalIgnoreCase))
            {
                return assembly.GetManifestResourceStream(name);
            }
        }

        return null;
    }

    /// <summary>
    /// Copy an embedded file to a temp path so native code (world-map PNG
    /// overlay, …) can open it. Same bytes are not rewritten if the dest
    /// already matches.
    /// </summary>
    public static string? Materialize(Assembly assembly, string logicalPath)
    {
        var bytes = Read(assembly, logicalPath);
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var folder = Path.Combine(Path.GetTempPath(), "GrandiaMods", "assets");
        Directory.CreateDirectory(folder);
        var asmName = assembly.GetName().Name ?? "mod";
        var dest = Path.Combine(folder, asmName + "_" + Path.GetFileName(logicalPath.Replace('/', '_')));
        if (File.Exists(dest))
        {
            var existing = File.ReadAllBytes(dest);
            if (existing.AsSpan().SequenceEqual(bytes))
            {
                return dest;
            }
        }

        File.WriteAllBytes(dest, bytes);
        return dest;
    }
}
