using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class HdSpriteKeyTests
{
    [Fact]
    public void HashTexels_matches_softhd_1a620()
    {
        Assert.Equal(0u, HdSpriteKey.HashTexels([]));
        Assert.Equal(0xEB741D64u, HdSpriteKey.HashTexels([0x0001]));
        Assert.Equal(0xFB0E96D3u, HdSpriteKey.HashTexels([0x1234, 0xABCD]));
        Assert.Equal(0x117697CDu, HdSpriteKey.HashTexels([0x0000]));
        Assert.Equal(0x6F2A929Du, HdSpriteKey.HashTexels([0x1111, 0x2222, 0x3333, 0x4444]));
    }

    [Fact]
    public void SignExtend_matches_anim_footer()
    {
        Assert.Equal(0xFFFFFFFFC01CC90Dul, HdSpriteKey.SignExtend(0xC01CC90D));
        Assert.Equal(0x000000005184D568ul, HdSpriteKey.SignExtend(0x5184D568));
    }

    [Fact]
    public void TryMapRect_decodes_ba38_cinematic_tpage()
    {
        Assert.True(HdSpriteKey.TryMapRect(0x8808, 0x2F, 0x40, 52, 6,
            out var x, out var y, out var words, out var bpp));
        Assert.Equal(4, bpp);
        Assert.Equal(512 + 0x2F / 4, x);
        Assert.Equal(0x40, y);
        Assert.Equal(53 / 4, words);

        Assert.True(HdSpriteKey.TryMapRect(0x8808 & 0x1FF, 0x2F, 0x40, 52, 6,
            out x, out y, out words, out bpp));
        Assert.Equal(512 + 0x2F / 4, x);
        Assert.Equal(0x40, y);
        Assert.Equal(53 / 4, words);
    }

    [Fact]
    public void HashVram_reads_stride_1024()
    {
        var vram = new ushort[HdSpriteKey.VramStride * 3];
        vram[HdSpriteKey.VramStride + 2] = 0x1234;
        vram[HdSpriteKey.VramStride + 3] = 0xABCD;
        Assert.Equal(
            HdSpriteKey.HashTexels([0x1234, 0xABCD]),
            HdSpriteKey.HashVram(vram, 2, 1, 2, 1));
    }

    [Fact]
    public void ParseLookup_reads_v1_footer()
    {
        var blob = new byte[8 + 8 + 4 + 13];
        blob[0] = (byte)'S';
        blob[1] = (byte)'P';
        blob[2] = (byte)'R';
        blob[3] = (byte)'I';
        blob[4] = (byte)'V';
        blob[5] = 1;
        BitConverter.TryWriteBytes(blob.AsSpan(6), (ushort)1);
        BitConverter.TryWriteBytes(blob.AsSpan(8), (ushort)10);
        BitConverter.TryWriteBytes(blob.AsSpan(10), (ushort)20);
        BitConverter.TryWriteBytes(blob.AsSpan(12), (ushort)30);
        BitConverter.TryWriteBytes(blob.AsSpan(14), (ushort)40);
        BitConverter.TryWriteBytes(blob.AsSpan(16), (ushort)0);
        BitConverter.TryWriteBytes(blob.AsSpan(18), (ushort)1);
        BitConverter.TryWriteBytes(blob.AsSpan(20), 0xC01CC90Du);
        BitConverter.TryWriteBytes(blob.AsSpan(24), 0xFFFFFFFFu);
        BitConverter.TryWriteBytes(blob.AsSpan(28), (ushort)0);
        blob[30] = 0xFF;
        blob[31] = 0xFF;
        blob[32] = 0xFF;

        var lookup = HdSpriteKey.ParseLookup((ReadOnlySpan<byte>)blob);
        var row = Assert.Single(lookup);
        Assert.Equal(0, row.Index);
        Assert.Equal(new HdSpriteRect(10, 20, 30, 40), row.Rect);
        Assert.Equal(0xFFFFFFFFC01CC90Dul, row.Key);
        Assert.True(HdSpriteKey.TryFindIndex(lookup, 0xC01CC90D, out var idx));
        Assert.Equal(0, idx);
    }

    [Fact]
    public void TryUnpackUv_skips_sign_extended_fnv()
    {
        Assert.False(HdSpriteKey.TryUnpackUv(HdSpriteKey.SignExtend(0xC01CC90D), out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void TryCookieUv_reads_first_two_bytes()
    {
        Assert.True(HdSpriteKey.TryCookieUv([0x2F, 0x40, 0x1E, 0xE5], out var u, out var v));
        Assert.Equal(0x2F, u);
        Assert.Equal(0x40, v);
        Assert.False(HdSpriteKey.TryCookieUv([], out _, out _));
    }

    [Fact]
    public void HashTexels_zero_13x8_is_ba38_probe()
    {
        Assert.Equal(0x58554005u, HdSpriteKey.HashTexels(new ushort[13 * 8]));
    }

    [Fact]
    public void HashVram_rejects_all_zero()
    {
        Assert.Equal(0u, HdSpriteKey.HashVram(new ushort[HdSpriteKey.VramStride * 8], 0, 0, 13, 8));
    }

    [Fact]
    public void FindKeys_hits_planted_footer()
    {
        var vram = new ushort[HdSpriteKey.VramStride * 8];
        vram[HdSpriteKey.VramStride + 2] = 0x1234;
        vram[HdSpriteKey.VramStride + 3] = 0xABCD;
        var hash = HdSpriteKey.HashVram(vram, 2, 1, 2, 1);
        var lookup = new[] { new HdAnimMatch(7, default, HdSpriteKey.SignExtend(hash)) };
        var occ = HdSpriteKey.DescribeVram(vram, 0, tile: 64);
        Assert.True(occ.NonZeroWords > 0);
        var hit = Assert.Single(HdSpriteKey.FindKeys(vram, 0, lookup, [(2, 1)], occ));
        Assert.Equal(2, hit.X);
        Assert.Equal(1, hit.Y);
        Assert.Equal(7, hit.Index);
    }
}
