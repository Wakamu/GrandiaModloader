using Grandia.Sdk;

namespace Grandia.Runtime;

internal static class ScriptHydrator
{
    private static readonly Dictionary<string, List<(int Id, byte[] Bytes)>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Hydrate(Map map, ModsConfig cfg, string cacheDir, Action<string>? log)
    {
        try
        {
            var scripts = Load(map.Stem, cfg);
            map.AttachBytecode(scripts, map.Stem);
            log?.Invoke($"hydrated {scripts.Count} script(s) on {map.Stem}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"script hydrate {map.Stem}: {ex.Message}");
        }
    }

    public static bool TryGet(string stem, int scriptId, ModsConfig? cfg, out byte[] bytes)
    {
        bytes = [];
        if (cfg is null || string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        foreach (var (id, blob) in Load(stem, cfg))
        {
            if (id == scriptId)
            {
                bytes = blob;
                return blob.Length > 0;
            }
        }

        return false;
    }

    private static List<(int Id, byte[] Bytes)> Load(string stem, ModsConfig cfg)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(stem, out var hit))
            {
                return hit;
            }
        }

        var list = FieldScriptBank.LoadAll(cfg.Text, stem);
        lock (Cache)
        {
            Cache[stem] = list;
        }

        return list;
    }
}
