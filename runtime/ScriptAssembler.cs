using System.Security.Cryptography;
using System.Text;
using Grandia.Sdk;

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

        _ = cfg;
        var bytes = FieldScriptAsm.Assemble(asm, scriptId, stem);
        lock (Cache)
        {
            Cache[key] = bytes;
        }

        return bytes;
    }

    private static string Hash(string stem, int scriptId, string asm)
    {
        var raw = Encoding.UTF8.GetBytes($"{stem}\n{scriptId:X4}\n{asm}");
        return Convert.ToHexString(SHA256.HashData(raw));
    }
}
