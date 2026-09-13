namespace Grandia.Sdk;

/// <summary>
/// SoftHD anim/party footer row: FNV-1a of the PS1 VRAM texels,
/// sign-extended to 64 bits, plus the atlas crop.
/// </summary>
public readonly record struct HdAnimMatch(int Index, HdSpriteRect Rect, ulong Key);

/// <summary>One occupied 64×64 VRAM tile (word coordinates).</summary>
public readonly record struct HdVramTile(int X, int Y, int NonZero);

/// <summary>Non-zero footprint of a 1024×512 VRAM buffer.</summary>
public readonly record struct HdVramOccupancy(
    int Which,
    int NonZeroWords,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    IReadOnlyList<HdVramTile> Tiles);

/// <summary>A VRAM rectangle whose FNV matches a footer key.</summary>
public readonly record struct HdVramHit(
    int Which,
    int X,
    int Y,
    int WordWidth,
    int Height,
    uint Hash,
    int Index);

/// <summary>
/// SoftHD <c>+0x1A620</c> sprite key: FNV-1a over 16-bit VRAM words
/// (1024-wide stride), with a zero-avoidance quirk. Footer keys in
/// <c>*_anim__spriteinfo.bin</c> are this 32-bit value sign-extended.
/// The pose cookie is the PS1 UV (<c>u,v</c> in the first two bytes),
/// not the key.
/// </summary>
public static class HdSpriteKey
{
    public const uint FnvOffset = 0x811C9DC5;
    public const uint FnvPrime = 0x01000193;
    public const int VramStride = 1024;
    public const int TpageMask = 0x9FF;

    /// <summary>
    /// FNV-1a of <paramref name="words"/> in draw order (left-to-right,
    /// top-to-bottom). Empty → 0. SoftHD resets a 0 accumulator to
    /// <see cref="FnvOffset"/> so a hashed sprite is never 0.
    /// </summary>
    public static uint HashTexels(ReadOnlySpan<ushort> words)
    {
        uint hash = 0;
        foreach (var word in words)
        {
            if (hash == 0)
            {
                hash = FnvOffset;
            }

            hash ^= (byte)word;
            hash *= FnvPrime;
            if (hash == 0)
            {
                hash = FnvOffset;
            }

            hash ^= (byte)(word >> 8);
            hash *= FnvPrime;
        }

        return hash;
    }

    public static ulong SignExtend(uint hash) => (ulong)(int)hash;

    /// <summary>
    /// PS1 tpage → VRAM origin and colour depth. Page Y is only bit 4
    /// (0 or 256). SoftHD <c>+0x1AE00</c> also ors <c>(tpage&gt;&gt;2)&amp;0x200</c>,
    /// but the live buffer is 1024×512 (1 MiB at <c>[0x63F880]</c>) so
    /// Y=512 is off the end. BA38 cinematic <c>0x8808</c> is page X=512,
    /// 4bpp, Y=0.
    /// </summary>
    public static bool TryMapRect(
        int tpage,
        int u,
        int v,
        int width,
        int height,
        out int vramX,
        out int vramY,
        out int wordWidth,
        out int bpp)
    {
        vramX = 0;
        vramY = 0;
        wordWidth = 0;
        bpp = (tpage & 0x180) == 0 ? 4 : 2;
        if (width <= 0 || height <= 0 || u < 0 || v < 0)
        {
            return false;
        }

        var pageX = (tpage & 0xF) << 6;
        var pageY = (tpage & 0x10) << 4;
        vramX = pageX + u / bpp;
        vramY = pageY + v;
        // SPRT hash box: maxU = u+w (inclusive +1), maxV = v+h-1.
        wordWidth = (width + 1) / bpp;
        return wordWidth > 0
            && vramX >= 0
            && vramY >= 0
            && vramY + height <= 512
            && vramX + wordWidth <= VramStride;
    }

    /// <summary>
    /// Hash a rectangle in a 16-bit VRAM buffer (stride
    /// <see cref="VramStride"/> words unless overridden).
    /// All-zero rectangles return 0 — SoftHD would still emit a
    /// non-zero FNV, but that key is never in the footer.
    /// </summary>
    public static uint HashVram(
        ReadOnlySpan<ushort> vram,
        int x,
        int y,
        int wordWidth,
        int height,
        int stride = VramStride)
    {
        if (wordWidth <= 0 || height <= 0 || x < 0 || y < 0 || stride <= 0)
        {
            return 0;
        }

        uint hash = 0;
        var any = false;
        for (var row = 0; row < height; row++)
        {
            var at = (y + row) * stride + x;
            if (at < 0 || at + wordWidth > vram.Length)
            {
                return 0;
            }

            var line = vram.Slice(at, wordWidth);
            for (var col = 0; col < wordWidth; col++)
            {
                var word = line[col];
                if (word != 0)
                {
                    any = true;
                }

                if (hash == 0)
                {
                    hash = FnvOffset;
                }

                hash ^= (byte)word;
                hash *= FnvPrime;
                if (hash == 0)
                {
                    hash = FnvOffset;
                }

                hash ^= (byte)(word >> 8);
                hash *= FnvPrime;
            }
        }

        return any ? hash : 0;
    }

