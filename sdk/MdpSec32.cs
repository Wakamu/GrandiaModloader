namespace Grandia.Sdk;

/// <summary>
/// MDP sec[32] UV / tpage directory (copied at <c>+0x7271C</c>).
/// Two relocated blocks of <c>{u8 u, v, w, h, u16 tpage}</c>.
/// Block 0 skins sec[0] 3D prims (left tpages). Block 1 is party /
/// named town sprites on sec[27] right tpages. Read-only.
/// </summary>
public static class MdpSec32
{
    public static (IReadOnlyList<MapSpriteUv> Block0, IReadOnlyList<MapSpriteUv> Block1) Parse(
        ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 8)
        {
            return ([], []);
        }

        var inner0 = RelocPair(blob, ReadU32(blob, 0));
        var inner1 = RelocPair(blob, ReadU32(blob, 4));
        var block0 = inner0 is { } a
            ? ReadBlock(blob, a.Uv, a.Clut, 0, ReadU32(blob, 4), CountHint(blob, 0x10))
            : [];
        var block1 = inner1 is { } b
            ? ReadBlock(blob, b.Uv, b.Clut, 1, blob.Length, null)
            : [];
        return (block0, block1);
    }

    public static IReadOnlyList<MapSpriteUv> ParseAll(ReadOnlySpan<byte> blob)
    {
        var (a, b) = Parse(blob);
        var all = new List<MapSpriteUv>(a.Count + b.Count);
        all.AddRange(a);
        all.AddRange(b);
        return all;
    }

    private static int? CountHint(ReadOnlySpan<byte> blob, int off)
    {
        if (blob.Length < off + 4)
        {
            return null;
        }

        var n = ReadU32(blob, off);
        return n > 0 ? n : null;
    }

    private static IReadOnlyList<MapSpriteUv> ReadBlock(
        ReadOnlySpan<byte> blob,
        int uvOff,
        int clutOff,
        int block,
        int end,
        int? max)
    {
        if (uvOff < 0 || clutOff < uvOff || clutOff > end || clutOff > blob.Length)
        {
            return [];
        }

        var n = (clutOff - uvOff) / 6;
        if (max is > 0 && max < n)
        {
            n = max.Value;
        }

        var list = new MapSpriteUv[n];
        for (var i = 0; i < n; i++)
        {
            var at = uvOff + i * 6;
            if (at + 6 > blob.Length)
            {
                return list.AsSpan(0, i).ToArray();
            }

            var clutAt = clutOff + i * 2;
            var clut = clutAt + 2 <= blob.Length
                ? BitConverter.ToUInt16(blob.Slice(clutAt))
                : 0;
            list[i] = new MapSpriteUv(
                block,
                blob[at],
                blob[at + 1],
                blob[at + 2],
                blob[at + 3],
                BitConverter.ToUInt16(blob.Slice(at + 4)),
                clut);
        }

        return list;
    }

    private static (int Uv, int Clut)? RelocPair(ReadOnlySpan<byte> blob, int off)
    {
        if (off < 0 || off + 8 > blob.Length)
        {
            return null;
        }

        var a = ReadU32(blob, off);
        if (a == -1)
        {
            return null;
        }

        var b = ReadU32(blob, off + 4);
        return (a + off + 4, b + off + 4);
    }

    private static int ReadU32(ReadOnlySpan<byte> blob, int off) =>
        off + 4 <= blob.Length ? BitConverter.ToInt32(blob.Slice(off)) : 0;
}
