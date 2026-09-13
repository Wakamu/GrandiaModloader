using System.Security.Cryptography;
using System.Text;
using Grandia.Sdk;

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

        _ = cfg;
        var bytes = FieldHookAsm.AssembleHook(asm, hookId);
        if (bytes.Length != FieldHookAsm.HookRowSize)
        {
            throw new InvalidOperationException($"FieldHookAsm produced {bytes.Length} bytes, want 20.");
        }

        lock (Cache)
        {
            Cache[key] = bytes;
        }

        return bytes;
    }

    private static string Hash(string stem, int hookId, string asm)
    {
        var raw = Encoding.UTF8.GetBytes($"{stem}\n{hookId:X4}\n{asm}");
        return Convert.ToHexString(SHA256.HashData(raw));
    }
}
