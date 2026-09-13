using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapNpcTests
{
    [Fact]
    public void Parse_lists_kind0_and_kind4_talkers_only()
    {
        var blob = BuildSec8(
            Row(kind: 0, talk: 21, x: 409, y: 256, z: 966, flags: 0x80, clut: 0x5000,
                box: (371, 994, 457, 939), unknown: 0xAB, pack: 0x0002),
            Row(kind: 2, talk: 9, x: -248, y: 4, z: -936, pack: 0x2200),
            Row(kind: 4, talk: 3, x: 10, y: 20, z: 30, pack: 0x0203),
            Row(kind: 0, talk: 0, x: 1, y: 2, z: 3),
            Row(kind: 5, talk: 1, x: 0, y: 0, z: 0));

        var table = MdpSec8.Parse(blob);
        Assert.Equal(5, table.Instances.Count);
        Assert.Equal(2, table.Items.Count);

        var greeter = table.Items[0];
        Assert.True(greeter.IsTown);
        Assert.Equal(0, greeter.Kind);
        Assert.Equal(21, greeter.TalkId);
        Assert.Equal(new WalkPos(409, 256, 966), greeter.Position);
        Assert.Equal(0x5000, greeter.Clut);
        Assert.Equal(new TalkBox(371, 994, 457, 939), greeter.TalkBox);
        Assert.Equal(2, greeter.Facing);
        Assert.Equal(0, greeter.WalkMode);
        Assert.Equal(0xAB, greeter.ToRaw()[0x20]);

        Assert.Equal(4, table.Items[1].Kind);
        Assert.Equal(3, table.Items[1].TalkId);
        Assert.Equal(3, table.Items[1].Facing);
        Assert.Equal(2, table.Items[1].WalkMode);
        Assert.Empty(table.OfTalk(9));
        Assert.Single(table.OfTalk(21));
    }

    [Fact]
    public void Hydrate_exposes_npcs_on_map_without_dirty()
    {
        var parsed = MdpSec8.Parse(BuildSec8(Row(kind: 0, talk: 21, x: 409, y: 256, z: 966)));
        var map = new Map("2000");
        map.Npcs.Hydrate(parsed.Instances);
        Assert.False(map.Dirty);
        Assert.Single(map.Npcs.Items);
        Assert.Equal(21, map.Npcs[0]!.TalkId);
        Assert.Equal(409, map.Npcs[0]!.X);
    }

    [Fact]
    public void Parse_empty_or_short_is_empty_table()
    {
        Assert.Empty(MdpSec8.Parse([]).Items);
        Assert.Empty(MdpSec8.Parse(new byte[2]).Instances);
    }

    [Fact]
    public void Mutate_marks_map_dirty_and_emit_keeps_wanderers()
    {
        var map = new Map("2000");
        map.Npcs.Hydrate(MdpSec8.Parse(BuildSec8(
            Row(kind: 0, talk: 21, x: 409, y: 256, z: 966, clut: 0x5000, unknown: 0xAB,
                box: (371, 994, 457, 939)),
            Row(kind: 2, talk: 9, x: -248, y: 4, z: -936, pack: 0x2200))).Instances);
        Assert.False(map.Dirty);

        var extra = map.AddNpc(21, 10, 20, 30);
        map.Npcs[0]!.Position = new WalkPos(100, 200, -50);
        map.Npcs[0]!.TalkBox = new TalkBox(80, -70, 120, -30);
        Assert.True(map.Dirty);
        Assert.True(extra.Append);
        Assert.Equal(21, extra.TalkId);
        Assert.Equal(0x5000, extra.Clut);
        Assert.Equal(new TalkBox(-28, 58, 58, 3), extra.TalkBox);

        var blob = MdpSec8.Emit(map.Npcs);
        var again = MdpSec8.Parse(blob);
        Assert.Equal(3, again.Instances.Count);
        Assert.Equal(2, again.Items.Count);
        Assert.Equal(new WalkPos(100, 200, -50), again.Items[0].Position);
        Assert.Equal(0xAB, again.Instances[0].ToRaw()[0x20]);
        Assert.Equal(2, again.Instances[1].Kind);
        Assert.Equal(9, again.Instances[1].TalkId);
        Assert.Equal(0x2200, BitConverter.ToUInt16(again.Instances[1].ToRaw(), 0x10));
        Assert.Equal(new WalkPos(10, 20, 30), again.Items[1].Position);
        Assert.Equal(0x4000 | 3, BitConverter.ToUInt16(blob, 0));
    }

    [Fact]
    public void Remove_drops_talker_and_keeps_kind2()
    {
        var map = new Map("2000");
        map.Npcs.Hydrate(MdpSec8.Parse(BuildSec8(
            Row(kind: 0, talk: 21, x: 409, y: 256, z: 966),
            Row(kind: 2, talk: 9, x: -248, y: 4, z: -936))).Instances);
        map.Npcs[0]!.Remove();
        var blob = MdpSec8.Emit(map.Npcs);
        var again = MdpSec8.Parse(blob);
        Assert.Empty(again.Items);
        Assert.Single(again.Instances);
        Assert.Equal(2, again.Instances[0].Kind);
    }

    [Fact]
    public void Add_without_donor_is_kind0()
    {
        var map = new Map("2000");
        map.Npcs.Hydrate([]);
        var row = map.AddNpc(7, 1, 2, 3);
        Assert.Equal(0, row.Kind);
        Assert.Equal(0x80, row.Flags);
        Assert.Equal(7, row.TalkId);
        var blob = MdpSec8.Emit(map.Npcs);
        Assert.Equal(0x4001, BitConverter.ToUInt16(blob, 0));
        Assert.Equal(7, blob[5]);
    }

    [Fact]
    public void Emit_caps_live_rows_at_heap_budget()
    {
        var table = new MapNpcTable();
        for (var i = 0; i < MdpSec8.MaxLive + 4; i++)
        {
            table.Add(1, i, 0, 0);
        }

        var blob = MdpSec8.Emit(table);
        Assert.True(blob.Length <= MdpSec8.HeapSize);
        Assert.Equal(MdpSec8.MaxLive, MdpSec8.Parse(blob).Instances.Count);
    }

    [Fact]
    public void Walk_sets_kind4_mode_and_box()
    {
        var map = new Map("2000");
        map.Npcs.Hydrate(MdpSec8.Parse(BuildSec8(
            Row(kind: 0, talk: 21, x: 409, y: 256, z: 966, pack: 0x0006))).Instances);
        map.Npcs[0]!.Walk(new TalkBox(676, 820, 766, 514));
        Assert.Equal(4, map.Npcs[0]!.Kind);
        Assert.Equal(2, map.Npcs[0]!.WalkMode);
        Assert.Equal(6, map.Npcs[0]!.Facing);
        Assert.Equal(0x0206, map.Npcs[0]!.RuntimeFlags);
        var raw = MdpSec8.Emit(map.Npcs);
        Assert.Equal(4, raw[3]);
        Assert.Equal(0x0206, BitConverter.ToUInt16(raw, 2 + 0x10));
    }

    [Fact]
    public void Kind2_is_not_a_town_npc()
    {
        Assert.False(MdpSec8.IsTown(2, 9));
        Assert.True(MdpSec8.IsTown(0, 21));
        Assert.True(MdpSec8.IsTown(4, 3));
        Assert.False(MdpSec8.IsTown(0, 0));
    }

    private static byte[] BuildSec8(params byte[][] rows)
    {
        var blob = new byte[4 + rows.Length * 48];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 2), (ushort)(0x4000 | rows.Length));
        var off = 2;
        foreach (var row in rows)
        {
            row.CopyTo(blob, off);
            off += 48;
        }

        return blob;
    }

    private static byte[] Row(int kind, int talk, int x, int y, int z,
        int flags = 0x80, int clut = 0, int pack = 0, int unknown = 0,
        (int XMin, int Z0, int XMax, int Z1)? box = null)
    {
        var r = new byte[48];
        r[0] = 0;
        r[1] = (byte)(kind & 0xF);
        r[2] = (byte)flags;
        r[3] = (byte)talk;
        BitConverter.TryWriteBytes(r.AsSpan(4), (short)x);
        BitConverter.TryWriteBytes(r.AsSpan(6), (short)y);
        BitConverter.TryWriteBytes(r.AsSpan(8), (short)z);
        BitConverter.TryWriteBytes(r.AsSpan(0xA), (ushort)clut);
        BitConverter.TryWriteBytes(r.AsSpan(0x10), (ushort)pack);
        if (box is { } b)
        {
            BitConverter.TryWriteBytes(r.AsSpan(0x12), (short)b.XMin);
            BitConverter.TryWriteBytes(r.AsSpan(0x14), (short)b.Z0);
            BitConverter.TryWriteBytes(r.AsSpan(0x16), (short)b.XMax);
            BitConverter.TryWriteBytes(r.AsSpan(0x18), (short)b.Z1);
        }

        r[0x20] = (byte)unknown;
        return r;
    }
}
