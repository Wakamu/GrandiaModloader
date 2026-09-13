using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class HdTextureTests
{
    [Theory]
    [InlineData(@"C:\game\content\FIELD\2000_maps__atlas.png", "2000", HdAssetKind.Maps, HdAssetFile.Atlas, null)]
    [InlineData("2000_tenants__spriteinfo.bin", "2000", HdAssetKind.Tenants, HdAssetFile.SpriteInfo, null)]
    [InlineData("BA38_anim__atlas.PNG", "BA38", HdAssetKind.Anim, HdAssetFile.Atlas, null)]
    [InlineData("7400_mapeff__atlas.png", "7400", HdAssetKind.MapEff, HdAssetFile.Atlas, null)]
    [InlineData("fc01_faces__atlas.png", "FC01", HdAssetKind.Faces, HdAssetFile.Atlas, null)]
    [InlineData("pgr00_party__spriteinfo.bin", "PGR00", HdAssetKind.Party, HdAssetFile.SpriteInfo, null)]
    [InlineData("areamap_areamap__atlas.png", "AREAMAP", HdAssetKind.AreaMap, HdAssetFile.Atlas, null)]
    [InlineData("areamap_areamap__atlas.jp.png", "AREAMAP", HdAssetKind.AreaMap, HdAssetFile.Atlas, "jp")]
    [InlineData("areamap_areamap__atlas.png.sc", "AREAMAP", HdAssetKind.AreaMap, HdAssetFile.Atlas, "sc")]
    [InlineData("2410_maps__atlas_tables.png", "2410", HdAssetKind.Maps, HdAssetFile.AtlasTables, null)]
    [InlineData("logo_logo__atlas.png", "LOGO", HdAssetKind.Logo, HdAssetFile.Atlas, null)]
    [InlineData("areamap_areamap__atlas.kr.png", "AREAMAP", HdAssetKind.AreaMap, HdAssetFile.Atlas, "kr")]
    [InlineData("2000_tenants", "2000", HdAssetKind.Tenants, HdAssetFile.Unknown, null)]
    [InlineData("content/field/fc01_faces", "FC01", HdAssetKind.Faces, HdAssetFile.Unknown, null)]
    [InlineData(@"FIELD\BA38_tenants", "BA38", HdAssetKind.Tenants, HdAssetFile.Unknown, null)]
    public void TryParse_reads_stem_kind_file_and_locale(
        string path, string stem, HdAssetKind kind, HdAssetFile file, string? locale)
    {
        Assert.True(HdTexturePath.TryParse(path, out var name));
        Assert.Equal(stem, name.Stem);
        Assert.Equal(kind, name.Kind);
        Assert.Equal(file, name.File);
        Assert.Equal(locale, name.Locale);
    }

    [Theory]
    [InlineData("2000.mdp")]
    [InlineData("TEXT1.BIN")]
    [InlineData("2000_unknown__atlas.png")]
    [InlineData("__atlas.png")]
    [InlineData("")]
    public void TryParse_rejects_non_softhd(string path)
    {
        Assert.False(HdTexturePath.TryParse(path, out _));
        Assert.False(HdTexturePath.IsHdAssetPath(path));
    }

    [Fact]
    public void ParseSpriteInfo_reads_xywh_and_ignores_tail()
    {
        var blob = new byte[9 + 16 + 4];
        blob[0] = (byte)'S';
        blob[1] = (byte)'P';
        blob[2] = (byte)'R';
        blob[3] = (byte)'I';
        blob[4] = (byte)'V';
        BitConverter.TryWriteBytes(blob.AsSpan(5), (ushort)2);
        BitConverter.TryWriteBytes(blob.AsSpan(7), (ushort)2);
        BitConverter.TryWriteBytes(blob.AsSpan(9), (ushort)2);
        BitConverter.TryWriteBytes(blob.AsSpan(11), (ushort)4);
        BitConverter.TryWriteBytes(blob.AsSpan(13), (ushort)128);
        BitConverter.TryWriteBytes(blob.AsSpan(15), (ushort)128);
        BitConverter.TryWriteBytes(blob.AsSpan(17), (ushort)130);
        BitConverter.TryWriteBytes(blob.AsSpan(19), (ushort)4);
        BitConverter.TryWriteBytes(blob.AsSpan(21), (ushort)64);
        BitConverter.TryWriteBytes(blob.AsSpan(23), (ushort)32);
        blob[25] = (byte)'E';
        blob[26] = (byte)'N';
        blob[27] = (byte)'D';

        var rects = HdTexturePath.ParseSpriteInfo(blob);
        Assert.Equal(2, rects.Count);
        Assert.Equal(new HdSpriteRect(2, 4, 128, 128), rects[0]);
        Assert.Equal(new HdSpriteRect(130, 4, 64, 32), rects[1]);
        Assert.Empty(HdTexturePath.ParseSpriteInfo([]));
        Assert.Empty(HdTexturePath.ParseSpriteInfo("SPRI"u8.ToArray()));
    }

    [Fact]
    public void ParseSpriteInfo_v4_keeps_8_byte_records_when_footer_is_long()
    {
        var blob = new byte[9 + 16 + 224];
        blob[0] = (byte)'S';
        blob[1] = (byte)'P';
        blob[2] = (byte)'R';
        blob[3] = (byte)'I';
        blob[4] = (byte)'V';
        blob[5] = 4;
        BitConverter.TryWriteBytes(blob.AsSpan(7), (ushort)2);
        BitConverter.TryWriteBytes(blob.AsSpan(9), (ushort)522);
        BitConverter.TryWriteBytes(blob.AsSpan(11), (ushort)2);
        BitConverter.TryWriteBytes(blob.AsSpan(13), (ushort)64);
        BitConverter.TryWriteBytes(blob.AsSpan(15), (ushort)128);
        BitConverter.TryWriteBytes(blob.AsSpan(17), (ushort)402);
        BitConverter.TryWriteBytes(blob.AsSpan(19), (ushort)662);
        BitConverter.TryWriteBytes(blob.AsSpan(21), (ushort)96);
        BitConverter.TryWriteBytes(blob.AsSpan(23), (ushort)96);
        blob[25] = 0xFF;
        blob[26] = 0xFF;

        var rects = HdTexturePath.ParseSpriteInfo(blob);
        Assert.Equal(2, rects.Count);
        Assert.Equal(new HdSpriteRect(522, 2, 64, 128), rects[0]);
        Assert.Equal(new HdSpriteRect(402, 662, 96, 96), rects[1]);
    }

    [Fact]
    public void ParseSpriteInfo_v1_anim_uses_8_byte_header()
    {
        // SoftHD map anim: SPRIV + u8 ver + u16 count + xywh at +8.
        var blob = new byte[8 + 16 + 8];
        blob[0] = (byte)'S';
        blob[1] = (byte)'P';
        blob[2] = (byte)'R';
        blob[3] = (byte)'I';
        blob[4] = (byte)'V';
        blob[5] = 1;
        BitConverter.TryWriteBytes(blob.AsSpan(6), (ushort)2);
        BitConverter.TryWriteBytes(blob.AsSpan(8), (ushort)2);
        BitConverter.TryWriteBytes(blob.AsSpan(10), (ushort)878);
        BitConverter.TryWriteBytes(blob.AsSpan(12), (ushort)64);
        BitConverter.TryWriteBytes(blob.AsSpan(14), (ushort)44);
        BitConverter.TryWriteBytes(blob.AsSpan(16), (ushort)526);
        BitConverter.TryWriteBytes(blob.AsSpan(18), (ushort)332);
        BitConverter.TryWriteBytes(blob.AsSpan(20), (ushort)48);
        BitConverter.TryWriteBytes(blob.AsSpan(22), (ushort)56);
        blob[24] = 0xFF;
        blob[25] = 0xFF;

        var rects = HdTexturePath.ParseSpriteInfo(blob);
        Assert.Equal(2, rects.Count);
        Assert.Equal(new HdSpriteRect(2, 878, 64, 44), rects[0]);
        Assert.Equal(new HdSpriteRect(526, 332, 48, 56), rects[1]);
    }

    [Fact]
    public void SpriteMatch_reads_xywh_and_path()
    {
        var rec = new byte[8];
        BitConverter.TryWriteBytes(rec.AsSpan(0), (ushort)2);
        BitConverter.TryWriteBytes(rec.AsSpan(2), (ushort)4);
        BitConverter.TryWriteBytes(rec.AsSpan(4), (ushort)64);
        BitConverter.TryWriteBytes(rec.AsSpan(6), (ushort)112);
        var ev = new HdSpriteMatchEvent(@"FIELD\2000_tenants__spriteinfo.bin", 3, rec);
        Assert.Equal("2000", ev.Stem);
        Assert.Equal(HdAssetKind.Tenants, ev.Kind);
        Assert.Equal(HdAssetFile.SpriteInfo, ev.File);
        Assert.Equal(3, ev.Index);
        Assert.Equal(new HdSpriteRect(2, 4, 64, 112), ev.Rect);
        Assert.Equal(rec, ev.Record);
    }

    [Fact]
    public void SpriteMatch_v4_skips_type_byte_when_xywh_follows()
    {
        var rec = new byte[32];
        rec[0] = 0xFF;
        rec[1] = 0xFF;
        BitConverter.TryWriteBytes(rec.AsSpan(2), (ushort)8);
        BitConverter.TryWriteBytes(rec.AsSpan(4), (ushort)16);
        BitConverter.TryWriteBytes(rec.AsSpan(6), (ushort)32);
        BitConverter.TryWriteBytes(rec.AsSpan(8), (ushort)48);
        var ev = new HdSpriteMatchEvent("fc01_faces__spriteinfo.bin", 0, rec);
        Assert.Equal(HdAssetKind.Faces, ev.Kind);
        Assert.Equal(new HdSpriteRect(8, 16, 32, 48), ev.Rect);
    }

    [Fact]
    public void SpriteDraw_reads_path_index_rect_and_live()
    {
        var rec = new byte[32];
        rec[0] = 0xFF;
        rec[1] = 0xFF;
        BitConverter.TryWriteBytes(rec.AsSpan(2), (ushort)8);
        BitConverter.TryWriteBytes(rec.AsSpan(4), (ushort)16);
        BitConverter.TryWriteBytes(rec.AsSpan(6), (ushort)32);
        BitConverter.TryWriteBytes(rec.AsSpan(8), (ushort)48);
        var live = new HdSpriteRect(120, 40, 16, 24);
        var ev = new HdSpriteDrawEvent(@"FIELD\BA38_tenants__spriteinfo.bin", 7, rec, live);
        Assert.Equal("BA38", ev.Stem);
        Assert.Equal(HdAssetKind.Tenants, ev.Kind);
        Assert.Equal(HdAssetFile.SpriteInfo, ev.File);
        Assert.Equal(7, ev.Index);
        Assert.Equal(new HdSpriteRect(8, 16, 32, 48), ev.Rect);
        Assert.Equal(live, ev.Live);
        Assert.Equal(rec, ev.Record);
    }

    [Fact]
    public void Event_replace_is_dirty_and_pixels_stay_unread_without_decoder()
    {
        var spriv = new byte[9 + 8];
        spriv[0] = (byte)'S';
        spriv[1] = (byte)'P';
        spriv[2] = (byte)'R';
        spriv[3] = (byte)'I';
        spriv[4] = (byte)'V';
        BitConverter.TryWriteBytes(spriv.AsSpan(7), (ushort)1);
        BitConverter.TryWriteBytes(spriv.AsSpan(9), (ushort)8);
        BitConverter.TryWriteBytes(spriv.AsSpan(11), (ushort)16);
        BitConverter.TryWriteBytes(spriv.AsSpan(13), (ushort)32);
        BitConverter.TryWriteBytes(spriv.AsSpan(15), (ushort)48);

        var ev = new HdTextureEvent(@"FIELD\2000_anim__spriteinfo.bin", spriv);
        Assert.Equal("2000", ev.Stem);
        Assert.Equal(HdAssetKind.Anim, ev.Kind);
        Assert.Equal(HdAssetFile.SpriteInfo, ev.File);
        Assert.Equal(spriv, ev.Bytes);
        Assert.Equal(new HdSpriteRect(8, 16, 32, 48), Assert.Single(ev.Rects));
        Assert.Null(ev.Pixels);
        Assert.False(ev.TryGetReplacement(out _));

        ev.Replace([1, 2, 3]);
        Assert.True(ev.TryGetReplacement(out var dest));
        Assert.Equal(new byte[] { 1, 2, 3 }, dest);
    }

    [Fact]
    public void Event_atlas_replace_pixels_uses_encoder_only()
    {
        var decoded = false;
        var encoded = false;
        var ev = new HdTextureEvent(
            "2000_maps__atlas.png",
            [0x89, 0x50, 0x4E, 0x47],
            decode: _ =>
            {
                decoded = true;
                return new HdPixels(1, 1, [1, 2, 3, 4]);
            },
            encode: (w, h, rgba) =>
            {
                encoded = true;
                Assert.Equal(2, w);
                Assert.Equal(1, h);
                return [9, 8, 7];
            });

        Assert.Equal(HdAssetFile.Atlas, ev.File);
        Assert.False(decoded);
        var px = new HdPixels(2, 1, [5, 6, 7, 8, 9, 10, 11, 12]);
        Assert.Equal(2, px.Width);
        Assert.Equal(1, px.Height);
        Assert.Equal(8, px.Rgba.Length);
        ev.ReplacePixels(px.Width, px.Height, px.Rgba);
        Assert.False(decoded);
        Assert.True(encoded);
        Assert.True(ev.TryGetReplacement(out var dest));
        Assert.Equal(new byte[] { 9, 8, 7 }, dest);
    }
}
