using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapsTests
{
    [Fact]
    public void Named_stems_match_hex_ids()
    {
        Assert.Equal(0x2000, (ushort)Maps.Parm);
        Assert.Equal(0x204C, (ushort)Maps.ParmGeneralStore);
        Assert.Equal(0x2410, (ushort)Maps.MarnaRoad);
        Assert.Equal(0x3C00, (ushort)Maps.NewParm);
        Assert.Equal(0x7400, (ushort)Maps.GumboVillage);
        Assert.Equal(0xBA38, (ushort)Maps.BaalIntro);
        Assert.Equal(0xCC06, (ushort)Maps.JBase);
        Assert.Equal(0xE418, (ushort)Maps.EndingParm);
    }

    [Fact]
    public void MapId_compares_and_converts()
    {
        MapId id = Maps.Parm;
        Assert.Equal(0x2000, id.Value);
        Assert.True(id == Maps.Parm);
        Assert.True(Maps.Parm == id);
        Assert.False(id == Maps.NewParm);
        Assert.True(id.IsKnown);
        Assert.Equal(Maps.Parm, id.Known);
        Assert.Equal("2000", id.ToString());
        Assert.Equal(Maps.Parm, (Maps)id);

        var unknown = new MapId(0x0001);
        Assert.False(unknown.IsKnown);
        Assert.Null(unknown.Known);
    }

    [Fact]
    public void Parse_stem_is_known()
    {
        Assert.Equal(Maps.BaalIntro, MapId.Parse("BA38").Known);
        Assert.Equal(Maps.BaalIntro, MapId.Parse("ba38.mdp").Known);
        Assert.Equal(Maps.GumboVillage, new Map("7400").Id.Known);
    }

    [Fact]
    public void Values_are_unique()
    {
        var values = Enum.GetValues<Maps>().Select(m => (ushort)m).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }
}
