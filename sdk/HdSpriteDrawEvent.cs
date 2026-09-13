namespace Grandia.Sdk;

/// <summary>
/// SoftHD resolved a live blit to an HD atlas crop
/// (<c>+0x1B67A</c>, every kind — maps / tenants / anim / win / mapeff).
/// <see cref="Index"/> is the SPRIV row when SoftHD used that table;
/// <see cref="Rect"/> is the atlas crop it will sample; <see cref="Live"/>
/// is the live UV box (sprite <c>+6/+8/+A/+C</c>). The map key is
/// FNV-1a of the PS1 VRAM texels at that UV (<c>+0x1A620</c>), not
/// the cookie bytes. 32-byte tables only
/// (tenants / maps / party v3/v4). Observe-only; fires per blit.
/// </summary>
public sealed class HdSpriteDrawEvent
{
    public HdSpriteDrawEvent(string path, int index, byte[]? record, HdSpriteRect live)
    {
        Path = path ?? "";
        Index = index;
        Live = live;
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

        Rect = HdSpriteMatchEvent.RectFromRecord(Record);
    }

    public string Path { get; }

    public string Stem { get; }

    public HdAssetKind Kind { get; }

    public HdAssetFile File { get; }

    public string? Locale { get; }

    /// <summary>Matched SPRIV row (0-based). Same index as <see cref="HdSpriteMatchEvent"/>.</summary>
    public int Index { get; }

    /// <summary>Atlas crop SoftHD will sample.</summary>
    public HdSpriteRect Rect { get; }

    /// <summary>Live UV box (u, v, w, h) at sprite <c>+6/+8/+A/+C</c>.</summary>
    public HdSpriteRect Live { get; }

    /// <summary>Raw 32-byte SPRIV row (xywh + PS1 match fields).</summary>
    public IReadOnlyList<byte> Record { get; }
}
