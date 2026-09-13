namespace Grandia.Sdk;

/// <summary>
/// SoftHD is about to open an <c>*__atlas.png</c> or <c>*__spriteinfo.bin</c>
/// (fopen / <c>SDL_RWFromFile</c>). <see cref="Kind"/> is the filename
/// token (<c>maps</c>, <c>tenants</c>, <c>anim</c>, <c>mapeff</c>,
/// <c>faces</c>, <c>party</c>, <c>areamap</c>, …) — not an MDP section.
/// <see cref="Bytes"/> is the stock file. <see cref="Pixels"/> decodes
/// the PNG only on first read. <see cref="Replace"/> / <see cref="ReplacePixels"/>
/// serve a virt-file so SoftHD decodes the replacement. Unread and
/// unassigned is a no-op.
/// </summary>
public sealed class HdTextureEvent
{
    private readonly byte[] _bytes;
    private readonly Func<byte[], HdPixels?>? _decode;
    private readonly Func<int, int, byte[], byte[]?>? _encode;
    private IReadOnlyList<HdSpriteRect>? _rects;
    private HdPixels? _pixels;
    private bool _pixelsTried;
    private byte[]? _replacement;

    public HdTextureEvent(string path, byte[]? bytes,
        Func<byte[], HdPixels?>? decode = null,
        Func<int, int, byte[], byte[]?>? encode = null)
    {
        Path = path ?? "";
        _bytes = bytes is { Length: > 0 } ? bytes : [];
        _decode = decode;
        _encode = encode;
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
    }

    public string Path { get; }

    /// <summary>Map / pack stem (<c>2000</c>, <c>FC01</c>, <c>PGR00</c>, <c>AREAMAP</c>).</summary>
    public string Stem { get; }

    public HdAssetKind Kind { get; }

    public HdAssetFile File { get; }

    /// <summary>Locale token (<c>jp</c> / <c>sc</c> / <c>tc</c>), or null.</summary>
    public string? Locale { get; }

    /// <summary>Stock (or current) file bytes. PNG or SPRIV — not decoded pixels.</summary>
    public IReadOnlyList<byte> Bytes => _bytes;

    /// <summary>SPRIV xywh when <see cref="File"/> is <see cref="HdAssetFile.SpriteInfo"/>.</summary>
    public IReadOnlyList<HdSpriteRect> Rects =>
        _rects ??= File == HdAssetFile.SpriteInfo ? HdTexturePath.ParseSpriteInfo(_bytes) : [];

    /// <summary>
    /// Decoded RGBA8 atlas (<see cref="HdPixels.Width"/> /
    /// <see cref="HdPixels.Height"/> / <see cref="HdPixels.Rgba"/>).
    /// Null when this open is spriteinfo, decode is unavailable, or the
    /// PNG fails. First get is the only decode.
    /// </summary>
    public HdPixels? Pixels
    {
        get
        {
            if (_pixelsTried)
            {
                return _pixels;
            }

            _pixelsTried = true;
            if (File != HdAssetFile.Atlas || _decode == null || _bytes.Length == 0)
            {
                return null;
            }

            _pixels = _decode(_bytes);
            return _pixels;
        }
    }

    /// <summary>Serve these raw PNG or SPRIV bytes instead of the stock file.</summary>
    public void Replace(byte[] bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return;
        }

        _replacement = bytes.ToArray();
    }

    /// <summary>Encode RGBA8 to PNG and <see cref="Replace"/> that file (atlas only).</summary>
    public void ReplacePixels(int width, int height, byte[] rgba)
    {
        if (File != HdAssetFile.Atlas || _encode == null || rgba is not { Length: > 0 }
            || width <= 0 || height <= 0)
        {
            return;
        }

        var png = _encode(width, height, rgba);
        if (png is { Length: > 0 })
        {
            _replacement = png;
        }
    }

    internal bool TryGetReplacement(out byte[] dest)
    {
        dest = _replacement ?? [];
        return dest.Length > 0;
    }
}
