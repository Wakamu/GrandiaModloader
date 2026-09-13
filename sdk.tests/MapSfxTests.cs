using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapSfxTests
{
    [Fact]
    public void Parse_reads_header_and_rows_until_ff()
    {
        var blob = BuildSec29(
            flags: 0,
            range: 896,
            Row(21, sfx: 6, flags: 0x60, kind: 0x81, period: 12, bias: 0, x: 849, y: 256, z: -819),
            Row(30, sfx: 16, flags: 0x70, kind: 0x81, period: 6, bias: 3, x: 79, y: 310, z: 1262));

        var table = MdpSec29.Parse(blob);
        Assert.Equal(0, table.Flags);
        Assert.Equal(896, table.Range);
        Assert.Equal(2, table.Items.Count);

        var river = table.Items[0];
        Assert.Equal(21, river.Id);
        Assert.Equal(6, river.Sfx);
        Assert.True(river.Looping);
        Assert.Equal(new WalkPos(849, 256, -819), river.Position);

        var frog = table.Items[1];
        Assert.Equal(16, frog.Sfx);
        Assert.Equal(3, frog.Bias);
        Assert.Equal(79, frog.X);
    }

    [Fact]
    public void Hydrate_exposes_sfx_on_map_without_dirty()
    {
        var parsed = MdpSec29.Parse(BuildSec29(
            2,
            896,
            Row(0, sfx: 3, flags: 0x80, kind: 0x82, x: -1255, y: 70, z: 382)));
        var map = new Map("2410");
        map.Sfx.Hydrate(parsed.Flags, parsed.Range, parsed.Items);
        Assert.False(map.Dirty);
        Assert.Equal(896, map.Sfx.Range);
        Assert.Single(map.Sfx.Items);
        Assert.Equal(3, map.Sfx.Items[0].Sfx);
        Assert.Equal(-1255, map.Sfx[0]!.X);
        Assert.Single(map.Sfx.OfSfx(3));
        Assert.Empty(map.Sfx.OfSfx(16));
    }

    [Fact]
    public void Parse_empty_or_short_is_empty_table()
    {
        Assert.Empty(MdpSec29.Parse([]).Items);
        Assert.Empty(MdpSec29.Parse(new byte[4]).Items);
    }

    [Fact]
    public void Mutate_marks_map_dirty_and_emit_roundtrips()
    {
        var map = new Map("7400");
        map.Sfx.Hydrate(0, 896, MdpSec29.Parse(BuildSec29(
            0,
            896,
            Row(21, sfx: 6, flags: 0x60, kind: 0x81, period: 12, x: 849, y: 256, z: -819),
            Row(30, sfx: 16, flags: 0x70, kind: 0x81, period: 6, bias: 3, x: 79, y: 310, z: 1262))).Items);
        Assert.False(map.Dirty);

        map.Sfx[0]!.Sfx = 9;
        map.Sfx[0]!.Position = new WalkPos(100, 200, -50);
        map.Sfx[1]!.Remove();
        var extra = map.AddSfx(16, 10, 20, 30);
        Assert.True(map.Dirty);
        Assert.True(extra.Append);
        Assert.Equal(16, extra.Sfx);

        var blob = MdpSec29.Emit(map.Sfx);
        var again = MdpSec29.Parse(blob);
        Assert.Equal(896, again.Range);
        Assert.Equal(2, again.Items.Count);
        Assert.Equal(9, again.Items[0].Sfx);
        Assert.Equal(new WalkPos(100, 200, -50), again.Items[0].Position);
        Assert.Equal(16, again.Items[1].Sfx);
        Assert.Equal(10, again.Items[1].X);
    }

    [Fact]
    public void Range_change_is_dirty_and_emit_writes_header()
    {
        var map = new Map("2410");
        map.Sfx.Hydrate(0, 896, []);
        Assert.False(map.Dirty);
        map.Sfx.Range = 512;
        Assert.True(map.Dirty);
        var blob = MdpSec29.Emit(map.Sfx);
        Assert.Equal(512, BitConverter.ToInt32(blob, 4));
        Assert.Equal(0xFF, blob[8]);
    }

    [Fact]
    public void Emit_caps_live_rows_at_heap_budget()
    {
        var table = new MapSfxTable(0, 896, []);
        for (var i = 0; i < MdpSec29.MaxLive + 4; i++)
        {
            table.Add(16, i, 0, 0);
        }

        var blob = MdpSec29.Emit(table);
        Assert.True(blob.Length <= MdpSec29.HeapSize);
        Assert.Equal(MdpSec29.MaxLive, MdpSec29.Parse(blob).Items.Count);
    }

    private static byte[] BuildSec29(int flags, int range, params byte[][] rows)
    {
        var blob = new byte[8 + (rows.Length + 1) * 16];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 4), flags);
        BitConverter.TryWriteBytes(blob.AsSpan(4, 4), range);
        var off = 8;
        foreach (var row in rows)
        {
            row.CopyTo(blob, off);
            off += 16;
        }

        blob[off] = 0xFF;
        return blob;
    }

    private static byte[] Row(int id, int sfx, int flags, int kind, int x, int y, int z,
        int period = 0, int bias = 0)
    {
        var r = new byte[16];
        r[0] = (byte)id;
        r[1] = 0xFF;
        r[2] = (byte)kind;
        r[3] = (byte)sfx;
        r[4] = (byte)flags;
        r[5] = (byte)period;
        r[6] = 0xFF;
        r[7] = (byte)bias;
        BitConverter.TryWriteBytes(r.AsSpan(0xA, 2), (short)x);
        BitConverter.TryWriteBytes(r.AsSpan(0xC, 2), (short)y);
        BitConverter.TryWriteBytes(r.AsSpan(0xE, 2), (short)z);
        return r;
    }
}
