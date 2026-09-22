using System.Buffers.Binary;
using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapCameraPathTests
{
    private static readonly string[] FieldDirs =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD",
        @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content\FIELD",
    ];

    [Fact]
    public void Parse_rejects_empty_and_bad_directory()
    {
        Assert.Empty(MdpSec15.Parse([]).Items);
        Assert.Empty(MdpSec15.Parse(new byte[2]).Items);
        Assert.Empty(MdpSec15.Parse(new byte[] { 0, 0, 0, 0 }).Items);
        Assert.Empty(MdpSec15.Parse(new byte[] { 3, 0, 0, 0 }).Items);
        Assert.Empty(MdpSec15.Parse(new byte[] { 8, 0, 0, 0 }).Items);
    }

    [Fact]
    public void Parse_reads_set_pos_and_end()
    {
        var chunk = new byte[14];
        chunk[0] = 0x01;
        WriteBe16_16(chunk.AsSpan(1), -106);
        WriteBe16_16(chunk.AsSpan(5), 64);
        WriteBe16_16(chunk.AsSpan(9), 133);
        chunk[13] = 0xFF;
        var blob = BuildSec15(chunk);

        var path = Assert.Single(MdpSec15.Parse(blob).Items);
        Assert.Equal(0, path.Index);
        Assert.Equal(1, path.Id);
        Assert.False(path.Empty);
        Assert.True(path.Ok);
        Assert.Equal(2, path.Ops.Count);
        var set = path.Ops[0];
        Assert.Equal(MapCameraPathOpKind.SetPos, set.Kind);
        Assert.Equal(-106, set.Values[0].Units, 3);
        Assert.Equal(64, set.Values[1].Units, 3);
        Assert.Equal(133, set.Values[2].Units, 3);
        Assert.Equal(MapCameraPathOpKind.End, path.Ops[1].Kind);
    }

    [Fact]
    public void Parse_stock_BA38_path_1_is_hook_27()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var blob = TryLoadSec15(field, "BA38");
        if (blob is null)
        {
            return;
        }

        var table = MdpSec15.Parse(blob);
        Assert.Equal(6, table.Items.Count);
        Assert.Equal(Enumerable.Range(1, 6), table.Items.Select(e => e.Id));
        Assert.All(table.Items, p => Assert.True(p.Ok));

        var path = table.OfId(1);
        Assert.NotNull(path);
        Assert.Equal(0, path.Index);
        Assert.Equal(69, path.Ops.Count);
        Assert.Equal(MapCameraPathOpKind.Set63Fa5B, path.Ops[0].Kind);
        Assert.Equal(0, path.Ops[0].Arg);
        Assert.Equal(MapCameraPathOpKind.SetFov, path.Ops[1].Kind);
        Assert.True(path.Ops[1].Values[1].IsInherit);
        var pos = path.Ops[2];
        Assert.Equal(MapCameraPathOpKind.SetPos, pos.Kind);
        Assert.Equal(-106, pos.Values[0].Units, 3);
        Assert.Equal(64, pos.Values[1].Units, 3);
        Assert.Equal(133, pos.Values[2].Units, 3);
        var rot = path.Ops[3];
        Assert.Equal(MapCameraPathOpKind.SetRot, rot.Kind);
        Assert.Equal(22.5, rot.Values[0].Units, 3);
        Assert.Equal(45, rot.Values[1].Units, 3);
        Assert.Equal(MapCameraPathOpKind.End, path.Ops[^1].Kind);
        var waitB = Assert.Single(path.Ops, o =>
            o.Kind == MapCameraPathOpKind.WaitB && o.Duration == 0xD6);
        var yieldAt = path.Ops.ToList().FindLastIndex(o =>
            o.Offset < waitB.Offset && o.Kind == MapCameraPathOpKind.YieldTween);
        Assert.True(yieldAt >= 0);
        Assert.Equal(MapCameraPathOpKind.Wait, path.Ops[yieldAt - 1].Kind);
        Assert.Equal(0x080E, MapCameraPath.BoxLockFlag);

        var map = new Map("BA38");
        map.CameraPaths.Hydrate(table.Items);
        Assert.False(map.Dirty);
        Assert.Equal(1, map.CameraPaths.OfId(1)!.Id);
        Assert.Null(map.CameraPaths.OfId(7));

        var hook = FieldHookAsm.AssembleHook("hook 27 camera_path 1 flags=0x40");
        Assert.Equal(0x0D, hook[1] & 0x3F);
        Assert.Equal(1, hook[5]);
        Assert.Equal(0x40, hook[4]);

        Assert.Equal(blob, MdpSec15.Emit(table));
    }

    [Fact]
    public void Emit_empty_table_is_one_stub()
    {
        Assert.Equal(new byte[] { 4, 0, 0, 0, 0xFF }, MdpSec15.Emit(new MapCameraPathTable()));
    }

    [Fact]
    public void Encode_set_pos_roundtrips()
    {
        var ops = new[]
        {
            new MapCameraPathOp(0, 1, MapCameraPathOpKind.SetPos,
            [
                MapCameraPathValue.FromUnits(-106),
                MapCameraPathValue.FromUnits(64),
                MapCameraPathValue.FromUnits(133),
            ]),
            new MapCameraPathOp(13, 0xFF, MapCameraPathOpKind.End),
        };
        var again = MdpSec15.Parse(MdpSec15.Emit(new MapCameraPathTable(
            [new MapCameraPath(0, 1, ops: ops)]))).Items[0];
        Assert.Equal(2, again.Ops.Count);
        Assert.Equal(MapCameraPathOpKind.SetPos, again.Ops[0].Kind);
        Assert.Equal(-106, again.Ops[0].Values[0].Units, 3);
        Assert.Equal(64, again.Ops[0].Values[1].Units, 3);
        Assert.Equal(133, again.Ops[0].Values[2].Units, 3);
        Assert.Equal(MapCameraPathOpKind.End, again.Ops[1].Kind);
    }

    [Fact]
    public void Mutate_marks_map_dirty_and_keeps_ids_after_remove()
    {
        var map = new Map("BA38");
        map.CameraPaths.Hydrate(MdpSec15.Parse(BuildSec15(
            EncodeSetPos(-1, 2, 3),
            EncodeSetPos(4, 5, 6),
            EncodeSetPos(7, 8, 9))).Items);
        Assert.False(map.Dirty);
        Assert.Equal(3, map.CameraPaths.Items.Count);

        map.CameraPaths.OfId(2)!.Remove();
        Assert.True(map.Dirty);
        Assert.Null(map.CameraPaths.OfId(2));
        Assert.Equal(1, map.CameraPaths.OfId(1)!.Id);
        Assert.Equal(3, map.CameraPaths.OfId(3)!.Id);

        var again = MdpSec15.Parse(MdpSec15.Emit(map.CameraPaths));
        Assert.Equal(3, again.Items.Count);
        Assert.Equal(new[] { 1, 2, 3 }, again.Items.Select(e => e.Id));
        Assert.True(again.Items[1].Empty);
        Assert.Equal(-1, again.OfId(1)!.Ops[0].Values[0].Units, 3);
        Assert.Equal(7, again.OfId(3)!.Ops[0].Values[0].Units, 3);

        var reuse = map.AddCameraPath(EncodeSetPos(10, 11, 12));
        Assert.Equal(2, reuse.Id);
        var filled = MdpSec15.Parse(MdpSec15.Emit(map.CameraPaths));
        Assert.Equal(3, filled.Items.Count);
        Assert.False(filled.OfId(2)!.Empty);
        Assert.Equal(10, filled.OfId(2)!.Ops[0].Values[0].Units, 3);
    }

    [Fact]
    public void Add_appends_next_id_and_SetOps_replaces_bytecode()
    {
        var map = new Map("BA38");
        map.CameraPaths.Hydrate(MdpSec15.Parse(BuildSec15(EncodeSetPos(1, 2, 3))).Items);
        var added = map.AddCameraPath(
        [
            new MapCameraPathOp(0, 1, MapCameraPathOpKind.SetPos,
            [
                MapCameraPathValue.FromUnits(20),
                MapCameraPathValue.FromUnits(30),
                MapCameraPathValue.FromUnits(40),
            ]),
            new MapCameraPathOp(13, 0xFF, MapCameraPathOpKind.End),
        ]);
        Assert.Equal(2, added.Id);
        Assert.True(map.Dirty);

        map.CameraPaths.OfId(1)!.SetOps(
        [
            new MapCameraPathOp(0, 1, MapCameraPathOpKind.SetPos,
            [
                MapCameraPathValue.FromUnits(50),
                MapCameraPathValue.FromUnits(60),
                MapCameraPathValue.FromUnits(70),
            ]),
            new MapCameraPathOp(13, 0xFF, MapCameraPathOpKind.End),
        ]);

        var again = MdpSec15.Parse(MdpSec15.Emit(map.CameraPaths));
        Assert.Equal(2, again.Items.Count);
        Assert.Equal(50, again.OfId(1)!.Ops[0].Values[0].Units, 3);
        Assert.Equal(20, again.OfId(2)!.Ops[0].Values[0].Units, 3);
    }

    [Fact]
    public void Hydrate_keeps_dirty_row_with_same_id()
    {
        var stock = MdpSec15.Parse(BuildSec15(EncodeSetPos(1, 2, 3), EncodeSetPos(4, 5, 6)));
        var map = new Map("BA38");
        map.CameraPaths.Hydrate(stock.Items);
        map.CameraPaths.OfId(1)!.SetOps(
        [
            new MapCameraPathOp(0, 1, MapCameraPathOpKind.SetPos,
            [
                MapCameraPathValue.FromUnits(9),
                MapCameraPathValue.FromUnits(8),
                MapCameraPathValue.FromUnits(7),
            ]),
            new MapCameraPathOp(13, 0xFF, MapCameraPathOpKind.End),
        ]);
        map.CameraPaths.Hydrate(MdpSec15.Parse(BuildSec15(
            EncodeSetPos(1, 2, 3), EncodeSetPos(4, 5, 6))).Items);
        Assert.Equal(1, map.CameraPaths.Items.Count(e => !e.Removed && e.Id == 1));
        Assert.Equal(9, map.CameraPaths.OfId(1)!.Ops[0].Values[0].Units, 3);
        var emit = MdpSec15.Parse(MdpSec15.Emit(map.CameraPaths));
        Assert.Equal(9, emit.OfId(1)!.Ops[0].Values[0].Units, 3);
        Assert.Equal(4, emit.OfId(2)!.Ops[0].Values[0].Units, 3);
    }

    private static byte[] EncodeSetPos(float x, float y, float z) =>
        MdpSec15.EncodeChunk(
        [
            new MapCameraPathOp(0, 1, MapCameraPathOpKind.SetPos,
            [
                MapCameraPathValue.FromUnits(x),
                MapCameraPathValue.FromUnits(y),
                MapCameraPathValue.FromUnits(z),
            ]),
            new MapCameraPathOp(13, 0xFF, MapCameraPathOpKind.End),
        ]);

    private static byte[] BuildSec15(params byte[][] chunks)
    {
        var dir = chunks.Length * 4;
        var cursor = dir;
        var offs = new int[chunks.Length];
        for (var i = 0; i < chunks.Length; i++)
        {
            offs[i] = cursor;
            cursor += chunks[i].Length;
        }

        var blob = new byte[cursor];
        for (var i = 0; i < chunks.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i * 4), (uint)offs[i]);
            chunks[i].CopyTo(blob.AsSpan(offs[i]));
        }

        return blob;
    }

    private static void WriteBe16_16(Span<byte> dest, int units)
    {
        var raw = unchecked((uint)(units * 65536));
        BinaryPrimitives.WriteUInt32BigEndian(dest, raw);
    }

    private static byte[]? TryLoadSec15(string field, string stem)
    {
        foreach (var name in new[] { $"{stem}.mdp", $"{stem.ToLowerInvariant()}.mdp" })
        {
            var path = Path.Combine(field, name);
            if (!File.Exists(path))
            {
                continue;
            }

            return MdpTim.TrySlice(File.ReadAllBytes(path), 15);
        }

        return null;
    }
}
