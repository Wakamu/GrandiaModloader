using Xunit;

namespace Grandia.Sdk.Tests;

public class FieldHookAsmTests
{
    [Theory]
    [InlineData("hook 52 present_channel 1", "3401000000010000000000000000000000000000")]
    [InlineData("hook 1 open_amap node=4", "011a000004040000000000000000000000000000")]
    [InlineData("hook 3 setup dest=0x2000 spawn=2", "0302000040200000020000000000000000000000")]
    [InlineData("hook 25 fanout 51 52", "191c000090000000003334000000000000000000")]
    [InlineData("hook 200 setup dest=0xCC15 spawn=1", "c802000040cc1500010000000000000000000000")]
    [InlineData("hook 3 setup dest=0x2000 spawn=2 if_clear=0x1A", "03020100402000000200000001a0000000000000")]
    [InlineData("hook 3 setup dest=0x2000 spawn=2 if_set=0x1A if_clear=0x1B", "03020d00402000000200000001a01b0000000000")]
    [InlineData("hook 2 sys_latch present_flag_713f83 param=1 value=1", "021e000042010100000000000000000000000000")]
    [InlineData("hook 1 cam_word 0xD000 flags=0x40", "0100000040d00000000000000000000000000000")]
    [InlineData("hook 6 scene_boot 0xFF", "060e000000ff0000000000000000000000000000")]
    [InlineData("hook 14 party_actor walk char=2 p0=1", "0e1d000043020100000000000000000000000000")]
    [InlineData("hook 16 flag_wait 5 0 expect=1 flags=0x40 follow=71 hi=2", "108c000040050000010000000000000000000047")]
    [InlineData("hook 0 attach_vis 4 single=2 flags=0x40 hi=2", "0086000040040200000000000000000000000000")]
    [InlineData("hook 31 attach_anim 1 7 start=10 step=2 interp=5 hold=3 latch=0x40 flags=0x40",
        "1f11000040010740000a00020005000300000000")]
    [InlineData("hook 52 present_channel 1 delay=3 follow=9", "3401000000010000000000000000000003000009")]
    [InlineData("hook 10 anim 7 unit=2 mode=1", "0a10000090070200000000000000000000000000")]
    [InlineData("hook 18 anim 2 talk=41 mode=2", "12100000a0022900000000000000000000000000")]
    [InlineData("hook 206 party_actor instance_facing talk=3 facing=5", "ce1d000048010305000000000000000000000000")]
    [InlineData("hook 206 party_actor instance_facing char=1 talk=3 facing=5", "ce1d000048010305000000000000000000000000")]
    [InlineData("hook 22 party_actor instance_facing talk=2 facing=3 flags=0x08", "161d000008010203000000000000000000000000")]
    [InlineData("hook 206 party_actor instance_facing party=3 facing=5", "ce1d000048000305000000000000000000000000")]
    [InlineData("hook 206 party_actor instance_facing char=0 p0=3 p1=5", "ce1d000048000305000000000000000000000000")]
    [InlineData("hook 54 scripted_battle table=0x61 5x1", "3619000040610000510000000000000000000000")]
    [InlineData("hook 49 scripted_battle table=0x06 1x1 2x1 3x1 word=0x4A trig=0x08",
        "311908004006004a112131000000000000000000")]
    [InlineData("hook 77 scripted_battle table=0x58 1x1 2x1", "4d19000040580000112100000000000000000000")]
    [InlineData("hook 25 hex=191c000090010000003334000000000000000000",
        "191c000090010000003334000000000000000000")]
    public void AssembleHook_matches_python(string line, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(FieldHookAsm.AssembleHook(line)).ToLowerInvariant());
    }

    [Theory]
    [InlineData("zone 0 chest event=0x0A11 item=Herbs kind=5 aabb=-435,16,-1167,-394,-4,-1205 hi=2",
        "00930000000205000011015a00000000000000004dfe100071fb76fefcff4bfb")]
    [InlineData("zone 0 setup dest=0x3404 spawn=2 aabb=-25,295,-317,45,250,-330",
        "0002000040340400020000000000000000000000e7ff2701c3fe2d00fa00b6fe")]
    [InlineData("zone 0 cam_word 0xD000 trig=0x20 aabb=-435,16,-1167,-394,-4,-1205 hi=2",
        "0080200000d000000000000000000000000000004dfe100071fb76fefcff4bfb")]
    public void AssembleZone_matches_python(string line, string hex)
    {
        Assert.Equal(hex, Convert.ToHexString(FieldHookAsm.AssembleZone(line)).ToLowerInvariant());
    }

    [Fact]
    public void FormatHook_names_talk_id_operands()
    {
        var facing = FieldHookAsm.AssembleHook("hook 206 party_actor instance_facing talk=3 facing=5");
        Assert.Equal(1, facing[5]);
        Assert.Contains("talk=3", FieldHookAsm.FormatHook(facing));
        Assert.Contains("facing=5", FieldHookAsm.FormatHook(facing));
        Assert.DoesNotContain("char=", FieldHookAsm.FormatHook(facing));
        Assert.DoesNotContain("p0=", FieldHookAsm.FormatHook(facing));

        var party = FieldHookAsm.AssembleHook("hook 206 party_actor instance_facing party=3 facing=5");
        Assert.Equal(0, party[5]);
        Assert.Contains("party=3", FieldHookAsm.FormatHook(party));
        Assert.DoesNotContain("talk=", FieldHookAsm.FormatHook(party));

        var latch = FieldHookAsm.AssembleHook("hook 18 anim 2 talk=41 mode=2");
        Assert.Contains("talk=41", FieldHookAsm.FormatHook(latch));
        Assert.DoesNotContain("unit=", FieldHookAsm.FormatHook(latch));

        var latchParty = FieldHookAsm.AssembleHook("hook 10 anim 7 unit=2 mode=1");
        Assert.Contains("unit=2", FieldHookAsm.FormatHook(latchParty));
        Assert.DoesNotContain("talk=", FieldHookAsm.FormatHook(latchParty));
    }

    [Fact]
    public void AssembleHook_rewrites_id()
    {
        var raw = FieldHookAsm.AssembleHook("setup dest=0xCC15 spawn=1", 200);
        Assert.Equal(200, raw[0]);
        Assert.Equal(0x02, raw[1] & 0x3F);
        Assert.Equal(0xCC15, (raw[5] << 8) | raw[6]);
    }

    [Fact]
    public void AddHook_and_AddZone_mark_map_dirty()
    {
        var map = new Map("E010");
        map.AddHook("setup dest=0xCC15 spawn=1", 200);
        map.AddZone(0x3404, "setup dest=0x3404 spawn=2 aabb=-25,295,-317,45,250,-330");
        Assert.True(map.Hooks.Dirty);
        Assert.True(map.Zones.Dirty);
        Assert.StartsWith("hook 200", map.Hooks.Get(200)!.Line);
        Assert.StartsWith("zone ", map.Zones.GetByDest(0x3404)!.Line);
    }

    [Theory]
    [InlineData("hook 52 present_channel 1")]
    [InlineData("hook 1 open_amap node=4")]
    [InlineData("hook 3 setup dest=0x2000 spawn=2")]
    [InlineData("hook 25 fanout 51 52")]
    [InlineData("hook 200 setup dest=0xCC15 spawn=1")]
    [InlineData("hook 3 setup dest=0x2000 spawn=2 if_clear=0x1A")]
    [InlineData("hook 2 sys_latch present_flag_713f83 param=1 value=1")]
    [InlineData("hook 14 party_actor walk char=2 p0=1")]
    [InlineData("hook 10 anim 7 unit=2 mode=1")]
    [InlineData("hook 18 anim 2 talk=41 mode=2")]
    [InlineData("hook 206 party_actor instance_facing talk=3 facing=5")]
    [InlineData("hook 22 party_actor instance_facing talk=2 facing=3 flags=0x08")]
    [InlineData("hook 206 party_actor instance_facing party=3 facing=5")]
    [InlineData("hook 54 scripted_battle table=0x61 5x1")]
    [InlineData("hook 49 scripted_battle table=0x06 1x1 2x1 3x1 word=0x4A trig=0x08")]
    [InlineData("hook 77 scripted_battle table=0x58 1x1 2x1")]
    public void FormatHook_roundtrips_bytes(string line)
    {
        var raw = FieldHookAsm.AssembleHook(line);
        var formatted = FieldHookAsm.FormatHook(raw);
        Assert.Equal(raw, FieldHookAsm.AssembleHook(formatted));
    }

    [Theory]
    [InlineData("00802d00009004000000000001901a0000000000b8ff98009004480080008004")]
    [InlineData("00802d00009005000000000001a01b000000000078ff84009005880074008005")]
    [InlineData("00800100006001000000000601c0001800000000e0ffcf0028062000a8000806")]
    public void FormatZone_2406_script_cam_word_roundtrips(string hex)
    {
        var raw = Convert.FromHexString(hex);
        var formatted = FieldHookAsm.FormatZone(raw);
        Assert.DoesNotContain("hex=", formatted);
        Assert.Contains("cam_word 0x", formatted);
        Assert.True(formatted.Contains("if_set=") || formatted.Contains("if_clear="));
        Assert.Equal(hex, Convert.ToHexString(FieldHookAsm.AssembleZone(formatted)).ToLowerInvariant());
        Assert.DoesNotContain("trig=0x2D", formatted);
        if ((raw[2] & 0xF0) != 0)
        {
            Assert.Contains($"trig=0x{raw[2] & 0xF0:X2}", formatted);
        }
    }

    [Fact]
    public void FormatZone_gated_trig_drops_to_always_on_without_flags()
    {
        var stock = Convert.FromHexString(
            "00802d00009004000000000001901a0000000000b8ff98009004480080008004");
        var line = FieldHookAsm.FormatZone(stock)
            .Replace("if_set=0x19", "", StringComparison.Ordinal)
            .Replace("if_clear=0x1A", "", StringComparison.Ordinal);
        var raw = FieldHookAsm.AssembleZone(line);
        Assert.Equal(0x20, raw[2]);
        Assert.Equal(0x9004, (raw[5] << 8) | raw[6]);
    }

    [Theory]
    [InlineData("zone 0 chest event=0x0A11 item=Herbs kind=5 aabb=-435,16,-1167,-394,-4,-1205 hi=2")]
    [InlineData("zone 0 setup dest=0x3404 spawn=2 aabb=-25,295,-317,45,250,-330")]
    [InlineData("zone 0 cam_word 0xD000 trig=0x20 aabb=-435,16,-1167,-394,-4,-1205 hi=2")]
    public void FormatZone_roundtrips_bytes(string line)
    {
        var raw = FieldHookAsm.AssembleZone(line);
        var formatted = FieldHookAsm.FormatZone(raw);
        Assert.Equal(raw, FieldHookAsm.AssembleZone(formatted));
    }

    [Fact]
    public void Hydrate_exposes_vanilla_zones_without_dirty()
    {
        var raw = FieldHookAsm.AssembleZone(
            "zone 0 setup dest=0x2406 spawn=1 aabb=-10,20,-30,10,0,-40");
        var map = new Map("2406");
        map.Zones.Hydrate([Zone.FromRaw(0, raw)]);
        Assert.False(map.Zones.Dirty);
        Assert.Single(map.Zones.Items);
        Assert.Contains("setup dest=0x2406", map.Zones.Items[0].ToString());
        Assert.Equal(0x2406, map.Zones.Items[0].Dest);
    }

    [Fact]
    public void Encounters_lists_scripted_rows_from_all_tables()
    {
        var tables = new MdpSec7 { Flags = 1 };
        tables.Hooks.Add(Convert.FromHexString("311908004006004a112131000000000000000000"));
        tables.AltHooks.Add(Convert.FromHexString("3619000040610000510000000000000000000000"));
        tables.Zones.Add(FieldHookAsm.AssembleZone(
            "zone 0 scripted_battle table=0x61 5x1 aabb=-42,160,1345,42,110,1285 hi=2"));

        var map = new Map("2406");
        map.HydrateEncounters(MapEncounter.FromSec7(tables));
        Assert.Equal(3, map.Encounters.Count);

        Assert.True(map.Encounters[0].IsZone);
        Assert.Equal(0x61, map.Encounters[0].EncounterTable);
        Assert.Equal("", map.Encounters[0].CallLine);
        Assert.Contains("scripted_battle", map.Encounters[0].Line);

        Assert.False(map.Encounters[1].Alt);
        Assert.Equal(49, map.Encounters[1].HookId);
        Assert.Equal(0x06, map.Encounters[1].EncounterTable);
        Assert.Equal(255, map.Encounters[1].EncounterRow);
        Assert.Equal("call_hook 49", map.Encounters[1].CallLine);
        Assert.Equal([new EncounterPair(1, 1), new EncounterPair(2, 1), new EncounterPair(3, 1)],
            map.Encounters[1].Pairs);

        Assert.True(map.Encounters[2].Alt);
        Assert.Equal(54, map.Encounters[2].HookId);
        Assert.Equal("call_hook 54 alt", map.Encounters[2].CallLine);
        Assert.Equal([new EncounterPair(5, 1)], map.Encounters[2].Pairs);
    }

    [Fact]
    public void Encounters_lists_field_wanderers_from_sec30()
    {
        var sec30 = new byte[8 + 56 * 2];
        BitConverter.TryWriteBytes(sec30.AsSpan(4), 2);
        WriteActor(sec30, 0, id: 0x0980, talk: 9, table: 1, x: -248, y: 4, z: -936);
        WriteActor(sec30, 1, id: 0x1380, talk: 23, table: 1, x: 376, y: 0, z: -922);

        var sec8 = new byte[2 + 48 * 2];
        BitConverter.TryWriteBytes(sec8.AsSpan(0), (ushort)(2 | 0x4000));
        WriteKind2(sec8, 0, talk: 9, pack: 0x2200);
        WriteKind2(sec8, 1, talk: 23, pack: 0x1200);

        var field = MapEncounter.FromField(sec8, sec30);
        Assert.Equal(2, field.Count);

        Assert.False(field[0].Scripted);
        Assert.Equal(9, field[0].EncounterRow);
        Assert.Equal(1, field[0].EncounterTable);
        Assert.Equal(-248, field[0].X);
        Assert.Equal([9], field[0].TalkIds);
        Assert.Equal([new EncounterPair(2, 2)], field[0].Pairs);
        Assert.Equal("", field[0].CallLine);
        Assert.Contains("wander row=9", field[0].Line);

        Assert.Equal(0x13, field[1].EncounterRow);
        Assert.Equal([23], field[1].TalkIds);
        Assert.Equal([new EncounterPair(1, 2)], field[1].Pairs);
    }

    [Fact]
    public void Encounters_skips_actors_without_kind2_talk()
    {
        var sec30 = new byte[8 + 56];
        BitConverter.TryWriteBytes(sec30.AsSpan(4), 1);
        WriteActor(sec30, 0, id: 0x0100, talk: 1, table: 1, x: 0, y: 0, z: 0);

        var sec8 = new byte[2 + 48];
        BitConverter.TryWriteBytes(sec8.AsSpan(0), (ushort)(1 | 0x4000));
        var inst = sec8.AsSpan(2, 48);
        inst[1] = 0;
        inst[3] = 1;

        Assert.Empty(MapEncounter.FromField(sec8, sec30));
    }

    [Fact]
    public void DecodeSec8Pairs_matches_7BD80_nibble_order()
    {
        var rec = new byte[48];
        rec[0x10] = 0x00;
        rec[0x11] = 0x22;
        Assert.Equal([new EncounterPair(2, 2)], MapEncounter.DecodeSec8Pairs(rec));

        rec[0x10] = 0x00;
        rec[0x11] = 0x12;
        Assert.Equal([new EncounterPair(1, 2)], MapEncounter.DecodeSec8Pairs(rec));
    }

    private static void WriteActor(byte[] sec30, int index, int id, int talk, int table, short x, short y, short z)
    {
        var at = 8 + index * 56;
        BitConverter.TryWriteBytes(sec30.AsSpan(at), (ushort)id);
        sec30[at + 2] = (byte)talk;
        sec30[at + 6] = (byte)table;
        BitConverter.TryWriteBytes(sec30.AsSpan(at + 0xC), x);
        BitConverter.TryWriteBytes(sec30.AsSpan(at + 0xE), y);
        BitConverter.TryWriteBytes(sec30.AsSpan(at + 0x10), z);
    }

    private static void WriteKind2(byte[] sec8, int index, int talk, int pack)
    {
        var at = 2 + index * 48;
        sec8[at + 1] = 2;
        sec8[at + 3] = (byte)talk;
        BitConverter.TryWriteBytes(sec8.AsSpan(at + 0x10), (ushort)pack);
    }
}

