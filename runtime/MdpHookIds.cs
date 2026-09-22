using System.Buffers.Binary;

namespace Grandia.Runtime;

/// <summary>Read table-2 hook ids from an MDP (sec[7], 20-byte rows).</summary>
internal static class MdpHookIds
{
    private const int HeaderSize = 512;
    private const int SectionCount = 64;

    public static IReadOnlyList<int> FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        return ReadTable2(File.ReadAllBytes(path));
    }

    public static List<int> ReadTable2(byte[] mdp) => ReadTable(mdp, 2);

    public static List<int> ReadTable3(byte[] mdp) => ReadTable(mdp, 3);

    public static List<int> ReadTable(byte[] mdp, int table)
    {
        var ids = new List<int>();
        if (mdp.Length < HeaderSize || table is < 2 or > 3)
        {
            return ids;
        }

        if (!TrySection(mdp, 7, out var off, out var len) || len < 0x18)
        {
            return ids;
        }

        var end = Math.Min(off + len, mdp.Length);
        var count = mdp[off + 1 + table];
        var rel = BinaryPrimitives.ReadUInt32LittleEndian(mdp.AsSpan(off + 8 + table * 4, 4));
        const int rowSize = 20;
        for (var i = 0; i < count; i++)
        {
            var at = off + (int)rel + i * rowSize;
            if (at < off || at + rowSize > end)
            {
                break;
            }

            var hid = mdp[at];
            if (hid != 0)
            {
                ids.Add(hid);
            }
        }

        return ids;
    }

    internal static byte[]? TrySlice(byte[]? mdp, int index)
    {
        if (mdp is not { Length: > 0 } || !TrySection(mdp, index, out var off, out var len) || len <= 0)
        {
            return null;
        }

        return mdp.AsSpan(off, Math.Min(len, mdp.Length - off)).ToArray();
    }

    internal static bool TrySection(byte[] mdp, int index, out int off, out int len)
    {
        off = 0;
        len = 0;
        var ptr = BinaryPrimitives.ReadUInt32LittleEndian(mdp.AsSpan(index * 8, 4));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(mdp.AsSpan(index * 8 + 4, 4));
        if (!TryFileOff(ptr, mdp.Length, out var fileOff))
        {
            return false;
        }

        off = fileOff;
        if (size != 0 && size != 0xFFFFFFFF && fileOff + (int)size <= mdp.Length)
        {
            len = (int)size;
            return true;
        }

        var next = NextFileOffAfter(mdp, fileOff);
        len = (next ?? mdp.Length) - fileOff;
        return len > 0;
    }

    private static bool TryFileOff(uint ptr, int fileLen, out int off)
    {
        off = 0;
        if (ptr == 0 || ptr == 0xFFFFFFFF)
        {
            return false;
        }

        if (ptr >= HeaderSize && ptr < fileLen)
        {
            off = (int)ptr;
            return true;
        }

        var masked = (int)(ptr & 0xFFFFFF);
        if (masked >= HeaderSize && masked < fileLen)
        {
            off = masked;
            return true;
        }

        return false;
    }

    private static int? NextFileOffAfter(byte[] mdp, int fileOff)
    {
        int? best = null;
        for (var i = 0; i < SectionCount; i++)
        {
            var ptr = BinaryPrimitives.ReadUInt32LittleEndian(mdp.AsSpan(i * 8, 4));
            if (!TryFileOff(ptr, mdp.Length, out var other) || other <= fileOff)
            {
                continue;
            }

            if (best is null || other < best)
            {
                best = other;
            }
        }

        return best;
    }
}
