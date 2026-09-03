using System.Reflection;
using System.Runtime.Loader;
using Grandia.Sdk;

namespace Grandia.Runtime;

public static class ModHost
{
    private static readonly object Gate = new();
    private static ModsConfig? Config;
    private static ModHooks Hooks = new();
    private static ModMapSet Maps = new();
    private static ModScriptSet Scripts = new();
    private static int PluginCount;
    private static readonly List<string> SearchDirs = [];
    private static readonly List<Assembly> LoadedAssemblies = [];
    private static readonly HashSet<string> Assembled = new(StringComparer.OrdinalIgnoreCase);
    private static bool Loaded;
    private static bool ResolverHooked;

    public static IReadOnlyList<string> SearchDirectories
    {
        get
        {
            lock (Gate)
            {
                return SearchDirs.ToArray();
            }
        }
    }

    public static IReadOnlyList<Assembly> LoadedModAssemblies
    {
        get
        {
            lock (Gate)
            {
                return LoadedAssemblies.ToArray();
            }
        }
    }

    /// <summary>
    /// Resolve a file next to a mod DLL, or an embedded resource (PNG, …)
    /// extracted to a temp path for native code.
    /// </summary>
    public static string? ResolveAsset(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = name.Trim();
        if (File.Exists(name))
        {
            return Path.GetFullPath(name);
        }

        foreach (var dir in SearchDirectories)
        {
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            var combined = Path.Combine(dir, name);
            if (File.Exists(combined))
            {
                return Path.GetFullPath(combined);
            }
        }

        lock (Gate)
        {
            foreach (var asm in LoadedAssemblies)
            {
                var extracted = EmbeddedResource.Materialize(asm, name);
                if (extracted != null)
                {
                    return extracted;
                }
            }
        }

        return null;
    }

