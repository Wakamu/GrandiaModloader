using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandiaModloader;

public sealed class AppConfig
{
    public const int DefaultSteamAppId = 1034860;

    /// <summary>steam = steam://rungameid; exe = start grandia.exe directly.</summary>
    public string LaunchMode { get; set; } = "steam";

    public int SteamAppId { get; set; } = DefaultSteamAppId;

    /// <summary>Grandia HD install folder (contains grandia.exe and content/).</summary>
    public string InstallDir { get; set; } = "";

    /// <summary>Mod ids (DLL file name without extension) in load order.</summary>
    public List<string> ModOrder { get; set; } = [];

    /// <summary>Enabled state keyed by mod id (DLL file name without extension).</summary>
    public Dictionary<string, bool> ModEnabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultConfigPath()
    {
        var portable = Path.Combine(Paths.AppDir, "config.json");
        if (File.Exists(portable))
        {
            return portable;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrandiaModloader");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "config.json");
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new AppConfig();
            fresh.FillDefaults();
            fresh.Save(path);
            return fresh;
        }

        var text = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<AppConfig>(text, JsonOptions()) ?? new AppConfig();
        cfg.FillDefaults();
        return cfg;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions()));
    }

    public void FillDefaults()
    {
        if (string.IsNullOrWhiteSpace(LaunchMode))
        {
            LaunchMode = "steam";
        }

        if (SteamAppId <= 0)
        {
            SteamAppId = DefaultSteamAppId;
        }

        if (string.IsNullOrWhiteSpace(InstallDir))
        {
            InstallDir = Paths.DetectInstallDir() ?? "";
        }

        ModOrder ??= [];
        ModEnabled ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    public static JsonSerializerOptions JsonOptions() =>
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
}

public static class Paths
{
    public static string AppDir => AppContext.BaseDirectory;

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppDir);
            for (var i = 0; i < 8 && dir is not null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "CMakeLists.txt")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "modloader")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return AppDir;
        }
    }

    public static string ModsDir => Path.Combine(UserDataRoot, "mods");

    public static string OverlayDir => Path.Combine(UserDataRoot, "overlay");

    /// <summary>
    /// Writable per-user data folder.
    /// Dev builds: repo root (next to CMakeLists.txt).
    /// Installed builds: %AppData%\GrandiaModloader.
    /// </summary>
    public static string UserDataRoot
    {
        get
        {
            if (IsSourceTree)
            {
                return RepoRoot;
            }

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GrandiaModloader");
            Directory.CreateDirectory(appData);
            return appData;
        }
    }

    public static bool IsSourceTree =>
        File.Exists(Path.Combine(RepoRoot, "CMakeLists.txt")) &&
        Directory.Exists(Path.Combine(RepoRoot, "modloader"));

    /// <summary>
    /// Frozen field assembler. Not a setting — ship
    /// <c>field_tools\field_tools.exe</c> next to GrandiaModloader.exe.
    /// </summary>
    public static string ResolveFieldTools()
    {
        foreach (var candidate in FieldToolsCandidates())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "field_tools.exe not found. Run python tools\\build_field_tools.py and copy field_tools\\ next to GrandiaModloader.exe.");
    }

    private static IEnumerable<string> FieldToolsCandidates()
    {
        yield return Path.Combine(AppDir, "field_tools", "field_tools.exe");
        yield return Path.Combine(AppDir, "field_tools.exe");
        yield return Path.Combine(RepoRoot, "vendor", "field_tools", "field_tools.exe");
    }

    public static string? DetectInstallDir()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster",
            @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster",
            @"C:\Program Files\Steam\steamapps\common\GRANDIA HD Remaster",
        ];
        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(c, "grandia.exe")))
            {
                return c;
            }
        }

        return null;
    }

    public static string ResolveDllPath()
    {
        string[] candidates =
        [
            Path.Combine(AppDir, "GrandiaMod.dll"),
            Path.Combine(RepoRoot, "build", "Release", "GrandiaMod.dll"),
            Path.Combine(RepoRoot, "build", "Debug", "GrandiaMod.dll"),
        ];
        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }

        throw new FileNotFoundException(
            "GrandiaMod.dll not found. Build the Win32 DLL (cmake -A Win32) and copy it next to GrandiaModloader.exe.");
    }
}
