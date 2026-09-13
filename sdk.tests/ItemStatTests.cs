using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class ItemStatTests
{
    [Fact]
    public void WoodenSword_typed_matches_windt_bytes()
    {
        var e = new ItemEvent((int)Item.WoodenSword, 0, 23, 2161)
        {
            Unknown8 = 2,
            Para2 = 1,
            Para1Post = 1801,
            Para4Post = 1281,
        };

        Assert.Equal(WeaponType.Sword, e.WeaponKind);
        Assert.Equal(ItemStat.Strength, e.Stats[0].Kind);
        Assert.Equal(7, e.Stats[0].Value);
        Assert.Equal(1, e.AttackRange);
        Assert.Equal(ItemAuto.None, e.Auto.Kind);
    }

    [Fact]
    public void ShockingKnife_auto_is_paralyze()
    {
        var e = new ItemEvent((int)Item.ShockingKnife, 6500, 22, 34929)
        {
            Unknown8 = 1,
            Para2 = 1,
            Para1Post = 8457,
            Para4Post = 1024,
            Unknown13 = 14,
            Unknown14 = 33,
            Para1Pre = 2,
        };

        Assert.Equal(WeaponType.Dagger, e.WeaponKind);
        Assert.Equal(33, e.Stats[0].Value);
        Assert.Equal(ItemAuto.Paralyze, e.Auto.Kind);
        Assert.Equal(33, e.Auto.Value);
        Assert.Equal(2, e.Auto.Param);
        Assert.Equal(0, e.AttackRange);
    }

    [Fact]
    public void Ruination_signed_vitality_and_combo()
    {
        var e = new ItemEvent((int)Item.RuinationKnife, 20000, 22, 34929)
        {
            Para2 = 2,
            Para3 = 10,
            Para1Post = 55305,
            Para2Post = 1023,
        };

        Assert.Equal(ItemStat.Vitality, e.Stats[0].Kind);
        Assert.Equal(-40, e.Stats[0].Value);
        Assert.Equal(ItemStat.ComboHits, e.Stats[1].Kind);
        Assert.Equal(3, e.Stats[1].Value);
        Assert.Equal(9, e.Para1Post & 0xFF);
    }

    [Fact]
    public void Telescope_attack_range_bonus_is_unsigned()
    {
        var e = new ItemEvent((int)Item.Telescope, 2000, 46, 0);
        e.Stats[0].Kind = ItemStat.AttackRange;
        e.Stats[0].Value = 128;

        Assert.Equal(128, e.Stats[0].Value);
        Assert.Equal(0x8000, e.Para1Post & 0xFF00);
    }

    [Fact]
    public void Typed_writes_preserve_strength_extra_and_drop_anime()
    {
        var e = new ItemEvent((int)Item.WoodenSword, 0, 23, 2161)
        {
            Para1Post = 9,
            Para4Post = 0x0500,
        };

        e.WeaponKind = WeaponType.Sword;
        e.Stats[0].Kind = ItemStat.Strength;
        e.Stats[0].Value = 7;
        e.AttackRange = 1;

        Assert.Equal(2, e.Unknown8);
        Assert.Equal(1, e.Para2);
        Assert.Equal(1801, e.Para1Post);
        Assert.Equal(1281, e.Para4Post);
    }

    [Fact]
    public void ForceKnife_range_10_and_godspeed_wit()
    {
        var force = new ItemEvent((int)Item.ForceKnife, 42000, 21, 0)
        {
            Para4Post = 1034,
        };
        Assert.Equal(10, force.AttackRange);

        var god = new ItemEvent((int)Item.GodspeedKnife, 22500, 21, 0)
        {
            Para2 = 1,
            Para3 = 3,
            Para1Post = 15369,
            Para2Post = 7680,
        };
        Assert.Equal(60, god.Stats[0].Value);
        Assert.Equal(ItemStat.Wit, god.Stats[1].Kind);
        Assert.Equal(30, god.Stats[1].Value);
    }

    [Fact]
    public void Auto_write_roundtrips_raw_bytes()
    {
        var e = new ItemEvent((int)Item.AmuletOfRelief, 7000, 46, 0);
        e.Auto.Kind = ItemAuto.HpRegen;
        e.Auto.Value = 2;

        Assert.Equal(31, e.Unknown13);
        Assert.Equal(2, e.Unknown14);
        Assert.Equal(0, e.Para1Pre);
    }
}
