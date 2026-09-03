using System.Security.Cryptography;
using System.Text;

namespace Grandia.Runtime;

internal static class HookAssembler
{
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);

    public static byte[] Assemble(string stem, int hookId, string asm, ModsConfig cfg,
        Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(asm))
        {
            return [];
        }

        var key = Hash(stem, hookId, asm);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var bytes = RunEmit(stem, hookId, asm, cfg);
        lock (Cache)
        {
            Cache[key] = bytes;
        }

        log?.Invoke($"assemble hook {hookId} on {stem} ({bytes.Length} bytes)");
        return bytes;
    }

    private static string Hash(string stem, int hookId, string asm)
    {
        var raw = Encoding.UTF8.GetBytes($"{stem}\n{hookId:X4}\n{asm}");
        return Convert.ToHexString(SHA256.HashData(raw));
    }

    private static byte[] RunEmit(string stem, int hookId, string asm, ModsConfig cfg)
    {
        var cache = string.IsNullOrWhiteSpace(cfg.Cache)
            ? Path.Combine(Path.GetTempPath(), "GrandiaMod")
            : cfg.Cache;
        var dir = Path.Combine(cache, "_asm");
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, $"{stem}_hook_{hookId:X4}.asm");
        var dest = Path.Combine(dir, $"{stem}_hook_{hookId:X4}.bin");
        File.WriteAllText(src, asm, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        FieldTools.Run(cfg, $"hook --emit {FieldTools.Quote(src)} -o {FieldTools.Quote(dest)}", "field_tools hook");
        var bytes = File.Exists(dest) ? File.ReadAllBytes(dest) : [];
        if (bytes.Length != 20)
        {
            throw new InvalidOperationException($"field_hook_asm --emit produced {bytes.Length} bytes, want 20.");
        }

        return bytes;
    }
}
