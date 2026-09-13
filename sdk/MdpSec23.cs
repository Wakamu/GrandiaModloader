namespace Grandia.Sdk;

/// <summary>
/// MDP sec[23] character sprite-clip bank. Magic <c>00 07 00 01</c>.
/// Field load <c>+0x5B336</c> calls <c>+0x54D30</c> and binds:
/// <c>+0x1C</c> clip count → <c>[0x71A5CC]</c>,
/// <c>+0x20</c> 6-byte clip dir <c>{u16 id, u16 start, u16 count}</c> → <c>[0x71A9C8]</c>,
/// <c>+0x24</c> 4-byte frames <c>{u16 pose, u16 delay}</c> → <c>[0x71C1A8]</c>,
/// <c>+0x28</c> 4-byte pose dir <c>{u16 start, u16 n}</c> → <c>[0x71A5E0]</c>,
/// <c>+0x2C</c> 16-byte parts → <c>[0x71C168]</c>,
/// <c>+0x30</c> 8-byte sprite cookies → <c>[0x719958]</c>.
/// Play with <c>unit_bind {clipId} {talkId}</c> (field_talk). Not
/// <see cref="MdpSec21"/> (xyz polylines) and not sec[31] wander VM.
/// Read-only — no emit.
/// </summary>
public static class MdpSec23
{
    public const int MinHeader = 0x38;
    public const int ClipRowSize = 6;
    public const int FrameSize = 4;
    public const int PoseRowSize = 4;
    public const int PartSize = 16;
    public const int SpriteSize = 8;

    public static bool HasMagic(ReadOnlySpan<byte> blob) =>
        blob.Length >= 4
        && blob[0] == 0x00
        && blob[1] == 0x07
        && blob[2] == 0x00
        && blob[3] == 0x01;

    public static (IReadOnlyList<MapSpriteClip> Clips, IReadOnlyList<MapSpritePose> Poses) Parse(
        ReadOnlySpan<byte> blob)
    {
        if (!HasMagic(blob) || blob.Length < MinHeader)
        {
            return ([], []);
        }

        var clipCount = BitConverter.ToUInt16(blob.Slice(0x1C));
        var clipDir = ReadOff(blob, 0x20);
        var framesOff = ReadOff(blob, 0x24);
        var posesOff = ReadOff(blob, 0x28);
        var partsOff = ReadOff(blob, 0x2C);
        var spritesOff = ReadOff(blob, 0x30);
        var tailOff = ReadOff(blob, 0x34);
        if (clipDir == 0 || framesOff == 0 || clipDir >= blob.Length || framesOff >= blob.Length)
        {
            return ([], []);
        }

        var frameEnd = FirstAfter(framesOff, posesOff, partsOff, spritesOff, tailOff, blob.Length);
        var poseEnd = FirstAfter(posesOff, partsOff, spritesOff, tailOff, blob.Length);
        var partEnd = FirstAfter(partsOff, spritesOff, tailOff, blob.Length);
        var spriteEnd = FirstAfter(spritesOff, tailOff, blob.Length);

        var maxClips = (blob.Length - clipDir) / ClipRowSize;
        if (clipCount > maxClips)
        {
            clipCount = (ushort)maxClips;
        }

        var frameCount = framesOff < frameEnd ? (frameEnd - framesOff) / FrameSize : 0;
        var poseCount = posesOff > 0 && posesOff < poseEnd ? (poseEnd - posesOff) / PoseRowSize : 0;
        var partCount = partsOff > 0 && partsOff < partEnd ? (partEnd - partsOff) / PartSize : 0;
        var spriteCount = spritesOff > 0 && spritesOff < spriteEnd
            ? (spriteEnd - spritesOff) / SpriteSize
            : 0;

        var clips = new List<MapSpriteClip>(clipCount);
        for (var i = 0; i < clipCount; i++)
        {
            var at = clipDir + i * ClipRowSize;
            if (at + ClipRowSize > blob.Length)
            {
                break;
            }

            var id = BitConverter.ToUInt16(blob.Slice(at));
            var start = BitConverter.ToUInt16(blob.Slice(at + 2));
            var n = (int)BitConverter.ToUInt16(blob.Slice(at + 4));
            if (start >= frameCount)
            {
                clips.Add(new MapSpriteClip(i, id));
                continue;
            }

            if (start + n > frameCount)
            {
                n = frameCount - start;
            }

            var frames = new MapSpriteClipFrame[n];
            for (var j = 0; j < n; j++)
            {
                var f = framesOff + (start + j) * FrameSize;
                frames[j] = new MapSpriteClipFrame(
                    BitConverter.ToUInt16(blob.Slice(f)),
                    BitConverter.ToUInt16(blob.Slice(f + 2)));
            }

            clips.Add(new MapSpriteClip(i, id, frames));
        }

        var poses = new List<MapSpritePose>(poseCount);
        for (var i = 0; i < poseCount; i++)
        {
            var at = posesOff + i * PoseRowSize;
            if (at + PoseRowSize > blob.Length)
            {
                break;
            }

            var start = BitConverter.ToUInt16(blob.Slice(at));
            var n = (int)BitConverter.ToUInt16(blob.Slice(at + 2));
            if (partsOff == 0 || start >= partCount)
            {
                poses.Add(new MapSpritePose(i));
                continue;
            }

            if (start + n > partCount)
            {
                n = partCount - start;
            }

            var parts = new MapSpritePart[n];
            for (var j = 0; j < n; j++)
            {
                var p = partsOff + (start + j) * PartSize;
                parts[j] = ReadPart(blob, p, spritesOff, spriteCount);
            }

            poses.Add(new MapSpritePose(i, parts));
        }

        return (clips, poses);
    }

    private static MapSpritePart ReadPart(ReadOnlySpan<byte> blob, int off, int spritesOff, int spriteCount)
    {
        if (off < 0 || off + PartSize > blob.Length)
        {
            return new MapSpritePart(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var raw = blob.Slice(off, PartSize).ToArray();
        var spriteIndex = BitConverter.ToUInt16(raw.AsSpan(0xE));
        byte[] cookie = [];
        if (spritesOff > 0 && spriteIndex < spriteCount)
        {
            var s = spritesOff + spriteIndex * SpriteSize;
            if (s + SpriteSize <= blob.Length)
            {
                cookie = blob.Slice(s, SpriteSize).ToArray();
            }
        }

        return new MapSpritePart(
            raw[0],
            raw[1],
            BitConverter.ToInt16(raw.AsSpan(2)),
            BitConverter.ToInt16(raw.AsSpan(4)),
            BitConverter.ToInt16(raw.AsSpan(6)),
            BitConverter.ToInt16(raw.AsSpan(8)),
            BitConverter.ToUInt16(raw.AsSpan(0xA)),
            BitConverter.ToUInt16(raw.AsSpan(0xC)),
            spriteIndex,
            cookie,
            raw);
    }

    private static int ReadOff(ReadOnlySpan<byte> blob, int at)
    {
        var off = BitConverter.ToInt32(blob.Slice(at));
        return off > 0 && off < blob.Length ? off : 0;
    }

    private static int FirstAfter(int start, params int[] rest)
    {
        var best = int.MaxValue;
        foreach (var off in rest)
        {
            if (off > start && off < best)
            {
                best = off;
            }
        }

        return best == int.MaxValue ? 0 : best;
    }
}
