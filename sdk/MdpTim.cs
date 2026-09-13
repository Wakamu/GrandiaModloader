namespace Grandia.Sdk;

/// <summary>
/// One LoadImage blit of a decompressed field TIM into the 1024×512
/// word buffer (sec[1] / sec[16] / sec[27]).
/// </summary>
public readonly record struct MapTextureUpload(int Section, int X, int Y, int Width, int Height, int Bytes);

/// <summary>
/// Decode MDP field TIMs (Huffman <c>+0x9630</c>) into the same 1024×512
/// word sheet the GPU LoadImage uses. This is the original PS1 texture,
/// not SoftHD.
/// </summary>
public static class MdpTim
{
    public const int Width = 1024;
    public const int Height = 512;

    public static MapTextureSheet FromMdp(byte[]? mdp)
    {
        var words = new ushort[Width * Height];
        var uploads = new List<MapTextureUpload>();
        if (mdp is not { Length: >= 512 })
        {
            return new MapTextureSheet(words, uploads);
        }

        Ingest(TrySlice(mdp, 1), words, uploads, 1);
        Ingest(TrySlice(mdp, 16), words, uploads, 16);
        Ingest(TrySlice(mdp, 27), words, uploads, 27);
        return new MapTextureSheet(words, uploads);
    }

    public static void Ingest(ReadOnlySpan<byte> blob, ushort[] vram, List<MapTextureUpload> uploads, int section)
    {
        if (blob.Length < 8 || vram.Length < Width * Height)
        {
            return;
        }

        if (BitConverter.ToUInt32(blob) == 8)
        {
            var size = BitConverter.ToInt32(blob.Slice(4));
            if (size > 8 && 8 + size < blob.Length)
            {
                Ingest(blob.Slice(8, size), vram, uploads, section);
                Ingest(blob.Slice(8 + size), vram, uploads, section);
                return;
            }
        }

        var count = BitConverter.ToUInt16(blob);
        if (count is >= 1 and <= 64
            && Mdp9630.TryDecompressFrames(blob, out var frames)
            && TryTim(frames, 0, out var framed))
        {
            Blit(vram, framed, uploads, section);
            return;
        }

        if (Mdp9630.TryDecompress(blob, out var dec) && TryTim(dec, 0, out var decoded))
        {
            Blit(vram, decoded, uploads, section);
            return;
        }

        foreach (var off in (ReadOnlySpan<int>)[0, 8])
        {
            if (TryTim(blob, off, out var raw))
            {
                Blit(vram, raw, uploads, section);
                return;
            }
        }
    }

    public static bool TryTim(ReadOnlySpan<byte> data, int off, out (int X, int Y, int W, int H, byte[] Pix) tim)
    {
        tim = default;
        if (off + 8 > data.Length)
        {
            return false;
        }

        var rec = off;
        if (BitConverter.ToUInt32(data.Slice(off)) == 8 && off + 16 <= data.Length)
        {
            rec = off + 8;
        }

        if (rec + 8 > data.Length)
        {
            return false;
        }

        var x = BitConverter.ToInt16(data.Slice(rec));
        var y = BitConverter.ToInt16(data.Slice(rec + 2));
        var w = BitConverter.ToInt16(data.Slice(rec + 4));
        var h = BitConverter.ToInt16(data.Slice(rec + 6));
        var need = w * h * 2;
        var pix = rec + 8;
        if (w is <= 0 or > Width || h is <= 0 or > Height || need <= 0 || pix + need > data.Length)
        {
            return false;
        }

        tim = (x, y, w, h, data.Slice(pix, need).ToArray());
        return true;
    }

    internal static byte[]? TrySlice(byte[] mdp, int index)
    {
        if (mdp.Length < 512 || index < 0 || index * 8 + 8 > 512)
        {
            return null;
        }

        var ptr = BitConverter.ToUInt32(mdp, index * 8);
        var size = BitConverter.ToUInt32(mdp, index * 8 + 4);
        var off = ptr >= 512 && ptr < (uint)mdp.Length ? (int)ptr : (int)(ptr & 0xFFFFFF);
        if (off < 512 || off >= mdp.Length)
        {
            return null;
        }

        var len = size != 0 && size != 0xFFFFFFFF && off + (int)size <= mdp.Length
            ? (int)size
            : mdp.Length - off;
        return mdp.AsSpan(off, len).ToArray();
    }

    private static void Blit(
        ushort[] vram,
        (int X, int Y, int W, int H, byte[] Pix) tim,
        List<MapTextureUpload> uploads,
        int section)
    {
        var (x, y, w, h, pix) = tim;
        var n = pix.Length / 2;
        for (var row = 0; row < h; row++)
        {
            var destY = y + row;
            if (destY is < 0 or >= Height)
            {
                continue;
            }

            var src = row * w;
            var dst = destY * Width + x;
            for (var col = 0; col < w; col++)
            {
                var destX = x + col;
                if (destX is < 0 or >= Width || src + col >= n)
                {
                    continue;
                }

                vram[dst + col] = BitConverter.ToUInt16(pix, (src + col) * 2);
            }
        }

        uploads.Add(new MapTextureUpload(section, x, y, w, h, pix.Length));
    }
}
