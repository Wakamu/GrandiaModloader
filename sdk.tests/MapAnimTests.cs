using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapAnimTests
{
    private static readonly string[] FieldDirs =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD",
        @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content\FIELD",
    ];

    [Fact]
    public void Parse_reads_directory_and_slices_streams()
    {
        var a = new byte[] { 0x11, 0x22, 0x33 };
        var b = new byte[] { 0x44, 0x55 };
        var blob = BuildSec21((1, 0x10, a, b), (2, 0x20, [0x66], []));
        var table = MdpSec21.Parse(blob);
        Assert.Equal(2, table.Items.Count);

        var first = table.Items[0];
        Assert.Equal(1, first.Id);
        Assert.Equal(0x10, first.Flags);
        Assert.Equal(a, first.StreamA);
        Assert.Equal(b, first.StreamB);
        Assert.Equal(2, table.OfId(2)!.Id);
        Assert.Empty(table.OfId(2)!.StreamB);
        Assert.Null(table.OfId(99));
    }

    [Fact]
    public void Frames_and_cues_decode_header_xyz_and_cmds()
    {
        var path = new byte[]
        {
            8, 0,
            10, 0, 54, 1, 73, 3,
            20, 0, 54, 1, 80, 3,
        };
        var cues = new byte[]
        {
            2, 0,
            0, 0, 38, 0x80,
            5, 0, 7, 0xC0,
        };
        var clip = MdpSec21.Parse(BuildSec21((4, 2, path, cues))).Items[0];
        Assert.Equal(8, clip.Header);
        Assert.True(clip.Turn);
        Assert.Equal(2, clip.Frames.Count);
        Assert.Equal(new WalkPos(10, 310, 841), clip.Frames[0]);
        Assert.Equal(new WalkPos(20, 310, 848), clip.Frames[1]);
        Assert.Equal(2, clip.Cues.Count);
        Assert.Equal(new MapAnimCue(0, 2, 38), clip.Cues[0]);
        Assert.Equal(new MapAnimCue(5, 3, 7), clip.Cues[1]);

        var still = MdpSec21.Parse(BuildSec21((1, 1, [0, 0, 1, 0, 2, 0, 3, 0], [0, 0]))).Items[0];
        Assert.False(still.Turn);
        Assert.Empty(still.Cues);
    }

    [Fact]
    public void SetFrames_and_cues_encode_and_add_emits()
    {
        var map = new Map("7400");
        var clip = map.AddAnim(40);
        clip.SetFrames(
            [new WalkPos(10, 310, 841), new WalkPos(20, 310, 848)],
            header: 8);
        clip.SetCues([new MapAnimCue(0, 2, 38), new MapAnimCue(5, 3, 7)]);
        Assert.True(map.Dirty);
        Assert.Equal(8, clip.Header);
        Assert.Equal(2, clip.Flags);
        Assert.Equal(new WalkPos(10, 310, 841), clip.Frames[0]);
        Assert.Equal(new MapAnimCue(5, 3, 7), clip.Cues[1]);

        var again = MdpSec21.Parse(MdpSec21.Emit(map.Anims));
        var got = again.OfId(40)!;
        Assert.Equal(8, got.Header);
        Assert.Equal(2, got.Flags);
        Assert.Equal(clip.Frames, got.Frames);
        Assert.Equal(clip.Cues, got.Cues);
    }

    [Fact]
    public void SetFrames_shrinks_flags_so_emit_does_not_keep_old_count()
    {
        var path = new byte[]
        {
            8, 0,
            1, 0, 2, 0, 3, 0,
            4, 0, 5, 0, 6, 0,
            7, 0, 8, 0, 9, 0,
        };
        var clip = MdpSec21.Parse(BuildSec21((2, 3, path, []))).Items[0];
        clip.SetFrames([new WalkPos(10, 20, 30)], header: 8);
        Assert.Equal(1, clip.Flags);
        Assert.Single(clip.Frames);
        Assert.Equal(2 + 3 * 6, clip.StreamA.Length);
        var again = MdpSec21.Parse(MdpSec21.Emit(new MapAnimTable([clip]))).Items[0];
        Assert.Equal(1, again.Flags);
        Assert.Equal(new WalkPos(10, 20, 30), Assert.Single(again.Frames));
        Assert.Equal(2 + 3 * 6, again.StreamA.Length);
    }

    [Fact]
    public void SetFrames_grow_writes_new_count_and_all_samples()
    {
        var stock = new byte[2 + 22 * 6];
        stock[0] = 8;
        var clip = MdpSec21.Parse(BuildSec21((2, 22, stock, []))).Items[0];
        var frames = Enumerable.Range(0, 29).Select(i => new WalkPos(251 + i, 258, 585 - i)).ToList();
        clip.SetFrames(frames, header: 0x208);
        Assert.Equal(29, clip.Flags);
        Assert.Equal(2 + 29 * 6, clip.StreamA.Length);
        Assert.Equal(29, clip.Frames.Count);
        Assert.Equal(new WalkPos(251, 258, 585), clip.Frames[0]);
        Assert.Equal(new WalkPos(251 + 28, 258, 585 - 28), clip.Frames[28]);
        var blob = MdpSec21.Emit(new MapAnimTable([clip]));
        var again = MdpSec21.Parse(blob).Items[0];
        Assert.Equal(29, again.Flags);
        Assert.Equal(2 + 29 * 6, again.StreamA.Length);
        Assert.Empty(again.Cues);
        Assert.NotEqual(0, BitConverter.ToInt32(blob, MdpSec21.HeaderSize + 8));
    }

    [Fact]
    public void Hydrate_replaces_dirty_row_with_same_id()
    {
        var map = new Map("7400");
        map.Anims.Hydrate(MdpSec21.Parse(BuildSec21((2, 22, new byte[2 + 22 * 6], []))).Items);
        map.Anims.OfId(2)!.SetFrames([new WalkPos(251, 258, 585), new WalkPos(405, 258, 434)]);
        map.Anims.Hydrate(MdpSec21.Parse(BuildSec21((2, 22, new byte[2 + 22 * 6], []))).Items);
        Assert.Equal(1, map.Anims.Items.Count(c => !c.Removed && c.Id == 2));
        Assert.Equal(2, map.Anims.OfId(2)!.Flags);
        Assert.Equal(new WalkPos(251, 258, 585), map.Anims.OfId(2)!.Frames[0]);
        var emit = MdpSec21.Parse(MdpSec21.Emit(map.Anims));
        Assert.Equal(1, emit.Items.Count(c => c.Id == 2));
        Assert.Equal(2, emit.OfId(2)!.Flags);
    }

    [Fact]
    public void Hydrate_exposes_anims_on_map_without_dirty()
    {
        var parsed = MdpSec21.Parse(BuildSec21((7, 0, [1, 2], [3])));
        var map = new Map("7400");
        map.Anims.Hydrate(parsed.Items);
        Assert.False(map.Dirty);
        Assert.Single(map.Anims.Items);
        Assert.Equal(7, map.Anims[0]!.Id);
        Assert.Equal(7, map.Anims.OfId(7)!.Id);
        Assert.Equal(new byte[] { 1, 2 }, map.Anims[0]!.StreamA);
    }

    [Fact]
    public void Parse_empty_or_short_is_empty_table()
    {
        Assert.Empty(MdpSec21.Parse([]).Items);
        Assert.Empty(MdpSec21.Parse(new byte[2]).Items);
        Assert.Empty(MdpSec21.Parse(new byte[] { 0, 0, 0, 0 }).Items);
    }

    [Fact]
    public void Mutate_marks_map_dirty_and_emit_roundtrips()
    {
        var map = new Map("7400");
        map.Anims.Hydrate(MdpSec21.Parse(BuildSec21(
            (1, 0x10, [0xAA], [0xBB, 0xCC]),
            (2, 0x20, [0xDD], []))).Items);
        Assert.False(map.Dirty);

        map.Anims[0]!.Flags = 0x11;
        map.Anims[1]!.Remove();
        Assert.True(map.Dirty);

        var blob = MdpSec21.Emit(map.Anims);
        var again = MdpSec21.Parse(blob);
        Assert.Single(again.Items);
        Assert.Equal(1, again.Items[0].Id);
        Assert.Equal(0x11, again.Items[0].Flags);
        Assert.Equal(new byte[] { 0xAA }, again.Items[0].StreamA);
        Assert.Equal(new byte[] { 0xBB, 0xCC }, again.Items[0].StreamB);
    }

    [Fact]
    public void Parse_stock_7400_and_2000_directory_ids()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var m7400 = TryLoadSec21(field, "7400");
        var m2000 = TryLoadSec21(field, "2000");
        if (m7400 is null || m2000 is null)
        {
            return;
        }

        var t7400 = MdpSec21.Parse(m7400);
        Assert.Equal(11, t7400.Items.Count);
        Assert.Equal(Enumerable.Range(1, 11), t7400.Items.Select(e => e.Id));
        Assert.NotNull(t7400.OfId(2));
        Assert.Null(t7400.OfId(12));
        Assert.True(t7400.Items.All(e => e.StreamA.Length > 0 || e.StreamB.Length > 0));
        Assert.Equal(t7400.Items.Select(e => e.Flags), t7400.Items.Select(e => e.Frames.Count));
        var c4 = t7400.OfId(4)!;
        Assert.True(c4.Turn);
        Assert.Equal(new MapAnimCue(0, 2, 38), Assert.Single(c4.Cues));

        var t2000 = MdpSec21.Parse(m2000);
        Assert.Equal(32, t2000.Items.Count);
        Assert.Equal(Enumerable.Range(1, 32), t2000.Items.Select(e => e.Id));
        var p3 = t2000.OfId(3)!;
        Assert.False(p3.Turn);
        Assert.Equal(5, p3.Cues.Count);
        Assert.All(p3.Cues, c => Assert.Equal(3, c.Table));
        Assert.All(p3.Cues, c => Assert.Equal(7, c.Param));

        var map = new Map("7400");
        map.Anims.Hydrate(t7400.Items);
        Assert.False(map.Dirty);
        Assert.Equal(2, map.Anims.OfId(2)!.Id);

        var hook = FieldHookAsm.AssembleHook($"hook 200 anim {map.Anims.OfId(2)!.Id} talk=2 mode=2");
        Assert.Equal(2, hook[5]);
        Assert.Equal(2, hook[6]);
        Assert.Equal(2, (hook[4] >> 4) & 3);

        var again = MdpSec21.Parse(MdpSec21.Emit(t7400));
        Assert.Equal(t7400.Items.Select(e => e.Id), again.Items.Select(e => e.Id));
        Assert.Equal(t7400.Items.Select(e => e.Flags), again.Items.Select(e => e.Flags));
        Assert.Equal(t7400.Items.Select(e => e.StreamA.Length), again.Items.Select(e => e.StreamA.Length));
        Assert.Equal(t7400.Items.Select(e => e.Cues), again.Items.Select(e => e.Cues));
    }

    private static byte[] BuildSec21(params (int Id, int Flags, byte[] A, byte[] B)[] rows)
    {
        var cursor = MdpSec21.HeaderSize + rows.Length * MdpSec21.RowSize;
        var recs = new List<(int Id, int Flags, int OffA, int OffB)>();
        var payload = new List<byte>();
        foreach (var row in rows)
        {
            var offA = 0;
            var offB = 0;
            if (row.A.Length > 0)
            {
                offA = cursor;
                payload.AddRange(row.A);
                cursor += row.A.Length;
            }

            if (row.B.Length > 0)
            {
                offB = cursor;
                payload.AddRange(row.B);
                cursor += row.B.Length;
            }

            recs.Add((row.Id, row.Flags, offA, offB));
        }

        var blob = new byte[cursor];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 2), (ushort)rows.Length);
        var off = MdpSec21.HeaderSize;
        foreach (var rec in recs)
        {
            BitConverter.TryWriteBytes(blob.AsSpan(off, 2), (ushort)rec.Id);
            BitConverter.TryWriteBytes(blob.AsSpan(off + 2, 2), (ushort)rec.Flags);
            BitConverter.TryWriteBytes(blob.AsSpan(off + 4, 4), rec.OffA);
            BitConverter.TryWriteBytes(blob.AsSpan(off + 8, 4), rec.OffB);
            off += MdpSec21.RowSize;
        }

        payload.CopyTo(blob.AsSpan(off));
        return blob;
    }

    private static byte[]? TryLoadSec21(string field, string stem)
    {
        foreach (var name in new[] { stem + ".mdp", stem + ".MDP", stem.ToUpperInvariant() + ".mdp" })
        {
            var path = Path.Combine(field, name);
            if (!File.Exists(path))
            {
                continue;
            }

            return TrySlice(File.ReadAllBytes(path), 21);
        }

        return null;
    }

    private static byte[]? TrySlice(byte[] mdp, int index)
    {
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
}
