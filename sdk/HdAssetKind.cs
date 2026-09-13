namespace Grandia.Sdk;

/// <summary>SoftHD <c>{stem}_{kind}__*</c> token (not an MDP section).</summary>
public enum HdAssetKind
{
    Unknown = 0,
    Maps,
    Tenants,
    Anim,
    MapEff,
    Faces,
    Party,
    AreaMap,
    Logo,
    Title,
    Win,
    Pgmdt,
    CodeFonts,
}

/// <summary>Which SoftHD sidecar this open is.</summary>
public enum HdAssetFile
{
    Unknown = 0,
    Atlas,
    /// <summary>Maps-only CLUT / UV sheet (<c>__atlas_tables.png</c>).</summary>
    AtlasTables,
    SpriteInfo,
}

/// <summary>One SPRIV xywh frame (atlas pixels).</summary>
public readonly record struct HdSpriteRect(int X, int Y, int Width, int Height);

/// <summary>Decoded atlas pixels, RGBA8 tightly packed.</summary>
public sealed class HdPixels
{
    public HdPixels(int width, int height, byte[]? rgba = null)
    {
        Width = width;
        Height = height;
        Rgba = rgba ?? [];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }
}
