using System.Text;
using Grandia.Sdk;

namespace Grandia.Runtime;

public static class PatchEmitter
{
    public static bool Emit(Map map, ModsConfig cfg, string cacheDir, Action<string>? log = null)
    {
        var patch = map.ToPatchText();
        if (string.IsNullOrWhiteSpace(patch))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cfg.Field) || !Directory.Exists(cfg.Field))
        {
            throw new InvalidOperationException($"FIELD folder missing: {cfg.Field}");
        }

        var exe = FieldTools.RequireExe(cfg);
        Directory.CreateDirectory(cacheDir);
        var dumpDir = Path.Combine(cacheDir, "_emit");
        Directory.CreateDirectory(dumpDir);
        var patchPath = Path.Combine(dumpDir, $"{map.Stem}.patch");
        if (OverlayMatches(cacheDir, map.Stem, patchPath, patch, exe))
        {
            return true;
        }

        File.WriteAllText(patchPath, patch, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var text = string.IsNullOrWhiteSpace(cfg.Text)
            ? Path.Combine(Path.GetDirectoryName(cfg.Field.TrimEnd('\\', '/')) ?? "", "TEXT", "EN")
            : cfg.Text;

        var args =
            $"patch build {FieldTools.Quote(patchPath)} -o {FieldTools.Quote(cacheDir)} --field {FieldTools.Quote(cfg.Field)} --text {FieldTools.Quote(text)}";
        FieldTools.Run(cfg, args, "field_tools patch");

        return true;
    }

    private static bool OverlayMatches(string cacheDir, string stem, string patchPath, string patch,
        string toolPath)
    {
        if (!File.Exists(patchPath) || !File.Exists(toolPath))
        {
            return false;
        }

        if (File.GetLastWriteTimeUtc(toolPath) > File.GetLastWriteTimeUtc(patchPath))
        {
            return false;
        }

        var field = Path.Combine(cacheDir, "FIELD", stem + ".mdp");
        var scn = Path.Combine(cacheDir, "TEXT", "EN", stem + ".SCN");
        if (!File.Exists(field) && !File.Exists(scn))
        {
            return false;
        }

        return File.ReadAllText(patchPath) == patch;
    }
}