    /// <summary>
    /// 64×64 occupancy of a 1024×512 VRAM buffer. Empty tiles are omitted.
    /// </summary>
    public static HdVramOccupancy DescribeVram(ReadOnlySpan<ushort> vram, int which, int tile = 64)
    {
        if (tile <= 0 || vram.Length < VramStride)
        {
            return new HdVramOccupancy(which, 0, 0, 0, -1, -1, []);
        }

        var height = Math.Min(vram.Length / VramStride, 512);
        var tiles = new List<HdVramTile>();
        var nonZero = 0;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = -1;
        var maxY = -1;
        for (var ty = 0; ty < height; ty += tile)
        {
            var th = Math.Min(tile, height - ty);
            for (var tx = 0; tx < VramStride; tx += tile)
            {
                var tw = Math.Min(tile, VramStride - tx);
                var n = 0;
                for (var row = 0; row < th; row++)
                {
                    var line = vram.Slice((ty + row) * VramStride + tx, tw);
                    for (var col = 0; col < tw; col++)
                    {
                        if (line[col] != 0)
                        {
                            n++;
                        }
                    }
                }

                if (n == 0)
                {
                    continue;
                }

                nonZero += n;
                tiles.Add(new HdVramTile(tx, ty, n));
                if (tx < minX)
                {
                    minX = tx;
                }

                if (ty < minY)
                {
                    minY = ty;
                }

                if (tx + tw - 1 > maxX)
                {
                    maxX = tx + tw - 1;
                }

                if (ty + th - 1 > maxY)
                {
                    maxY = ty + th - 1;
                }
            }
        }

        if (nonZero == 0)
        {
            return new HdVramOccupancy(which, 0, 0, 0, -1, -1, tiles);
        }

        return new HdVramOccupancy(which, nonZero, minX, minY, maxX, maxY, tiles);
    }

