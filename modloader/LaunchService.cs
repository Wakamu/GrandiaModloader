using System.Diagnostics;
using System.Text.Json;

namespace GrandiaModloader;

public sealed class LaunchService
{
    private readonly ModStore _store;
    private CancellationTokenSource? _cts;

    public LaunchService(ModStore store)
    {
        _store = store;
    }

    public event Action<string>? Log;

    public bool IsBusy => _cts is not null;

    public void Cancel() => _cts?.Cancel();

    public async Task LaunchAsync()
    {
        if (_cts is not null)
        {
            throw new InvalidOperationException("Launch already in progress.");
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            var cfg = _store.Config;
            cfg.FillDefaults();

            var dll = Paths.ResolveDllPath();
            var tools = Paths.ResolveFieldTools();
            Log?.Invoke($"field_tools {tools}");
            var overlay = Paths.OverlayDir;
            Directory.CreateDirectory(overlay);
            StageRuntime(dll);
            WriteModsJson(cfg, dll, overlay, tools);

            var existing = DllInjector.FindProcessId("grandia.exe");
            if (existing is int runningPid)
            {
                Log?.Invoke("grandia.exe is already running. C# mods already in that process will not reload — quit the game and Launch again.");
                InjectIfNeeded(runningPid, dll);
                return;
            }

            StartGame(cfg);
            var pid = await DllInjector.WaitForProcessAsync("grandia.exe", token, pollMs: 750, log: msg => Log?.Invoke(msg))
                .ConfigureAwait(false);
            InjectIfNeeded(pid, dll);
            Log?.Invoke("Mods are live. OnMapLoad assembles on fopen; patches apply after map bind.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    private void StageRuntime(string dllPath)
    {
        var dest = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(dest))
        {
            return;
        }

        foreach (var cfg in new[] { "Release", "Debug" })
        {
            var src = Path.Combine(Paths.RepoRoot, "runtime", "bin", cfg, "net8.0");
            if (!Directory.Exists(src))
            {
                continue;
            }

            foreach (var name in new[]
                     {
                         "Grandia.Runtime.dll", "Grandia.Runtime.runtimeconfig.json", "Grandia.Runtime.deps.json",
                         "Grandia.Sdk.dll",
                     })
            {
                var file = Path.Combine(src, name);
                if (File.Exists(file))
                {
                    File.Copy(file, Path.Combine(dest, name), overwrite: true);
                }
            }

            Log?.Invoke($"Staged CLR runtime from {src}");
            return;
        }

        Log?.Invoke("Grandia.Runtime not built yet — cmake/dotnet build runtime, or Launch will fail CLR init.");
    }

    private void WriteModsJson(AppConfig cfg, string dllPath, string overlay, string tools)
    {
        var dest = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(dest))
        {
            throw new InvalidOperationException("GrandiaMod.dll path has no directory.");
        }

        var install = cfg.InstallDir;
        if (string.IsNullOrWhiteSpace(install) || !File.Exists(Path.Combine(install, "grandia.exe")))
        {
            throw new InvalidOperationException(
                "Grandia install folder not found. Set it in Settings (folder that contains grandia.exe).");
        }

        var field = Path.Combine(install, "content", "FIELD");
        var text = Path.Combine(install, "content", "TEXT", "EN");
        if (!Directory.Exists(field))
        {
            throw new InvalidOperationException($"Missing FIELD folder: {field}");
        }

        var mods = new List<object>();
        foreach (var mod in _store.ListMods().Where(m => m.Enabled))
        {
            if (string.IsNullOrWhiteSpace(mod.AssemblyPath) || !File.Exists(mod.AssemblyPath))
            {
                Log?.Invoke($"Skip {mod.Name}: DLL missing at {mod.AssemblyPath}");
                continue;
            }

            Log?.Invoke($"Mod {mod.Name} {mod.Version}: {mod.AssemblyPath}");
            mods.Add(new { id = mod.Id, assembly = mod.AssemblyPath });
        }

        var payload = new
        {
            tools,
            field = Path.GetFullPath(field),
            text = Directory.Exists(text) ? Path.GetFullPath(text) : text,
            cache = Path.GetFullPath(overlay),
            mods,
        };

        var jsonPath = Path.Combine(dest, "mods.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, AppConfig.JsonOptions()));
        Log?.Invoke($"Wrote {jsonPath}");
    }

    private void StartGame(AppConfig cfg)
    {
        var mode = (cfg.LaunchMode ?? "steam").Trim().ToLowerInvariant();
        if (mode == "exe")
        {
            var exe = Path.Combine(cfg.InstallDir, "grandia.exe");
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("grandia.exe not found", exe);
            }

            Log?.Invoke($"Starting {exe}");
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = cfg.InstallDir,
                UseShellExecute = true,
            });
            return;
        }

        var uri = $"steam://rungameid/{cfg.SteamAppId}";
        Log?.Invoke($"Starting Steam ({uri})");
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true,
        });
    }

    private void InjectIfNeeded(int pid, string dll)
    {
        if (DllInjector.IsModuleLoaded(pid, dll, msg => Log?.Invoke(msg)))
        {
            Log?.Invoke("GrandiaMod.dll is already loaded. Quit grandia.exe to load a rebuilt mod DLL.");
            return;
        }

        DllInjector.Inject(pid, dll, msg => Log?.Invoke(msg));
    }
}