public class MdpSec7Tests
{
    [Fact]
    public void Parse_emit_identity_empty_tables()
    {
        var raw = new byte[0x18];
        raw[0] = 1;
        var again = MdpSec7.Parse(raw).Emit();
        Assert.Equal(raw, again[..0x18]);
    }

    [Fact]
    public void Apply_appends_table2_hook()
    {
        var vanilla = new byte[0x18];
        vanilla[0] = 1;
        var map = new Map("E010");
        map.AddHook("setup dest=0xCC15 spawn=1", 200);
        var sec7 = MdpSec7.Apply(vanilla, map);
        var parsed = MdpSec7.Parse(sec7);
        Assert.Single(parsed.Hooks);
        Assert.Equal(200, parsed.Hooks[0][0]);
        Assert.Equal(0xCC15, (parsed.Hooks[0][5] << 8) | parsed.Hooks[0][6]);
    }

    [Fact]
    public void Apply_replaces_setup_zone_by_dest()
    {
        var dest = 0x3404;
        var oldZone = FieldHookAsm.AssembleZone(
            "zone 0 setup dest=0x3404 spawn=1 aabb=-1,1,-1,1,-1,-1");
        var tables = new MdpSec7 { Flags = 1 };
        tables.Zones.Add(oldZone);
        var vanilla = tables.Emit();

        var map = new Map("3424");
        map.ReplaceZone(dest, "setup dest=0x3404 spawn=2 aabb=-25,295,-317,45,250,-330");
        var sec7 = MdpSec7.Apply(vanilla, map);
        var parsed = MdpSec7.Parse(sec7);
        Assert.Single(parsed.Zones);
        Assert.Equal(2, (parsed.Zones[0][7] << 8) | parsed.Zones[0][8]);
    }

