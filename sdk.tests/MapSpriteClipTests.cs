using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class MapSpriteClipTests
{
    private static readonly string[] FieldDirs =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\GRANDIA HD Remaster\content\FIELD",
        @"C:\Program Files (x86)\Steam\steamapps\common\Grandia HD Remaster\content\FIELD",
    ];

    [Fact]
    public void Parse_rejects_empty_and_wrong_magic()
    {
        Assert.Empty(MdpSec23.Parse([]).Clips);
        Assert.Empty(MdpSec23.Parse(new byte[2]).Clips);
        Assert.Empty(MdpSec23.Parse(new byte[0x40]).Clips);
        var wrong = new byte[0x40];
        wrong[0] = 0x00;
        wrong[1] = 0x06;
        wrong[2] = 0x00;
        wrong[3] = 0x01;
        Assert.Empty(MdpSec23.Parse(wrong).Clips);
        Assert.Empty(MdpSec23.Parse(wrong).Poses);
    }

    [Fact]
    public void Parse_reads_clip_frames_and_pose_parts()
    {
        var cookie = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44 };
        var blob = BuildSec23(
            clips: [(15, [(0, 15), (1, 8)])],
            poses:
            [
                (0, [Part(44, 4, -42, 1, -16, 15, 0x8808, 3, 0)]),
                (1, [Part(44, 4, -42, 1, -16, 15, 0x8808, 3, 0)]),
            ],
            cookies: [cookie]);

        var (clips, poses) = MdpSec23.Parse(blob);
        var clip = Assert.Single(clips);
        Assert.Equal(15, clip.Id);
        Assert.Equal(2, clip.Frames.Count);
        Assert.Equal(new MapSpriteClipFrame(0, 15), clip.Frames[0]);
        Assert.Equal(new MapSpriteClipFrame(1, 8), clip.Frames[1]);

        Assert.Equal(2, poses.Count);
        var pose = poses[0];
        var part = Assert.Single(pose.Parts);
        Assert.Equal(44, part.Width);
        Assert.Equal(4, part.Height);
        Assert.Equal(-42, part.OffsetX);
        Assert.Equal(1, part.OffsetY);
        Assert.Equal(-16, part.OffsetZ);
        Assert.Equal(15, part.Extra);
        Assert.Equal(0x8808, part.Tpage);
        Assert.Equal(3, part.Channel);
        Assert.Equal(0, part.SpriteIndex);
        Assert.Equal(cookie, part.Cookie.ToArray());
        Assert.Equal(16, part.Raw.Count);

        var map = new Map("BA38");
        map.SpriteClips.Hydrate(clips);
        map.Poses.Hydrate(poses);
        Assert.False(map.Dirty);
        Assert.Equal(15, map.SpriteClips.OfId(15)!.Id);
        Assert.Equal(3, map.Poses[0]!.Parts[0].Channel);
    }

    [Fact]
    public void Parse_clamps_oob_frame_and_part_ranges()
    {
        var blob = BuildSec23(
            clips: [(6, [(0, 2)]), (99, [(0, 2)])],
            poses: [(0, [Part(8, 1, 0, 0, 0, 0, 0, 0, 99)])],
            cookies: [new byte[8]]);
        BitConverter.TryWriteBytes(blob.AsSpan(0x40 + 6 + 2), (ushort)40);
        BitConverter.TryWriteBytes(blob.AsSpan(0x40 + 6 + 4), (ushort)2);

        var (clips, poses) = MdpSec23.Parse(blob);
        Assert.Equal(2, clips.Count);
        Assert.Single(clips[0].Frames);
        Assert.Empty(clips[1].Frames);
        Assert.Empty(Assert.Single(Assert.Single(poses).Parts).Cookie);
    }

    [Fact]
    public void Hydrate_does_not_mark_map_dirty()
    {
        var parsed = MdpSec23.Parse(BuildSec23(
            clips: [(12, [(0, 5)])],
            poses: [(0, [Part(1, 1, 0, 0, 0, 0, 0, 2, 0)])],
            cookies: [new byte[8]]));
        var map = new Map("BA38");
        map.SpriteClips.Hydrate(parsed.Clips);
        map.Poses.Hydrate(parsed.Poses);
        Assert.False(map.Dirty);
        Assert.Single(map.SpriteClips.Items);
        Assert.Equal(12, map.SpriteClips.OfId(12)!.Id);
        Assert.Null(map.SpriteClips.OfId(15));
        Assert.Equal(2, map.Poses[0]!.Parts[0].Channel);
    }

    [Fact]
    public void Parse_stock_BA38_cinematic_clips()
    {
        var field = FieldDirs.FirstOrDefault(Directory.Exists);
        if (field is null)
        {
            return;
        }

        var blob = TryLoadSec23(field, "BA38");
        if (blob is null)
        {
            return;
        }

        var (clips, poses) = MdpSec23.Parse(blob);
        var table = new MapSpriteClipTable(clips);
        var poseTable = new MapSpritePoseTable(poses);
        Assert.Equal(34, table.Items.Count);
        Assert.Equal(93, poseTable.Items.Count);

        var leen = table.OfId(15)!;
        Assert.Equal(2, leen.Frames.Count);
        Assert.Equal(new MapSpriteClipFrame(0x28, 15), leen.Frames[0]);
        Assert.Equal(new MapSpriteClipFrame(0x27, 15), leen.Frames[1]);
        var leenA = poseTable[0x28]!.Parts[0];
        var leenB = poseTable[0x27]!.Parts[0];
        Assert.Equal(3, leenA.Channel);
        Assert.Equal(3, leenB.Channel);
        Assert.Equal(0x8808, leenA.Tpage);
        Assert.Equal(0x0DA2, leenA.SpriteIndex);
        Assert.Equal(0x0D73, leenB.SpriteIndex);
        Assert.Equal(leenA.OffsetX, leenB.OffsetX);
        Assert.Equal(8, leenA.Cookie.Count);

        var baal = table.OfId(6)!;
        Assert.Equal(2, baal.Frames.Count);
        Assert.Equal(new MapSpriteClipFrame(0x0E, 15), baal.Frames[0]);
        Assert.Equal(new MapSpriteClipFrame(0x18, 15), baal.Frames[1]);
        Assert.Equal(0, poseTable[0x0E]!.Parts[0].Channel);
        Assert.Equal(1, poseTable[0x18]!.Parts[0].Channel);

        var mullen = table.OfId(12)!;
        Assert.Equal(11, mullen.Frames.Count);
        Assert.Equal(new MapSpriteClipFrame(0x21, 5), mullen.Frames[0]);
        Assert.Equal(0x1B, mullen.Frames[4].Pose);
        Assert.Equal(2, poseTable[0x21]!.Parts[0].Channel);

        var map = new Map("BA38");
        map.SpriteClips.Hydrate(clips);
        map.Poses.Hydrate(poses);
        Assert.False(map.Dirty);
        Assert.Equal(15, map.SpriteClips.OfId(15)!.Id);

        var empty = TryLoadSec23(field, "2410");
        if (empty is null)
        {
            var (noClips, noPoses) = MdpSec23.Parse([]);
            Assert.Empty(noClips);
            Assert.Empty(noPoses);
        }
        else
        {
            var missing = MdpSec23.Parse(empty);
            Assert.Empty(missing.Clips);
            Assert.Empty(missing.Poses);
        }
    }

    private static byte[] Part(
        int w, int h, int x, int y, int z, int extra, int tpage, int channel, int sprite)
    {
        var raw = new byte[16];
        raw[0] = (byte)w;
        raw[1] = (byte)h;
        BitConverter.TryWriteBytes(raw.AsSpan(2), (short)x);
        BitConverter.TryWriteBytes(raw.AsSpan(4), (short)y);
        BitConverter.TryWriteBytes(raw.AsSpan(6), (short)z);
        BitConverter.TryWriteBytes(raw.AsSpan(8), (short)extra);
        BitConverter.TryWriteBytes(raw.AsSpan(0xA), (ushort)tpage);
        BitConverter.TryWriteBytes(raw.AsSpan(0xC), (ushort)channel);
        BitConverter.TryWriteBytes(raw.AsSpan(0xE), (ushort)sprite);
        return raw;
    }

    private static byte[] BuildSec23(
        (int Id, (int Pose, int Delay)[] Frames)[] clips,
        (int Index, byte[][] Parts)[] poses,
        byte[][] cookies)
    {
        var clipDir = 0x40;
        var framesOff = clipDir + clips.Length * 6;
        var frameN = clips.Sum(c => c.Frames.Length);
        var posesOff = framesOff + frameN * 4;
        var poseN = poses.Length == 0 ? 0 : poses.Max(p => p.Index) + 1;
        var partsOff = posesOff + poseN * 4;
        var partN = poses.Sum(p => p.Parts.Length);
        var spritesOff = partsOff + partN * 16;
        var tailOff = spritesOff + cookies.Length * 8;
        var blob = new byte[tailOff];
        blob[0] = 0x00;
        blob[1] = 0x07;
        blob[2] = 0x00;
        blob[3] = 0x01;
        BitConverter.TryWriteBytes(blob.AsSpan(0x14), 1);
        BitConverter.TryWriteBytes(blob.AsSpan(0x18), unchecked((int)0xFFFFFFFF));
        BitConverter.TryWriteBytes(blob.AsSpan(0x1C), (ushort)clips.Length);
        BitConverter.TryWriteBytes(blob.AsSpan(0x20), clipDir);
        BitConverter.TryWriteBytes(blob.AsSpan(0x24), framesOff);
        BitConverter.TryWriteBytes(blob.AsSpan(0x28), posesOff);
        BitConverter.TryWriteBytes(blob.AsSpan(0x2C), partsOff);
        BitConverter.TryWriteBytes(blob.AsSpan(0x30), spritesOff);
        BitConverter.TryWriteBytes(blob.AsSpan(0x34), tailOff);

        var frameCursor = 0;
        var clipAt = clipDir;
        foreach (var clip in clips)
        {
            BitConverter.TryWriteBytes(blob.AsSpan(clipAt), (ushort)clip.Id);
            BitConverter.TryWriteBytes(blob.AsSpan(clipAt + 2), (ushort)frameCursor);
            BitConverter.TryWriteBytes(blob.AsSpan(clipAt + 4), (ushort)clip.Frames.Length);
            foreach (var (pose, delay) in clip.Frames)
            {
                var f = framesOff + frameCursor * 4;
                BitConverter.TryWriteBytes(blob.AsSpan(f), (ushort)pose);
                BitConverter.TryWriteBytes(blob.AsSpan(f + 2), (ushort)delay);
                frameCursor++;
            }

            clipAt += 6;
        }

        var partCursor = 0;
        var poseByIndex = poses.ToDictionary(p => p.Index, p => p.Parts);
        for (var i = 0; i < poseN; i++)
        {
            var parts = poseByIndex.TryGetValue(i, out var list) ? list : [];
            var at = posesOff + i * 4;
            BitConverter.TryWriteBytes(blob.AsSpan(at), (ushort)partCursor);
            BitConverter.TryWriteBytes(blob.AsSpan(at + 2), (ushort)parts.Length);
            foreach (var part in parts)
            {
                part.CopyTo(blob.AsSpan(partsOff + partCursor * 16));
                partCursor++;
            }
        }

        for (var i = 0; i < cookies.Length; i++)
        {
            cookies[i].AsSpan(0, Math.Min(8, cookies[i].Length)).CopyTo(blob.AsSpan(spritesOff + i * 8));
        }

        return blob;
    }

    private static byte[]? TryLoadSec23(string field, string stem)
    {
        foreach (var name in new[] { stem + ".mdp", stem + ".MDP", stem.ToUpperInvariant() + ".mdp" })
        {
            var path = Path.Combine(field, name);
            if (!File.Exists(path))
            {
                continue;
            }

            return TrySlice(File.ReadAllBytes(path), 23);
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
