using System.Buffers.Binary;

namespace Grandia.Sdk;

/// <summary>
/// MDP sec[10] map-params block (always 512 bytes). Copied as 256 u16s
/// at <c>+0x61063</c> onto the field object at <c>[0x63FA9C]</c>.
/// Camera fields: mode +4, pitch +0x10, Select pan +0x94.
/// </summary>
public static class MdpSec10
{
    public const int Size = 512;
    public const int ModeOff = 0x04;
    public const int ViewXOff = 0x06;
    public const int ViewYOff = 0x08;
    public const int ViewZOff = 0x0A;
    public const int FollowOff = 0x0C;
    public const int PitchOff = 0x10;
    public const int FollowTermOff = 0x18;
    public const int ProjAOff = 0x84;
    public const int ProjBOff = 0x86;
    public const int ProjCOff = 0x88;
    public const int SelectPanOff = 0x94;
    public const int ClipLoOff = 0xE4;
    public const int ClipHiOff = 0xE8;
    public const int ClipScaleOff = 0xEE;

    public const int SelectPanXMin = -2048;
    public const int SelectPanXMax = 2032;
    public const int SelectPanZMin = -2032;
    public const int SelectPanZMax = 2048;

    public static void Load(MapCamera dest, ReadOnlySpan<byte> blob)
    {
        if (blob.Length < Size)
        {
            dest.Adopt([], CameraMode.Town, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                false, 0, 0, 0, 0, 0, 0, 0);
            dest.Dirty = false;
            return;
        }

        var raw = blob[..Size].ToArray();
        var mode = (CameraMode)(raw[ModeOff] & 3);
        var pitch = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(PitchOff));
        var follow = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(FollowOff));
        var followTerm = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(FollowTermOff));
        var projA = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(ProjAOff));
        var projB = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(ProjBOff));
        var projC = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(ProjCOff));
        var viewX = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(ViewXOff));
        var viewY = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(ViewYOff));
        var viewZ = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(ViewZOff));
        var pan = DecodeSelectPan(raw.AsSpan(SelectPanOff, 4), logicalWest: true);
        var clipLo = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(ClipLoOff));
        var clipHi = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(ClipHiOff));
        var scale = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(ClipScaleOff));
        dest.Adopt(raw, mode, pitch, follow, followTerm, projA, projB, projC,
            viewX, viewY, viewZ, pan.Enabled, pan.XMin, pan.ZMin, pan.XMax, pan.ZMax,
            clipLo, clipHi, scale);
        dest.Dirty = false;
    }

    /// <summary>Vanilla blob plus dirty camera fields. Empty when missing sec[10].</summary>
    public static byte[] Emit(MapCamera cam)
    {
        if (!cam.Present || cam.Raw.Length < Size)
        {
            return [];
        }

        var outb = cam.Raw.ToArray();
        outb[ModeOff] = (byte)cam.Mode;
        BinaryPrimitives.WriteInt16LittleEndian(outb.AsSpan(ViewXOff), cam.ViewX);
        BinaryPrimitives.WriteInt16LittleEndian(outb.AsSpan(ViewYOff), cam.ViewY);
        BinaryPrimitives.WriteInt16LittleEndian(outb.AsSpan(ViewZOff), cam.ViewZ);
        BinaryPrimitives.WriteUInt32LittleEndian(outb.AsSpan(FollowOff), cam.Follow);
        BinaryPrimitives.WriteUInt32LittleEndian(outb.AsSpan(PitchOff), cam.Pitch);
        var term = cam.FollowTerm == 0 ? 0x10000u : cam.FollowTerm;
        BinaryPrimitives.WriteUInt32LittleEndian(outb.AsSpan(FollowTermOff), term);
        BinaryPrimitives.WriteUInt16LittleEndian(outb.AsSpan(ProjAOff), cam.ProjA);
        BinaryPrimitives.WriteUInt16LittleEndian(outb.AsSpan(ProjBOff), cam.ProjB);
        BinaryPrimitives.WriteUInt16LittleEndian(outb.AsSpan(ProjCOff), cam.ProjC);
        EncodeSelectPan(cam.SelectPan).CopyTo(outb.AsSpan(SelectPanOff, 4));
        BinaryPrimitives.WriteInt16LittleEndian(outb.AsSpan(ClipLoOff), cam.SelectPan.ClipLo);
        BinaryPrimitives.WriteInt16LittleEndian(outb.AsSpan(ClipHiOff), cam.SelectPan.ClipHi);
        BinaryPrimitives.WriteInt16LittleEndian(outb.AsSpan(ClipScaleOff), cam.SelectPan.Scale);
        return outb;
    }

    public readonly record struct SelectPanBox(
        bool Enabled, int XMin, int ZMin, int XMax, int ZMax);

    /// <param name="logicalWest">
    /// Disk byte 0x94==0 means pan off; when true, treat west as 1 so
    /// re-enable restores a real limit instead of the −2048 off switch.
    /// </param>
    public static SelectPanBox DecodeSelectPan(ReadOnlySpan<byte> raw, bool logicalWest = false)
    {
        if (raw.Length < 4)
        {
            throw new ArgumentException("select pan needs 4 bytes", nameof(raw));
        }

        var b94 = raw[0];
        var b95 = raw[1];
        var b96 = raw[2];
        var b97 = raw[3];
        var enabled = b94 != 0;
        if (logicalWest && !enabled)
        {
            b94 = 1;
        }

        return new SelectPanBox(
            enabled,
            (b94 - 128) << 4,
            (128 - b97) << 4,
            (b96 - 128) << 4,
            (128 - b95) << 4);
    }

    public static byte[] EncodeSelectPan(MapSelectPan pan) =>
        EncodeSelectPan(pan.XMin, pan.ZMin, pan.XMax, pan.ZMax, pan.Enabled);

    /// <summary>
    /// Pack world XZ limits into sec[10]+0x94..+0x97. Snaps to 16-unit
    /// steps. While <paramref name="enabled"/>, west of −2048 becomes
    /// −2032 so byte 0x94 is not the pan-off switch.
    /// </summary>
    public static byte[] EncodeSelectPan(int xMin, int zMin, int xMax, int zMax,
        bool enabled = true)
    {
        xMin = Snap16(xMin);
        xMax = Snap16(xMax);
        zMin = Snap16(zMin);
        zMax = Snap16(zMax);
        if (xMin > xMax)
        {
            (xMin, xMax) = (xMax, xMin);
        }

        if (zMin > zMax)
        {
            (zMin, zMax) = (zMax, zMin);
        }

        xMin = Math.Clamp(xMin, SelectPanXMin, SelectPanXMax);
        xMax = Math.Clamp(xMax, SelectPanXMin, SelectPanXMax);
        zMin = Math.Clamp(zMin, SelectPanZMin, SelectPanZMax);
        zMax = Math.Clamp(zMax, SelectPanZMin, SelectPanZMax);
        var b94 = (xMin >> 4) + 128;
        var b96 = (xMax >> 4) + 128;
        var b95 = 128 - (zMax >> 4);
        var b97 = 128 - (zMin >> 4);
        b94 = Math.Clamp(b94, 0, 255);
        b95 = Math.Clamp(b95, 0, 255);
        b96 = Math.Clamp(b96, 0, 255);
        b97 = Math.Clamp(b97, 0, 255);
        if (!enabled)
        {
            b94 = 0;
        }
        else if (b94 == 0)
        {
            b94 = 1;
        }

        return [(byte)b94, (byte)b95, (byte)b96, (byte)b97];
    }

    private static int Snap16(int value) =>
        (int)Math.Round(value / 16.0) * 16;
}
