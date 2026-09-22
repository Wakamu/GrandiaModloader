using System.Buffers.Binary;
using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapCameraTests
{
    private static readonly string[] FieldDirs =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD",
        @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content\FIELD",
    ];

    [Fact]
    public void Decode_select_pan_matches_parm_bytes()
    {
        var pan = MdpSec10.DecodeSelectPan([0x51, 0x2A, 0xD2, 0xD2]);
        Assert.True(pan.Enabled);
        Assert.Equal(-752, pan.XMin);
        Assert.Equal(1312, pan.XMax);
        Assert.Equal(-1312, pan.ZMin);
        Assert.Equal(1376, pan.ZMax);
    }

    [Fact]
    public void Encode_select_pan_off_clears_west_byte()
    {
        var on = MdpSec10.EncodeSelectPan(-752, -1312, 1312, 1376);
        Assert.Equal(new byte[] { 0x51, 0x2A, 0xD2, 0xD2 }, on);

        var off = MdpSec10.EncodeSelectPan(-752, -1312, 1312, 1376, enabled: false);
        Assert.Equal(0, off[0]);
        Assert.Equal(on[1], off[1]);
        Assert.Equal(on[2], off[2]);
        Assert.Equal(on[3], off[3]);
        Assert.False(MdpSec10.DecodeSelectPan(off).Enabled);
    }

    [Fact]
    public void Encode_select_pan_west_off_switch_snaps_while_enabled()
    {
        var raw = MdpSec10.EncodeSelectPan(-2048, -2032, 2032, 2048);
        Assert.Equal(1, raw[0]);
        var decoded = MdpSec10.DecodeSelectPan(raw);
        Assert.True(decoded.Enabled);
        Assert.Equal(-2032, decoded.XMin);
    }

    [Fact]
    public void Load_missing_sec10_is_not_present()
    {
        var cam = new MapCamera();
        MdpSec10.Load(cam, []);
        Assert.False(cam.Present);
        Assert.False(cam.Dirty);
        Assert.Throws<InvalidOperationException>(() => cam.Mode = CameraMode.Interior);
    }

    [Fact]
    public void Emit_writes_mode_pitch_and_pan()
    {
        var blob = NewSec10();
        blob[MdpSec10.ModeOff] = 0;
        var cam = new MapCamera();
        MdpSec10.Load(cam, blob);
        Assert.True(cam.Present);
        Assert.Equal(CameraMode.Town, cam.Mode);
        Assert.Equal(0u, cam.Pitch);
        Assert.True(cam.SelectPan.Enabled);

        cam.Mode = CameraMode.Interior;
        cam.Pitch = 45;
        cam.Follow = 36;
        cam.FollowTerm = 0;
        cam.SelectPan.Distance = 0.12f;
        Assert.True(cam.SelectPan.Enabled);
        Assert.Equal(-752, cam.SelectPan.XMin);
        Assert.Equal(1312, cam.SelectPan.XMax);
        Assert.Equal(-1312, cam.SelectPan.ZMin);
        Assert.Equal(1376, cam.SelectPan.ZMax);
        Assert.Equal(0.12f, cam.SelectPan.Distance);
        Assert.Equal(0x1EB8, cam.SelectPan.P28Raw);
        Assert.True(cam.Dirty);

        var emit = MdpSec10.Emit(cam);
        Assert.Equal(MdpSec10.Size, emit.Length);
        Assert.Equal((byte)CameraMode.Interior, emit[MdpSec10.ModeOff]);
        Assert.Equal(45u, BinaryPrimitives.ReadUInt32LittleEndian(emit.AsSpan(MdpSec10.PitchOff)));
        Assert.Equal(0x10000u, BinaryPrimitives.ReadUInt32LittleEndian(emit.AsSpan(MdpSec10.FollowTermOff)));
        Assert.Equal(new byte[] { 0x51, 0x2A, 0xD2, 0xD2 }, emit[MdpSec10.SelectPanOff..(MdpSec10.SelectPanOff + 4)]);
        Assert.Equal(-512, BinaryPrimitives.ReadInt16LittleEndian(emit.AsSpan(MdpSec10.ClipLoOff)));
        Assert.Equal(512, BinaryPrimitives.ReadInt16LittleEndian(emit.AsSpan(MdpSec10.ClipHiOff)));

        var again = new MapCamera();
        MdpSec10.Load(again, emit);
        Assert.Equal(CameraMode.Interior, again.Mode);
        Assert.Equal(45u, again.Pitch);
        Assert.True(again.SelectPan.Enabled);
        Assert.Equal(-752, again.SelectPan.XMin);
        Assert.Equal(1312, again.SelectPan.XMax);
        Assert.Equal(MapSelectPan.StockDistance, again.SelectPan.Distance);
        Assert.Equal(0, again.SelectPan.P28Raw);
        Assert.Equal(-512, again.SelectPan.ClipLo);
        Assert.Equal(512, again.SelectPan.ClipHi);
    }

    [Fact]
    public void Distance_does_not_change_pan_or_clip()
    {
        var cam = new MapCamera();
        MdpSec10.Load(cam, NewSec10());
        cam.SelectPan.ClipLo = -400;
        cam.SelectPan.Distance = 0.5f;
        Assert.Equal(-752, cam.SelectPan.XMin);
        Assert.Equal(1312, cam.SelectPan.XMax);
        Assert.Equal(-400, cam.SelectPan.ClipLo);
        Assert.Equal(512, cam.SelectPan.ClipHi);
        Assert.Equal(0x8000, cam.SelectPan.P28Raw);
    }

    [Fact]
    public void Map_dirty_includes_camera()
    {
        var map = new Map("2000");
        Assert.False(map.Dirty);
        map.Camera.Hydrate(NewSec10());
        Assert.False(map.Dirty);
        map.Camera.Mode = CameraMode.Outdoor;
        Assert.True(map.Dirty);
        Assert.Contains("mode 1", map.ToPatchText());
        Assert.Contains("select_pan", map.ToPatchText());
        Assert.Contains("distance=", map.ToPatchText());
    }

    [Fact]
    public void Stock_parm_and_marna_camera()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var parm = TryLoadSec10(field, "2000");
        var marna = TryLoadSec10(field, "2410");
        if (parm is null || marna is null)
        {
            return;
        }

        var p = new MapCamera();
        MdpSec10.Load(p, parm);
        Assert.Equal(CameraMode.Town, p.Mode);
        Assert.Equal(0u, p.Pitch);
        Assert.Equal(36u, p.Follow);
        Assert.Equal(0x10000u, p.FollowTerm);
        Assert.True(p.SelectPan.Enabled);
        Assert.Equal(-752, p.SelectPan.XMin);
        Assert.Equal(-512, p.SelectPan.ClipLo);
        Assert.Equal(512, p.SelectPan.ClipHi);
        Assert.Equal(MapSelectPan.StockDistance, p.SelectPan.Distance);
        Assert.Equal(256, p.SelectPan.Scale);
        Assert.Equal(650, p.ViewX);
        Assert.Equal(256, p.ViewY);

        var m = new MapCamera();
        MdpSec10.Load(m, marna);
        Assert.Equal(CameraMode.Outdoor, m.Mode);
        Assert.Equal(0u, m.Pitch);
        Assert.Equal(35u, m.Follow);
        Assert.Equal(0xDEBDu, m.FollowTerm);
        Assert.False(m.SelectPan.Enabled);
        Assert.Equal(-512, m.SelectPan.ClipLo);
        Assert.Equal(512, m.SelectPan.ClipHi);
        Assert.Equal(MapSelectPan.StockDistance, m.SelectPan.Distance);
        Assert.Equal(256, m.SelectPan.Scale);
    }

    private static byte[] NewSec10()
    {
        var blob = new byte[MdpSec10.Size];
        blob[2] = 1;
        blob[MdpSec10.ModeOff] = 0;
        BinaryPrimitives.WriteInt16LittleEndian(blob.AsSpan(MdpSec10.ViewXOff), 650);
        BinaryPrimitives.WriteInt16LittleEndian(blob.AsSpan(MdpSec10.ViewYOff), 256);
        BinaryPrimitives.WriteInt16LittleEndian(blob.AsSpan(MdpSec10.ViewZOff), 1127);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(MdpSec10.FollowOff), 36);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(MdpSec10.FollowTermOff), 0x10000);
        new byte[] { 0x51, 0x2A, 0xD2, 0xD2 }.CopyTo(blob.AsSpan(MdpSec10.SelectPanOff));
        BinaryPrimitives.WriteInt16LittleEndian(blob.AsSpan(MdpSec10.ClipLoOff), -512);
        BinaryPrimitives.WriteInt16LittleEndian(blob.AsSpan(MdpSec10.ClipHiOff), 512);
        return blob;
    }

    private static byte[]? TryLoadSec10(string field, string stem)
    {
        foreach (var name in new[] { stem + ".mdp", stem + ".MDP", stem.ToUpperInvariant() + ".mdp" })
        {
            var path = Path.Combine(field, name);
            if (!File.Exists(path))
            {
                continue;
            }

            return MdpTim.TrySlice(File.ReadAllBytes(path), 10);
        }

        return null;
    }
}