    [Fact]
    public void Apply_replaces_hydrated_zone_by_line()
    {
        const string stock =
            "00802d00009004000000000001901a0000000000b8ff98009004480080008004";
        var raw = Convert.FromHexString(stock);
        var tables = new MdpSec7 { Flags = 1 };
        tables.Zones.Add(raw);
        var vanilla = tables.Emit();

        var map = new Map("2406");
        map.Zones.Hydrate([Zone.FromRaw(0, raw)]);
        var z = map.Zones.Items[0];
        z.Line = z.Line!.Replace("if_set=0x19", "if_set=0x18");
        var sec7 = MdpSec7.Apply(vanilla, map);
        var parsed = MdpSec7.Parse(sec7);
        Assert.Single(parsed.Zones);
        Assert.NotEqual(stock, Convert.ToHexString(parsed.Zones[0]).ToLowerInvariant());
        Assert.Contains("if_set=0x18", FieldHookAsm.FormatZone(parsed.Zones[0]));
    }

    [Fact]
    public void Apply_removes_hydrated_zone()
    {
        var a = FieldHookAsm.AssembleZone(
            "zone 0 cam_word 0x9004 aabb=-1,1,-1,1,-1,-1");
        var b = FieldHookAsm.AssembleZone(
            "zone 0 cam_word 0x9005 aabb=-2,2,-2,2,-2,-2");
        var tables = new MdpSec7 { Flags = 1 };
        tables.Zones.Add(a);
        tables.Zones.Add(b);
        var vanilla = tables.Emit();

        var map = new Map("2406");
        map.Zones.Hydrate([Zone.FromRaw(0, a), Zone.FromRaw(1, b)]);
        map.Zones.Items[0].Remove();
        var parsed = MdpSec7.Parse(MdpSec7.Apply(vanilla, map));
        Assert.Single(parsed.Zones);
        Assert.Equal(0x9005, (parsed.Zones[0][5] << 8) | parsed.Zones[0][6]);
    }
}
