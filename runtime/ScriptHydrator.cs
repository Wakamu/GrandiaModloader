using System.Text.Json;
using Grandia.Sdk;

namespace Grandia.Runtime;

internal static class ScriptHydrator
{
    public static void Hydrate(Map map, ModsConfig cfg, string cacheDir, Action<string>? log)
    {
        try
        {
            var scripts = Load(map.Stem, cfg, cacheDir);
            map.HydrateScripts(scripts);
            log?.Invoke($"hydrated {scripts.Count} script(s) on {map.Stem}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"script hydrate {map.Stem}: {ex.Message}");
        }
    }

    private static List<(int Id, string Text)> Load(string stem, ModsConfig cfg, string cacheDir)
    {
        // Always dump vanilla FIELD/TEXT. Overlay files are emit output; using
        // them as input changes the stamp after every emit and re-spawns field_tools.
        var textRoot = cfg.Text;
        var fieldRoot = cfg.Field;
        var dump = Path.Combine(cacheDir, "_ir", stem + ".scripts.json");
        var stamp = DumpStamp(textRoot, fieldRoot, stem);
        if (File.Exists(dump) && File.Exists(dump + ".stamp") &&
            File.ReadAllText(dump + ".stamp") == stamp)
        {
            return ReadDump(dump);
        }

        RunDump(stem, dump, cfg, fieldRoot, textRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
        File.WriteAllText(dump + ".stamp", stamp);
        return ReadDump(dump);
    }

    private static string DumpStamp(string textRoot, string fieldRoot, string stem)
    {
        var scn = FirstExisting(textRoot, stem + ".SCN", stem + ".scn");
        var ofs = FirstExisting(textRoot, stem + ".OFS", stem + ".ofs");
        var mdp = FirstExisting(fieldRoot, stem + ".mdp", stem + ".MDP");
        static string Tick(string? path) =>
            path is null ? "0" : File.GetLastWriteTimeUtc(path).Ticks.ToString();
        return $"{scn}|{Tick(scn)}|{ofs}|{Tick(ofs)}|{mdp}|{Tick(mdp)}";
    }

    private static string? FirstExisting(string root, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        foreach (var name in names)
        {
            var p = Path.Combine(root, name);
            if (File.Exists(p))
            {
                return p;
            }
        }

        return null;
    }

    private static void RunDump(string stem, string dump, ModsConfig cfg, string field, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
        FieldTools.Run(cfg,
            $"script {stem} --dump {FieldTools.Quote(dump)} --field {FieldTools.Quote(field)} --text {FieldTools.Quote(text)}",
            "field_tools dump");
    }

    private static List<(int Id, string Text)> ReadDump(string dump)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(dump));
        var list = new List<(int, string)>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = el.GetProperty("id").GetInt32();
            var text = el.GetProperty("text").GetString() ?? "";
            list.Add((id, text));
        }

        return list;
    }
}
