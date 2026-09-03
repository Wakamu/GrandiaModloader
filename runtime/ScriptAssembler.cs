using System.Security.Cryptography;
using System.Text;

namespace Grandia.Runtime;

internal static class ScriptAssembler
{
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    public static byte[] Assemble(string stem, int scriptId, string asm, ModsConfig cfg,
        Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(asm))
        {
            return [];
        }

        var key = Hash(stem, scriptId, asm);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var bytes = RunEmit(stem, scriptId, asm, cfg);
        lock (Cache)
        {
            Cache[key] = bytes;
        }

        log?.Invoke($"assemble script 0x{scriptId:X4} on {stem} ({bytes.Length} bytes)");
        return bytes;
    }

    private static string Hash(string stem, int scriptId, string asm)
    {
        var raw = Encoding.UTF8.GetBytes($"{stem}\n{scriptId:X4}\n{asm}");
        return Convert.ToHexString(SHA256.HashData(raw));
    }

    private static byte[] RunEmit(string stem, int scriptId, string asm, ModsConfig cfg)
    {
        var cache = string.IsNullOrWhiteSpace(cfg.Cache)
            ? Path.Combine(Path.GetTempPath(), "GrandiaMod")
            : cfg.Cache;
        var dir = Path.Combine(cache, "_asm");
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, $"{stem}_{scriptId:X4}.asm");
        var dest = Path.Combine(dir, $"{stem}_{scriptId:X4}.bin");
        File.WriteAllText(src, asm, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var stemArg = string.IsNullOrWhiteSpace(stem) ? "" : $"{stem} ";
        FieldTools.Run(cfg, $"script {stemArg}--emit {FieldTools.Quote(src)} -o {FieldTools.Quote(dest)}",
            "field_tools script");
        return File.Exists(dest) ? File.ReadAllBytes(dest) : [];
    }
}
