namespace GrandiaModloader;

public sealed class ModInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public required string AssemblyPath { get; init; }
    public bool Enabled { get; set; } = true;
}

public sealed class ModStore
{
    private readonly string _configPath;
    private AppConfig _config;

    public ModStore(string configPath, AppConfig config)
    {
        _configPath = configPath;
        _config = config;
        Directory.CreateDirectory(Paths.ModsDir);
    }

    public AppConfig Config => _config;

    public void ReloadConfig()
    {
        _config = AppConfig.Load(_configPath);
    }

    public void SaveConfig()
    {
        _config.Save(_configPath);
    }

    public List<ModInfo> ListMods()
    {
        Directory.CreateDirectory(Paths.ModsDir);
        FlattenLegacyFolders();

        var files = Directory.GetFiles(Paths.ModsDir, "*.dll")
            .Where(p => !IsHostDll(Path.GetFileNameWithoutExtension(p)))
            .Select(p => new FileInfo(p))
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f.Name), f => f,
                StringComparer.OrdinalIgnoreCase);

        var ordered = new List<ModInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in _config.ModOrder)
        {
            if (!files.TryGetValue(id, out var file))
            {
                continue;
            }

            ordered.Add(ReadMod(file));
            seen.Add(id);
        }

        foreach (var file in files.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (seen.Contains(id))
            {
                continue;
            }

            ordered.Add(ReadMod(file));
        }

        PersistOrder(ordered);
        return ordered;
    }

    public ModInfo AddFromPath(string path)
    {
        Directory.CreateDirectory(Paths.ModsDir);
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            var dll = FindBuiltAssembly(path) ??
                      Directory.GetFiles(path, "*.dll")
                          .FirstOrDefault(p => !IsHostDll(Path.GetFileNameWithoutExtension(p)));
            if (dll is null)
            {
                throw new InvalidOperationException("That folder has no compiled mod DLL.");
            }

            path = dll;
        }

        if (!File.Exists(path) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Pick a compiled mod DLL.");
        }

        return AddFromDll(path);
    }

    public void Remove(string id)
    {
        var dll = Path.Combine(Paths.ModsDir, id + ".dll");
        if (File.Exists(dll))
        {
            File.Delete(dll);
        }

        var pdb = Path.Combine(Paths.ModsDir, id + ".pdb");
        if (File.Exists(pdb))
        {
            File.Delete(pdb);
        }

        _config.ModOrder.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        _config.ModEnabled.Remove(id);
        SaveConfig();
    }

    public void SetEnabled(string id, bool enabled)
    {
        _config.ModEnabled[id] = enabled;
        SaveConfig();
    }

    public void Move(int index, int delta)
    {
        var mods = ListMods();
        var dest = index + delta;
        if (index < 0 || index >= mods.Count || dest < 0 || dest >= mods.Count)
        {
            return;
        }

        (mods[index], mods[dest]) = (mods[dest], mods[index]);
        PersistOrder(mods);
    }

    private ModInfo ReadMod(FileInfo file)
    {
        var id = Path.GetFileNameWithoutExtension(file.Name);
        var meta = ModMetadataReader.Read(file.FullName);
        var enabled = true;
        if (_config.ModEnabled.TryGetValue(id, out var stored))
        {
            enabled = stored;
        }

        return new ModInfo
        {
            Id = id,
            Name = meta.Name,
            Version = meta.Version,
            Description = meta.Description,
            AssemblyPath = file.FullName,
            Enabled = enabled,
        };
    }

    private void AppendOrder(string id, bool enabled)
    {
        if (!_config.ModOrder.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
        {
            _config.ModOrder.Add(id);
        }

        _config.ModEnabled[id] = enabled;
        SaveConfig();
    }

    private void PersistOrder(List<ModInfo> mods)
    {
        _config.ModOrder = mods.Select(m => m.Id).ToList();
        foreach (var m in mods)
        {
            _config.ModEnabled[m.Id] = m.Enabled;
        }

        SaveConfig();
    }

    private ModInfo AddFromDll(string dllPath)
    {
        var name = Path.GetFileNameWithoutExtension(dllPath);
        if (IsHostDll(name))
        {
            throw new InvalidOperationException("That DLL is part of the loader, not a mod.");
        }

        var dest = Path.Combine(Paths.ModsDir, Path.GetFileName(dllPath));
        var src = Path.GetFullPath(dllPath);
        if (!string.Equals(src, Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(src, dest, overwrite: true);
        }

        var pdb = Path.ChangeExtension(src, ".pdb");
        if (File.Exists(pdb))
        {
            File.Copy(pdb, Path.ChangeExtension(dest, ".pdb"), overwrite: true);
        }

        var mod = ReadMod(new FileInfo(dest));
        AppendOrder(mod.Id, enabled: true);
        return mod;
    }

    /// <summary>
    /// Old layout was <c>mods/Name/mod.json + Name.dll</c>. Copy the DLL up
    /// so a single file in <c>mods/</c> is enough.
    /// </summary>
    private static void FlattenLegacyFolders()
    {
        foreach (var dir in Directory.GetDirectories(Paths.ModsDir))
        {
            if (new DirectoryInfo(dir).Name.StartsWith('.'))
            {
                continue;
            }

            string? dll = null;
            try
            {
                dll = FindBuiltAssembly(dir) ??
                      Directory.GetFiles(dir, "*.dll")
                          .FirstOrDefault(p => !IsHostDll(Path.GetFileNameWithoutExtension(p)));
            }
            catch
            {
                continue;
            }

            if (dll is null)
            {
                continue;
            }

            var dest = Path.Combine(Paths.ModsDir, Path.GetFileName(dll));
            try
            {
                if (!File.Exists(dest) || File.GetLastWriteTimeUtc(dll) > File.GetLastWriteTimeUtc(dest))
                {
                    File.Copy(dll, dest, overwrite: true);
                }
            }
            catch
            {
                // Leave the folder in place if the copy is locked.
            }
        }
    }

    private static bool IsHostDll(string nameWithoutExt) =>
        nameWithoutExt.Equals("Grandia.Sdk", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.Equals("Grandia.Runtime", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.Equals("GrandiaMod", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.Equals("GrandiaModloader", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.Equals("Archipelago.MultiClient.Net", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.Equals("System.IO.Hashing", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.Equals("Websocket.Client", StringComparison.OrdinalIgnoreCase) ||
        nameWithoutExt.StartsWith("System.Reactive", StringComparison.OrdinalIgnoreCase);

    public static string? FindBuiltAssembly(string folder)
    {
        string[] hints =
        [
            folder,
            Path.Combine(folder, "bin", "Release", "net8.0"),
            Path.Combine(folder, "bin", "Debug", "net8.0"),
        ];
        foreach (var dir in hints)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var dll = Directory.GetFiles(dir, "*.dll")
                .FirstOrDefault(p => !IsHostDll(Path.GetFileNameWithoutExtension(p)));
            if (dll is not null)
            {
                return Path.GetFullPath(dll);
            }
        }

        return null;
    }
}
