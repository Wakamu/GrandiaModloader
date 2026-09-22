using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class CameraPathAsmTests
{
    private static readonly string[] FieldDirs =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD",
        @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content\FIELD",
    ];

    [Fact]
    public void Format_set_pos_and_wait_b()
    {
        var ops = new[]
        {
            new MapCameraPathOp(0, 1, MapCameraPathOpKind.SetPos,
            [
                MapCameraPathValue.FromUnits(-106),
                MapCameraPathValue.FromUnits(64),
                MapCameraPathValue.FromUnits(133),
            ]),
            new MapCameraPathOp(13, 0x13, MapCameraPathOpKind.WaitB, duration: 214),
            new MapCameraPathOp(16, 0xFF, MapCameraPathOpKind.End),
        };
        var text = CameraPathAsm.Format(ops, id: 1);
        Assert.Contains("camera_path 1", text);
        Assert.Contains("set_pos -106 64 133", text);
        Assert.Contains("wait_b 214", text);
        Assert.Contains("end", text);
        Assert.DoesNotContain("@", text);
    }

    [Fact]
    public void Parse_accepts_offsets_envelope_and_comments()
    {
        var ops = CameraPathAsm.Parse("""
            # BA38 hook 27
            camera_path 1 {
              @0 set_pos -106 64 133
              @13 set_rot 22.5 45 0
              wait 180
              yield_tween
              wait_b 0xD6
              end
            }
            """);
        Assert.Equal(6, ops.Count);
        Assert.Equal(MapCameraPathOpKind.SetPos, ops[0].Kind);
        Assert.Equal(-106, ops[0].Values[0].Units, 3);
        Assert.Equal(MapCameraPathOpKind.SetRot, ops[1].Kind);
        Assert.Equal(22.5, ops[1].Values[0].Units, 3);
        Assert.Equal(180, ops[2].Duration);
        Assert.Equal(MapCameraPathOpKind.YieldTween, ops[3].Kind);
        Assert.Equal(214, ops[4].Duration);
        Assert.Equal(MapCameraPathOpKind.End, ops[5].Kind);
    }

    [Fact]
    public void Parse_sentinels_and_save_cam()
    {
        var ops = CameraPathAsm.Parse("""
            set_pos inherit player snapshot
            ch_mode1 pos_x inherit 10 dur=30 foot=0000
            save_cam pos,rot
            end
            """);
        Assert.True(ops[0].Values[0].IsInherit);
        Assert.True(ops[0].Values[1].IsPlayer);
        Assert.True(ops[0].Values[2].IsSnapshot);
        Assert.Equal(MapCameraPathOpKind.Channel, ops[1].Kind);
        Assert.Equal(MapCameraPathChannel.PosX, ops[1].Channel);
        Assert.Equal(1, ops[1].TweenMode);
        Assert.Equal(30, ops[1].Duration);
        Assert.Equal(5, ops[2].SaveFlags);
        Assert.True(ops[2].SavesPos);
        Assert.True(ops[2].SavesRot);
    }

    [Fact]
    public void Replace_marks_dirty_and_roundtrips()
    {
        var map = new Map("BA38");
        var row = map.AddCameraPath("""
            set_pos 0 80 0
            set_rot 0 45 0
            wait 30
            end
            """);
        Assert.Equal(1, row.Id);
        Assert.True(map.Dirty);
        Assert.Contains("set_pos 0 80 0", row.Lines);
        Assert.StartsWith("camera_path 1", row.ToAsm());

        row.Replace("""
            set_pos -10 20 30
            wait_b 10
            end
            """);
        Assert.Equal(-10, row.Ops[0].Values[0].Units, 3);
        Assert.Equal(10, row.Ops[1].Duration);

        row.AddLine("wait 5");
        Assert.Equal(MapCameraPathOpKind.Wait, row.Ops[^2].Kind);
        Assert.Equal(5, row.Ops[^2].Duration);
        Assert.Equal(MapCameraPathOpKind.End, row.Ops[^1].Kind);
    }

    [Fact]
    public void Format_parse_roundtrips_BA38_path_1()
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

        var path = MdpSec15.Parse(blob).OfId(1)!;
        var text = path.ToAsm();
        Assert.Contains("camera_path 1", text);
        Assert.Contains("set_pos -106 64 133", text);
        Assert.Contains("set_rot 22.5 45 0", text);
        Assert.Contains("wait_b 214", text);
        Assert.Contains("yield_tween", text);

        var again = CameraPathAsm.Parse(text);
        Assert.Equal(path.Ops.Count, again.Count);
        Assert.Equal(path.Ops.Select(o => o.Kind), again.Select(o => o.Kind));
        var waitB = again.Single(o => o.Kind == MapCameraPathOpKind.WaitB && o.Duration == 214);
        var yieldAt = again.FindLastIndex(o =>
            o.Kind == MapCameraPathOpKind.YieldTween);
        Assert.True(yieldAt >= 0);
        Assert.Equal(MapCameraPathOpKind.Wait, again[yieldAt - 1].Kind);

        path.Replace(text);
        Assert.Equal(path.Ops.Select(o => o.Kind), again.Select(o => o.Kind));
        Assert.Equal(-106, path.Ops.First(o => o.Kind == MapCameraPathOpKind.SetPos).Values[0].Units, 3);
    }

    private static byte[]? TryLoadSec15(string field, string stem)
    {
        foreach (var name in new[] { $"{stem}.mdp", $"{stem.ToLowerInvariant()}.mdp" })
        {
            var path = Path.Combine(field, name);
            if (File.Exists(path))
            {
                return MdpTim.TrySlice(File.ReadAllBytes(path), 15);
            }
        }

        return null;
    }
}
