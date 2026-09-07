using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class ItemTextBinTests
{
    [Fact]
    public void Rebuild_preserves_renames()
    {
        var src = BuildToy();
        var tables = ItemTextBin.Parse(src);
        Assert.Equal("Rusty Knife", tables.Names[63]);
        Assert.Equal("RUSTY", tables.ShortNames[63]);

        tables.Names[63] = "Lump of Coal";
        tables.ShortNames[63] = "LUMP COAL";
        tables.Descriptions[63] = "+14 attack";
        tables.Names[0] = "Life Jewel";

        var rebuilt = ItemTextBin.Rebuild(src, tables);
        var again = ItemTextBin.Parse(rebuilt);
        Assert.Equal("Lump of Coal", again.Names[63]);
        Assert.Equal("LUMP COAL", again.ShortNames[63]);
        Assert.Equal("+14 attack", again.Descriptions[63]);
        Assert.Equal("Life Jewel", again.Names[0]);
        Assert.Equal("Herbs", again.Names[345]);
    }

    [Fact]
    public void Parse_vanilla_text1_roundtrips_known_names()
    {
        const string path =
            @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\TEXT\EN\TEXT1.BIN";
        if (!File.Exists(path))
        {
            return;
        }

        var src = File.ReadAllBytes(path);
        var tables = ItemTextBin.Parse(src);
        Assert.Equal("Rusty Knife", tables.Names[63]);
        Assert.Equal("Herbs", tables.Names[345]);

        var rebuilt = ItemTextBin.Rebuild(src, tables);
        var again = ItemTextBin.Parse(rebuilt);
        Assert.Equal(tables.Names[63], again.Names[63]);
        Assert.Equal(tables.ShortNames[63], again.ShortNames[63]);
        Assert.Equal(tables.Descriptions[345], again.Descriptions[345]);
    }

    [Fact]
    public void PatchInPlace_keeps_file_length_and_applies_fitting_names()
    {
        const string path =
            @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\TEXT\EN\TEXT1.BIN";
        if (!File.Exists(path))
        {
            return;
        }

        var src = File.ReadAllBytes(path);
        var tables = ItemTextBin.Parse(src);
        tables.Names[0] = "Life Jewel";
        tables.ShortNames[0] = "LIF JEWL";
        tables.Names[63] = "Lump of Coal";
        tables.ShortNames[63] = "LUMP COAL";
        tables.Descriptions[63] = "A mineral that is rarer than diamond";

        var result = ItemTextBin.PatchInPlace(src, tables);
        Assert.Equal(src.Length, result.Data.Length);
        Assert.True(result.Applied >= 4);

        var header = src.AsSpan(0, ItemTextBin.SectionCount * 4).ToArray();
        Assert.Equal(header, result.Data.AsSpan(0, header.Length).ToArray());

        var again = ItemTextBin.Parse(result.Data);
        Assert.Equal("Life Jewel", again.Names[0]);
        Assert.Equal("Lump of Coal", again.Names[63]);
        Assert.Equal("LUMP COAL", again.ShortNames[63]);
        Assert.Equal("Herbs", again.Names[345]);

        var nameOff = BitConverter.ToInt32(src, ItemTextBin.ItemNameSection * 4);
        Assert.Equal(0, result.Data[nameOff]);
    }

    [Fact]
    public void PatchInPlace_skips_description_that_blows_the_section()
    {
        var src = BuildToy();
        var tables = ItemTextBin.Parse(src);
        tables.Descriptions[0] = new string('X', 400);

        var result = ItemTextBin.PatchInPlace(src, tables);
        Assert.Equal(src.Length, result.Data.Length);
        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("Desc0", ItemTextBin.Parse(result.Data).Descriptions[0]);
    }

    private static byte[] BuildToy()
    {
        var shorts = new string[ItemTextBin.ItemCount];
        var names = new string[ItemTextBin.ItemCount];
        var descs = new string[ItemTextBin.ItemCount];
        for (var i = 0; i < ItemTextBin.ItemCount; i++)
        {
            shorts[i] = "S" + i;
            names[i] = "Name" + i;
            descs[i] = "Desc" + i;
        }

        shorts[63] = "RUSTY";
        names[63] = "Rusty Knife";
        descs[63] = "A little rusty";
        names[345] = "Herbs";

        var empty = new ItemTextBin.Tables();
        for (var i = 0; i < ItemTextBin.ItemCount; i++)
        {
            empty.ShortNames[i] = shorts[i];
            empty.Names[i] = names[i];
            empty.Descriptions[i] = descs[i];
        }

        var header = new byte[ItemTextBin.SectionCount * 4];
        return ItemTextBin.Rebuild(header, empty);
    }
}
