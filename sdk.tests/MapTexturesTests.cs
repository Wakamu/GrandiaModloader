using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapTexturesTests
{
    private static readonly string[] FieldDirs =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD",
        @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content\FIELD",
    ];

    [Fact]
    public void Hydrate_exposes_sheet_without_dirty()
    {
        var words = new ushort[MdpTim.Width * MdpTim.Height];
        words[256 * MdpTim.Width + 10] = 0x1234;
        var map = new Map("BA38");
        map.Textures.Hydrate(new MapTextureSheet(words, [new MapTextureUpload(1, 0, 256, 64, 64, 128)]));
        Assert.False(map.Dirty);
        Assert.Equal(1, map.Textures.Occupied);
        Assert.Equal(0x1234, map.Textures.Sheet.WordAt(10, 256));
        Assert.Single(map.Textures.Uploads);
    }

    [Fact]
    public void Crop_falls_back_to_field_half_when_page_y_is_empty()
    {
        var words = new ushort[MdpTim.Width * MdpTim.Height];
        // tpage 0x0008 → page X=512, Y=0, 4bpp. Put texels at Y=256 instead.
        const int x = 512;
        const int y = 256;
        words[y * MdpTim.Width + x] = 0xABCD;
        var sheet = new MapTextureSheet(words);
        Assert.True(sheet.TryCrop(0x0008, 0, 0, 4, 1, out var crop));
        Assert.Equal(256, crop.VramY);
        Assert.Equal(0xABCD, crop.Words[0]);
        Assert.False(crop.Empty);
        Assert.Equal(0xD, crop.Indices()[0]);
    }

    [Fact]
    public void Decode_stock_BA38_field_tim()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var mdp = TryLoadMdp(field, "BA38");
        if (mdp is null)
        {
            return;
        }

        var sheet = MdpTim.FromMdp(mdp);
        Assert.True(sheet.Uploads.Count >= 1);
        Assert.Contains(sheet.Uploads, u => u.Section == 1 && u.Y == 256 && u.Width >= 256 && u.Height == 256);
        Assert.True(sheet.Occupied > 1000);

        var map = new Map("BA38");
        map.Textures.Hydrate(sheet);
        var uv = MdpSec32.ParseAll(MdpTim.TrySlice(mdp, 32) ?? []);
        var hit = uv
            .Where(u => u.Block == 1 && u.Width > 8 && u.Height > 8)
            .Select(u => (Uv: u, Ok: map.Textures.TryCrop(u, out var crop), Crop: crop))
            .FirstOrDefault(p => p.Ok && !p.Crop.Empty);
        if (hit.Uv.Width == 0)
        {
            return;
        }

        Assert.False(hit.Crop.Empty);
        Assert.Equal(hit.Uv.Width, hit.Crop.TexelWidth);
        Assert.Equal(hit.Uv.Height, hit.Crop.TexelHeight);
        Assert.False(map.Dirty);
    }

    [Fact]
    public void TryCrop_hd_sprite_uses_source_or_fnv_join()
    {
        var words = new ushort[MdpTim.Width * MdpTim.Height];
        const int tpage = 0x001B;
        const int u = 4;
        const int v = 0;
        const int w = 8;
        const int h = 8;
        Assert.True(HdSpriteKey.TryMapRect(tpage, u, v, w, h, out var x, out var y, out var ww, out _));
        for (var row = 0; row < h; row++)
        {
            for (var col = 0; col < ww; col++)
            {
                words[(y + row) * MdpTim.Width + x + col] = (ushort)(0x1111 + row * 16 + col);
            }
        }

        var sheet = new MapTextureSheet(words);
        var uv = new MapSpriteUv(1, u, v, w, h, tpage);
        Assert.True(sheet.TryCrop(uv, out var expected));
        var key = HdSpriteKey.SignExtend(expected.Hash());

        var packed = new MapSprite(3, HdAssetKind.Tenants, new HdSpriteRect(0, 0, w, h), 1, uv);
        Assert.True(sheet.TryCrop(packed, [], out var fromSrc));
        Assert.Equal(expected.Words, fromSrc.Words);

        var anim = new MapSprite(7, HdAssetKind.Anim, new HdSpriteRect(0, 0, 16, 16), key);
        Assert.True(sheet.TryCrop(anim, [uv], out var fromHash));
        Assert.Equal(expected.VramX, fromHash.VramX);
        Assert.Equal(expected.Hash(), fromHash.Hash());

        var map = new Map("BA38");
        map.Textures.Hydrate(sheet);
        map.Sprites.Hydrate([uv], []);
        Assert.True(map.TryCrop(anim, out var viaMap));
        Assert.False(viaMap.Empty);
        var png = MapTexturePng.Encode(viaMap);
        Assert.True(png.Length > 8);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
    }

    [Fact]
    public void Decode_stock_2000_sec1_rect()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var mdp = TryLoadMdp(field, "2000");
        if (mdp is null)
        {
            return;
        }

        var sheet = MdpTim.FromMdp(mdp);
        Assert.Contains(sheet.Uploads, u => u.Section == 1 && u is { X: 0, Y: 256, Height: 256 });
        Assert.True(sheet.Occupied > 1000);
    }

    [Fact]
    public void Stock_BA38_hd_sprite_joins_tim_and_extracts_png()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var mdp = TryLoadMdp(field, "BA38");
        if (mdp is null)
        {
            return;
        }

        var map = new Map("BA38");
        map.Textures.Hydrate(MdpTim.FromMdp(mdp));
        var uv = MdpSec32.ParseAll(MdpTim.TrySlice(mdp, 32) ?? []);
        var sheets = new List<MapSpriteSheet>();
        foreach (var (kind, token) in new[]
        {
            (HdAssetKind.Anim, "anim"),
            (HdAssetKind.Tenants, "tenants"),
        })
        {
            var path = Path.Combine(field, $"BA38_{token}__spriteinfo.bin");
            if (!File.Exists(path))
            {
                path = Path.Combine(field, $"ba38_{token}__spriteinfo.bin");
            }

            if (File.Exists(path))
            {
                sheets.Add(MapSpriteBank.FromSpriteInfo(kind, File.ReadAllBytes(path)));
            }
        }

        map.Sprites.Hydrate(uv, sheets);
        var tenant = map.Sprites.Tenants.Items.FirstOrDefault(s => s.Source is not null);
        if (tenant is not null)
        {
            Assert.True(map.TryCrop(tenant, out var tim));
            Assert.False(tim.Empty);
        }

        var hits = map.Sprites.Anim.Items.Count(s => map.TryCrop(s, out var crop) && !crop.Empty);
        Assert.True(hits >= 1);

        var dest = Path.Combine(Path.GetTempPath(), "GrandiaExtractTest", "BA38");
        if (Directory.Exists(dest))
        {
            Directory.Delete(dest, true);
        }

        var wrote = MapTextureExtract.Write(map, Path.GetDirectoryName(dest));
        Assert.True(Directory.Exists(Path.Combine(wrote, "Uv")));
        Assert.True(File.Exists(Path.Combine(wrote, "matches.txt")));
    }

    private static byte[]? TryLoadMdp(string field, string stem)
    {
        foreach (var name in new[] { stem + ".mdp", stem + ".MDP", stem.ToUpperInvariant() + ".mdp" })
        {
            var path = Path.Combine(field, name);
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        return null;
    }
}
