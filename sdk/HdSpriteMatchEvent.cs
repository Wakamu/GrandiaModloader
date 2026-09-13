namespace Grandia.Sdk;

/// <summary>
/// SoftHD just inserted one SPRIV row into its live table
/// (<c>+0x29164</c> 32-byte v3/v4, or <c>+0x29301</c> 8-byte v1/v2).
/// <see cref="Index"/> is that row. <see cref="Rect"/> is atlas xywh when
/// the record starts with it (faces / party / v1). Tenants / maps v4 keep
/// match fields in <see cref="Record"/> — not <c>SpriteIndex</c>.
/// Observe-only; fires once per row while spriteinfo is parsed.
/// </summary>
public sealed class HdSpriteMatchEvent
{
    public HdSpriteMatchEvent(string path, int index, byte[]? record)
    {
        Path = path ?? "";
        Index = index;
        Record = record is { Length: > 0 } ? record.ToArray() : [];
        if (HdTexturePath.TryParse(Path, out var name))
        {
            Stem = name.Stem;
            Kind = name.Kind;
            File = name.File;
            Locale = name.Locale;
        }
        else
        {
            Stem = "";
            Kind = HdAssetKind.Unknown;
            File = HdAssetFile.Unknown;
            Locale = null;
        }

        Rect = RectFromRecord(Record);
    }

    public string Path { get; }

    public string Stem { get; }

    public HdAssetKind Kind { get; }

    public HdAssetFile File { get; }

    public string? Locale { get; }

    /// <summary>Row SoftHD just appended (0-based).</summary>
    public int Index { get; }

    /// <summary>Atlas crop when the record is packed xywh; otherwise zeros.</summary>
    public HdSpriteRect Rect { get; }

    /// <summary>Raw insert (8 bytes v1/v2, 32 bytes v3/v4).</summary>
    public IReadOnlyList<byte> Record { get; }

    internal static HdSpriteRect RectFromRecord(IReadOnlyList<byte> record)
    {
        if (record.Count < 8)
        {
            return default;
        }

        var raw = record as byte[] ?? record.ToArray();
        var off = 0;
        if (raw.Length >= 10 && !LooksLikeRect(raw, 0) && LooksLikeRect(raw, 2))
        {
            off = 2;
        }

        return new HdSpriteRect(
            BitConverter.ToUInt16(raw, off),
            BitConverter.ToUInt16(raw, off + 2),
            BitConverter.ToUInt16(raw, off + 4),
            BitConverter.ToUInt16(raw, off + 6));
    }

    private static bool LooksLikeRect(byte[] raw, int off)
    {
        if (off + 8 > raw.Length)
        {
            return false;
        }

        var x = BitConverter.ToUInt16(raw, off);
        var y = BitConverter.ToUInt16(raw, off + 2);
        var w = BitConverter.ToUInt16(raw, off + 4);
        var h = BitConverter.ToUInt16(raw, off + 6);
        return w is > 0 and <= 2048 && h is > 0 and <= 2048 && x < 8192 && y < 8192;
    }
}