    /// <summary>
    /// Slide <paramref name="sizes"/> across occupied VRAM and report
    /// rectangles whose FNV matches a footer key.
    /// Large boxes only try tile origins (part-sized boxes slide by 1).
    /// </summary>
    public static IReadOnlyList<HdVramHit> FindKeys(
        ReadOnlySpan<ushort> vram,
        int which,
        IReadOnlyList<HdAnimMatch> lookup,
        IEnumerable<(int WordWidth, int Height)> sizes,
        HdVramOccupancy occupancy,
        int maxHits = 64)
    {
        var byHash = new Dictionary<uint, int>();
        foreach (var row in lookup)
        {
            if (row.Key == 0)
            {
                continue;
            }

            byHash[(uint)row.Key] = row.Index;
        }

        if (byHash.Count == 0 || occupancy.NonZeroWords <= 0 || occupancy.MaxX < occupancy.MinX)
        {
            return [];
        }

        var hits = new List<HdVramHit>();
        var seen = new HashSet<(int X, int Y, int Ww, int H, uint Hash)>();
        foreach (var (ww, h) in sizes.Distinct())
        {
            if (ww <= 0 || h <= 0 || ww > 256 || h > 256)
            {
                continue;
            }

            // Slide only inside occupied tiles. The overall bbox is often
            // the whole 1024×512 because field TIM and sprites sit apart.
            var step = ww * h <= 256 ? 1 : 16;
            foreach (var tile in occupancy.Tiles)
            {
                var x0 = tile.X;
                var y0 = tile.Y;
                var x1 = Math.Min(tile.X + 63, VramStride - ww);
                var y1 = Math.Min(tile.Y + 63, 512 - h);
                if (x1 < x0 || y1 < y0)
                {
                    continue;
                }

                for (var y = y0; y <= y1 && hits.Count < maxHits; y += step)
                {
                    for (var x = x0; x <= x1 && hits.Count < maxHits; x += step)
                    {
                        var hash = HashVram(vram, x, y, ww, h);
                        if (hash == 0 || !byHash.TryGetValue(hash, out var idx))
                        {
                            continue;
                        }

                        if (!seen.Add((x, y, ww, h, hash)))
                        {
                            continue;
                        }

                        hits.Add(new HdVramHit(which, x, y, ww, h, hash, idx));
                    }
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// Footer after the xywh table: <c>u16 extraCount</c>, <c>u16 mapCount</c>,
    /// then <c>mapCount</c> records. v1 is 13 bytes
    /// (<c>u64 key, u16 index, u16, u8</c>); v4 prepends one extra byte.
    /// </summary>
    public static IReadOnlyList<HdAnimMatch> ParseLookup(
        IReadOnlyList<byte> blob,
        IReadOnlyList<HdSpriteRect>? rects = null)
    {
        var copy = blob as byte[] ?? blob.ToArray();
        return ParseLookup((ReadOnlySpan<byte>)copy, rects);
    }

    public static IReadOnlyList<HdAnimMatch> ParseLookup(
        ReadOnlySpan<byte> blob,
        IReadOnlyList<HdSpriteRect>? rects = null)
    {
        rects ??= HdTexturePath.ParseSpriteInfo(blob);
        var list = new HdAnimMatch[rects.Count];
        for (var i = 0; i < list.Length; i++)
        {
            list[i] = new HdAnimMatch(i, rects[i], 0);
        }

        if (blob.Length < 10
            || blob[0] != (byte)'S'
            || blob[1] != (byte)'P'
            || blob[2] != (byte)'R'
            || blob[3] != (byte)'I'
            || blob[4] != (byte)'V')
        {
            return list;
        }

        var ver = blob[5];
        var countOff = ver == 1 ? 6 : 7;
        var dataOff = ver == 1 ? 8 : 9;
        if (blob.Length < dataOff + 4)
        {
            return list;
        }

        var count = BitConverter.ToUInt16(blob.Slice(countOff));
        var rectEnd = dataOff + count * 8;
        if (rectEnd + 4 > blob.Length)
        {
            return list;
        }

        var extraN = BitConverter.ToUInt16(blob.Slice(rectEnd));
        var mapN = BitConverter.ToUInt16(blob.Slice(rectEnd + 2));
        var p = rectEnd + 4 + extraN * 8;
        var rec = ver >= 4 ? 14 : 13;
        for (var i = 0; i < mapN && p + rec <= blob.Length; i++)
        {
            var off = ver >= 4 ? p + 1 : p;
            var lo = BitConverter.ToUInt32(blob.Slice(off));
            var hi = BitConverter.ToUInt32(blob.Slice(off + 4));
            var idx = BitConverter.ToUInt16(blob.Slice(off + 8));
            if (idx < list.Length)
            {
                list[idx] = new HdAnimMatch(idx, list[idx].Rect, ((ulong)hi << 32) | lo);
            }

            p += rec;
        }

        return list;
    }

    public static bool TryFindIndex(IReadOnlyList<HdAnimMatch> lookup, uint hash, out int index)
    {
        var key = SignExtend(hash);
        for (var i = 0; i < lookup.Count; i++)
        {
            if (lookup[i].Key == key)
            {
                index = lookup[i].Index;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// Tenants / maps v4 footer key from SoftHD <c>+0x2EF10</c>
    /// (<c>u,v,w,h</c> in the low dword, tpage + clut in the high).
    /// Anim v1 FNV keys (hi = 0 / <c>FFFFFFFF</c>) return false.
    /// </summary>
    public static bool TryUnpackUv(
        ulong key,
        out int u,
        out int v,
        out int w,
        out int h,
        out int tpage,
        out int clut)
    {
        u = 0;
        v = 0;
        w = 0;
        h = 0;
        tpage = 0;
        clut = 0;
        var lo = (uint)key;
        var hi = (uint)(key >> 32);
        if (key == 0 || hi is 0 or 0xFFFFFFFF)
        {
            return false;
        }

        u = (int)(lo & 0xFF);
        v = (int)((lo >> 8) & 0xFF);
        w = (int)((lo >> 16) & 0xFF);
        h = (int)((lo >> 24) & 0xFF);
        tpage = (int)(hi & 0xFFFF);
        clut = (int)(hi >> 16);
        return w > 0 && h > 0;
    }

    /// <summary>First two cookie bytes are the SPRT <c>u,v</c>.</summary>
    public static bool TryCookieUv(IReadOnlyList<byte> cookie, out int u, out int v)
    {
        if (cookie is { Count: >= 2 })
        {
            u = cookie[0];
            v = cookie[1];
            return true;
        }

        u = 0;
        v = 0;
        return false;
    }
}
