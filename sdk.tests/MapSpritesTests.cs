using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapSpritesTests
{
    private static readonly string[] FieldDirs =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD",
        @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content\FIELD",
    ];

    [Fact]
    public void Token_round_trips_map_kinds()
    {
        Assert.Equal("anim", HdTexturePath.Token(HdAssetKind.Anim));
        Assert.Equal("tenants", HdTexturePath.Token(HdAssetKind.Tenants));
        Assert.Equal("maps", HdTexturePath.Token(HdAssetKind.Maps));
        Assert.Equal("mapeff", HdTexturePath.Token(HdAssetKind.MapEff));
        Assert.True(HdTexturePath.TryParse("BA38_anim__spriteinfo.bin", out var name));
        Assert.Equal(HdAssetKind.Anim, name.Kind);
        Assert.Equal("anim", HdTexturePath.Token(name.Kind));
    }

    [Fact]
    public void UnpackUv_rejects_anim_fnv_and_reads_tenant_pack()
    {
        Assert.False(HdSpriteKey.TryUnpackUv(HdSpriteKey.SignExtend(0xC01CC90D), out _, out _, out _, out _, out _, out _));
        Assert.False(HdSpriteKey.TryUnpackUv(0x5184D568, out _, out _, out _, out _, out _, out _));

        const ulong tenant = 0x3BD80E20EFC0001Cul;
        Assert.True(HdSpriteKey.TryUnpackUv(tenant, out var u, out var v, out var w, out var h, out var tpage, out var clut));
        Assert.Equal(0x1C, u);
        Assert.Equal(0, v);
        Assert.Equal(0xC0, w);
        Assert.Equal(0xEF, h);
        Assert.Equal(0x0E20, tpage);
        Assert.Equal(0x3BD8, clut);
    }

    [Fact]
    public void Hydrate_exposes_sheets_without_dirty()
    {
        var map = new Map("BA38");
        var sheet = new MapSpriteSheet(HdAssetKind.Anim, [
            new MapSprite(0, HdAssetKind.Anim, new HdSpriteRect(1, 2, 3, 4), 0x11),
        ]);
        map.Sprites.Hydrate([new MapSpriteUv(1, 8, 16, 32, 48, 0x1F)], [sheet]);
        Assert.False(map.Dirty);
        var uv = Assert.Single(map.Sprites.Uv);
        Assert.Equal(0x1F, uv.Tpage);
        Assert.Equal(3, map.Sprites.Anim[0]!.Atlas.Width);
        Assert.Empty(map.Sprites.Tenants.Items);
        Assert.Empty(map.Sprites.Maps.Items);
        Assert.Empty(map.Sprites.MapEff.Items);
    }

    [Fact]
    public void Parse_stock_BA38_spriteinfo_and_sec32()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var animPath = Path.Combine(field, "BA38_anim__spriteinfo.bin");
        if (!File.Exists(animPath))
        {
            animPath = Path.Combine(field, "ba38_anim__spriteinfo.bin");
        }

        if (!File.Exists(animPath))
        {
            return;
        }

        var anim = MapSpriteBank.FromSpriteInfo(HdAssetKind.Anim, File.ReadAllBytes(animPath));
        Assert.Equal(81, anim.Count);
        Assert.Equal(new HdSpriteRect(586, 494, 128, 104), anim[0]!.Atlas);
        Assert.Null(anim[0]!.Source);
        Assert.Equal(0xFFFFFFFFC01CC90Dul, anim[0]!.Key);

        var tenPath = Path.Combine(field, "BA38_tenants__spriteinfo.bin");
        if (!File.Exists(tenPath))
        {
            tenPath = Path.Combine(field, "ba38_tenants__spriteinfo.bin");
        }

        if (File.Exists(tenPath))
        {
            var tenants = MapSpriteBank.FromSpriteInfo(HdAssetKind.Tenants, File.ReadAllBytes(tenPath));
            Assert.Equal(143, tenants.Count);
            Assert.NotNull(tenants.Items.FirstOrDefault(s => s.Source is not null));
        }

        var sec32 = TryLoadSec(field, "BA38", 32);
        if (sec32 is null)
        {
            return;
        }

        var (block0, block1) = MdpSec32.Parse(sec32);
        Assert.True(block1.Count > 0);
        Assert.Contains(block1, u => (u.Tpage & 0x1F) is >= 0x1B and <= 0x1F);
        var map = new Map("BA38");
        map.Sprites.Hydrate(MdpSec32.ParseAll(sec32), [anim]);
        Assert.Equal(81, map.Sprites.Anim.Count);
        Assert.Equal(block0.Count + block1.Count, map.Sprites.Uv.Count);
        Assert.False(map.Dirty);
    }

    private static byte[]? TryLoadSec(string field, string stem, int index)
    {
        foreach (var name in new[] { stem + ".mdp", stem + ".MDP", stem.ToUpperInvariant() + ".mdp" })
        {
            var path = Path.Combine(field, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var mdp = File.ReadAllBytes(path);
            if (mdp.Length < 512)
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

        return null;
    }
}