    public static void Initialize(string modsJsonPath, Action<string>? log = null)
    {
        lock (Gate)
        {
            if (Loaded)
            {
                return;
            }

            Config = ModsConfig.Load(modsJsonPath);
            Hooks = new ModHooks(log);
            Maps = new ModMapSet();
            Scripts = new ModScriptSet();
            PluginCount = 0;
            LoadedAssemblies.Clear();
            SearchDirs.Clear();
            foreach (var entry in Config.Mods)
            {
                if (string.IsNullOrWhiteSpace(entry.Assembly) || !File.Exists(entry.Assembly))
                {
                    log?.Invoke($"skip mod {entry.Id}: assembly not found");
                    continue;
                }

                try
                {
                    LoadMod(entry.Id, entry.Assembly, log);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"mod {entry.Id} failed to load: {ex.Message}");
                }
            }

            Loaded = true;
            log?.Invoke($"runtime ready ({PluginCount} plugin(s))");
        }
    }

    public static int OnMapOpen(string stem, ushort from, ushort to, int spawn, string cacheDir,
        Action<string>? log = null)
    {
        lock (Gate)
        {
            if (!Loaded)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(stem))
            {
                return 0;
            }

            stem = stem.Trim().ToUpperInvariant();
            var embedded = Maps.TryGet(stem, out var packed) && packed.HasAny;
            if (!Hooks.HasAny && !embedded)
            {
                return 0;
            }

            var toId = to != 0 ? new MapId(to) : MapId.Parse(stem);
            if (toId.Value == 0)
            {
                toId = MapId.Parse(stem);
            }

            var fromId = new MapId(from);
            // Assemble once per stem. from/to/spawn are often 0 on dest-arrival
            // fopen; bind-time apply uses this cache on every map enter.
            if (Assembled.Contains(stem))
            {
                return MapRamStore.IsDirty(stem) ? 1 : 0;
            }

            var map = new Map(stem);
            if (embedded && packed!.Mdp != null)
            {
                map.Hooks.SeedOccupied(MdpHookIds.ReadTable2(packed.Mdp));
            }
            else if (Config != null)
            {
                map.Hooks.SeedOccupied(LoadOccupiedHookIds(stem, Config.Field));
            }

            var cache = string.IsNullOrWhiteSpace(cacheDir)
                ? Config?.Cache ?? ""
                : cacheDir;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var hydrateMs = 0L;
            map.EnsureScripts = () =>
            {
                if (Config is null)
                {
                    return;
                }

                sw.Restart();
                ScriptHydrator.Hydrate(map, Config, cache, log);
                hydrateMs = sw.ElapsedMilliseconds;
            };
            var ev = new MapLoadEvent(fromId, toId, spawn, map);
            Hooks.Invoke("OnMapLoad", ev);

            if (!map.Dirty)
            {
                Assembled.Add(stem);
                if (embedded)
                {
                    MapRamStore.LoadEmbedded(stem, packed!.Mdp, packed.Scn, packed.Ofs);
                    log?.Invoke($"OnMapOpen {stem} embedded mdp={packed.Mdp?.Length ?? 0} scn={packed.Scn?.Length ?? 0} ofs={packed.Ofs?.Length ?? 0}");
                    return 1;
                }

                MapRamStore.MarkClean(stem);
                log?.Invoke($"OnMapOpen {stem} hydrate={hydrateMs}ms dirty=0");
                return 0;
            }

            var outDir = cache;
            if (string.IsNullOrWhiteSpace(outDir) || Config is null)
            {
                throw new InvalidOperationException("cache dir is empty");
            }

            sw.Restart();
            PatchEmitter.Emit(map, Config, outDir, log);
            var dirtyScripts = map.Scripts.Where(s => s.Dirty).Select(s => s.Id).ToArray();
            var dirtyHooks = map.Hooks.Items.Where(h => h.Dirty).Select(h => h.Id).ToArray();
            var ram = MapRamStore.LoadEmitted(stem, outDir, Config, dirtyScripts, dirtyHooks);
            log?.Invoke(
                $"OnMapOpen {stem} hydrate={hydrateMs}ms emit={sw.ElapsedMilliseconds}ms ram sec7={ram.Sec7.Length} scn={ram.Scn.Length} ofs={ram.Ofs.Length} scripts={ram.Scripts.Count} hooks={ram.Hooks.Count}");
            Assembled.Add(stem);
            return MapRamStore.IsDirty(stem) ? 1 : 0;
        }
    }

    public static void OnEventFlag(EventFlagEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnEventFlag", ev);
    }

    public static void OnItemAssignUi(ItemAssignUiEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnItemAssignUi", ev);
    }

    public static void OnFieldGoldAdd(FieldGoldAddEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnFieldGoldAdd", ev);
    }

    public static void OnWorldMapLoad(WorldMapLoadEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnWorldMapLoad", ev);
    }

    public static void OnWorldMapConfirm(WorldMapConfirmEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnWorldMapConfirm", ev);
    }

    public static void OnMapTravel(MapTravelEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnMapTravel", ev);
    }

    public static void OnSave(SaveEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnSave", ev);
    }

    public static void OnLoad(LoadEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnLoad", ev);
    }

    public static void OnBattleSetup(BattleSetupEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnBattleSetup", ev);
    }

    public static void OnBattleLoad(BattleLoadEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnBattleLoad", ev);
    }

    public static void OnMenuOpen(MenuOpenEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnMenuOpen", ev);
    }

    public static void OnEnemyLoaded(EnemyLoadedEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnEnemyLoaded", ev);
    }

    public static void OnShopOpen(ShopOpenEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnShopOpen", ev);
    }

    public static void OnTick(TickEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnTick", ev);
    }

    public static void OnTitleScreen(TitleScreenEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnTitleScreen", ev);
    }

    public static void OnCharacter(CharacterEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnCharacter", ev);
    }

    public static void OnItem(ItemEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnItem", ev);
    }

    public static void OnMagic(MagicEvent ev, Action<string>? log = null)
    {
        InvokeHooks("OnMagic", ev);
    }

    private static void InvokeHooks<TEvent>(string name, TEvent ev)
    {
        lock (Gate)
        {
            if (!Loaded)
            {
                return;
            }

            Hooks.Invoke(name, ev);
        }
    }

    private static void LoadMod(string id, string assemblyPath, Action<string>? log)
    {
        var alc = AssemblyLoadContext.Default;
        if (!ResolverHooked)
        {
            alc.Resolving += ResolveSdk;
            ResolverHooked = true;
        }

        var src = Path.GetFullPath(assemblyPath);
        var written = File.GetLastWriteTimeUtc(src);
        var loadPath = ShadowCopy(src);
        var srcDir = Path.GetDirectoryName(src);
        var loadDir = Path.GetDirectoryName(loadPath);
        if (!string.IsNullOrEmpty(srcDir) && !SearchDirs.Contains(srcDir))
        {
            SearchDirs.Add(srcDir);
        }

        if (!string.IsNullOrEmpty(loadDir) && !SearchDirs.Contains(loadDir))
        {
            SearchDirs.Add(loadDir);
        }

        log?.Invoke($"mod {id}: {src} utc={written:yyyy-MM-dd HH:mm:ss}");

        var asm = alc.LoadFromAssemblyPath(loadPath);
        LoadedAssemblies.Add(asm);
        ExtractPackedAssets(asm, log);
        var plugins = asm.GetExportedTypes()
            .Where(t => !t.IsAbstract && t.GetCustomAttribute<ModAttribute>() != null)
            .ToArray();
        if (plugins.Length > 0)
        {
            foreach (var type in plugins)
            {
                LoadPlugin(id, type, log);
            }

            return;
        }

        var found = 0;
        foreach (var type in asm.GetExportedTypes())
        {
            if (!typeof(IMod).IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is not IMod mod)
            {
                continue;
            }

            Hooks.RegisterIMod(mod);
            found++;
            PluginCount++;
            log?.Invoke($"loaded IMod {type.FullName}");
        }

        if (found == 0)
        {
            log?.Invoke($"no [Mod] or IMod in {Path.GetFileName(src)}");
        }
    }

    private static void LoadPlugin(string id, Type type, Action<string>? log)
    {
        var instance = Activator.CreateInstance(type);
        if (instance is null)
        {
            log?.Invoke($"skip {type.FullName}: no public constructor");
            return;
        }

        var ctx = new ModContext(id, Hooks, Maps, Scripts, message => log?.Invoke($"{id}: {message}"));
        var inits = 0;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in type.GetMethods(flags))
        {
            if (method.GetCustomAttribute<InitAttribute>() is null)
            {
                continue;
            }

            if (!TryCallInit(instance, method, ctx, log))
            {
                continue;
            }

            inits++;
        }

        Hooks.Register(instance);
        PluginCount++;
        log?.Invoke(inits == 0
            ? $"loaded {type.FullName} (no [Init])"
            : $"loaded {type.FullName}");
    }

    private static bool TryCallInit(object instance, MethodInfo method, ModContext ctx,
        Action<string>? log)
    {
        var args = method.GetParameters();
        object?[]? invokeArgs = args.Length switch
        {
            0 => null,
            1 when args[0].ParameterType == typeof(ModContext) => [ctx],
            _ => null,
        };
        if (args.Length > 0 && invokeArgs is null)
        {
            log?.Invoke($"skip [Init] {instance.GetType().Name}.{method.Name}: use () or (ModContext)");
            return false;
        }

        try
        {
            method.Invoke(method.IsStatic ? null : instance, invokeArgs);
            return true;
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException { InnerException: { } cause } ? cause : ex;
            log?.Invoke($"[Init] {instance.GetType().Name}.{method.Name}: {inner.Message}");
            return false;
        }
    }

    private static string ShadowCopy(string assemblyPath)
    {
        var destDir = Path.Combine(Path.GetTempPath(), "GrandiaMods",
            Path.GetFileNameWithoutExtension(assemblyPath) + "_" + DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(destDir);
        var name = Path.GetFileName(assemblyPath);
        File.Copy(assemblyPath, Path.Combine(destDir, name), overwrite: true);
        var pdb = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdb))
        {
            File.Copy(pdb, Path.Combine(destDir, Path.GetFileName(pdb)), overwrite: true);
        }

        return Path.Combine(destDir, name);
    }

    private static void ExtractPackedAssets(Assembly asm, Action<string>? log)
    {
        foreach (var resource in asm.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                !resource.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !resource.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = EmbeddedResource.Materialize(asm, resource);
            if (path != null)
            {
                log?.Invoke($"asset {resource} -> {path}");
            }
        }
    }

    private static Assembly? ResolveSdk(AssemblyLoadContext ctx, AssemblyName name)
    {
        if (string.Equals(name.Name, "Grandia.Sdk", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(ModAttribute).Assembly;
        }

        return null;
    }

    private static IEnumerable<int> LoadOccupiedHookIds(string stem, string fieldDir)
    {
        foreach (var path in HookMdpCandidates(stem, fieldDir))
        {
            if (File.Exists(path))
            {
                return MdpHookIds.FromFile(path);
            }
        }

        return [];
    }

    private static IEnumerable<string> HookMdpCandidates(string stem, string fieldDir)
    {
        if (string.IsNullOrWhiteSpace(fieldDir))
        {
            yield break;
        }

        foreach (var name in new[] { stem + ".mdp", stem + ".MDP" })
        {
            yield return Path.Combine(fieldDir, name);
            yield return Path.Combine(fieldDir, "FIELD", name);
        }
    }

    internal static bool TryGetMapFile(string stem, int kind, out nint ptr, out int len)
    {
        lock (Gate)
        {
            return Maps.TryPin(stem, kind, out ptr, out len);
        }
    }

    internal static bool TryFillPatchInfo(string stem, out MapRamPatch? patch)
    {
        lock (Gate)
        {
            patch = MapRamStore.Get(stem);
            return patch != null;
        }
    }

    /// <summary>
    /// 1 = use <paramref name="ip"/>, -1 = skip (not found), 0 = vanilla lookup.
    /// </summary>
    public static int OnScriptExecute(string stem, int scriptId, out uint ip, Action<string>? log = null)
    {
        ip = 0;
        lock (Gate)
        {
            if (!Loaded)
            {
                return 0;
            }

            if (!Hooks.HasAny)
            {
                return 0;
            }

            stem = (stem ?? "").Trim().ToUpperInvariant();
            var mapId = MapId.Parse(stem);
            var patch = MapRamStore.Get(stem);
            var ev = new ScriptExecuteEvent(mapId, scriptId, Scripts)
            {
                Bytecode = patch != null && patch.Scripts.TryGetValue(scriptId, out var blob)
                    ? blob.Bytes
                    : null,
            };
            Hooks.Invoke("OnScriptExecute", ev);

            if (ev.Skip)
            {
                return -1;
            }

            byte[]? bytes = ev.Bytecode;
            if (ev.Script.Dirty)
            {
                if (Config is null)
                {
                    log?.Invoke($"OnScriptExecute 0x{scriptId:X4} on {stem}: no config, vanilla");
                    return 0;
                }

                log?.Invoke($"OnScriptExecute replace 0x{scriptId:X4} on {stem} (assembling)");
                try
                {
                    bytes = ScriptAssembler.Assemble(stem, scriptId, ev.Script.ToAsm(), Config, log);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"OnScriptExecute assemble 0x{scriptId:X4}: {ex.Message}");
                    return 0;
                }
            }
            else if (!string.IsNullOrEmpty(ev.EmbeddedName))
            {
                log?.Invoke($"OnScriptExecute 0x{scriptId:X4} on {stem} (embedded {ev.EmbeddedName})");
            }

            if (bytes is not { Length: > 0 })
            {
                return 0;
            }

            if (patch == null)
            {
                patch = new MapRamPatch();
                MapRamStore.LoadLive(stem, patch);
            }

            if (!patch.Scripts.TryGetValue(scriptId, out var cached) ||
                !ReferenceEquals(cached.Bytes, bytes))
            {
                patch.SetScript(scriptId, bytes);
                cached = patch.Scripts[scriptId];
            }

            ip = (uint)cached.Ptr;
            return ip == 0 ? 0 : 1;
        }
    }

    /// <summary>
    /// 1 = use <paramref name="row"/>, -1 = skip, 0 = vanilla lookup.
    /// </summary>
    public static int OnCallHook(string stem, int table, int hookId, out uint row, Action<string>? log = null)
    {
        row = 0;
        lock (Gate)
        {
            if (!Loaded || !Hooks.HasAny)
            {
                return 0;
            }

            stem = (stem ?? "").Trim().ToUpperInvariant();
            var mapId = MapId.Parse(stem);
            var patch = MapRamStore.Get(stem);
            var ev = new CallHookEvent(mapId, hookId, table)
            {
                Row = table == 1 && patch != null && patch.Hooks.TryGetValue(hookId, out var blob)
                    ? blob.Bytes
                    : null,
            };
            Hooks.Invoke("OnCallHook", ev);

            if (ev.Skip)
            {
                return -1;
            }

            if (ev.Dirty)
            {
                if (Config is null)
                {
                    log?.Invoke($"OnCallHook {hookId} on {stem}: no config, vanilla");
                    return 0;
                }

                log?.Invoke($"OnCallHook replace {hookId} on {stem} (assembling)");
                try
                {
                    ev.Row = HookAssembler.Assemble(stem, hookId, ev.Assembler ?? "", Config, log);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"OnCallHook assemble {hookId}: {ex.Message}");
                    return 0;
                }
            }

            if (ev.Row is not { Length: > 0 })
            {
                return 0;
            }

            if (patch == null)
            {
                patch = new MapRamPatch();
                MapRamStore.LoadLive(stem, patch);
            }

            if (!patch.Hooks.TryGetValue(hookId, out var cached) ||
                !ReferenceEquals(cached.Bytes, ev.Row))
            {
                patch.SetHook(hookId, ev.Row);
                cached = patch.Hooks[hookId];
            }

            row = (uint)cached.Ptr;
            return row == 0 ? 0 : 1;
        }
    }
}
