namespace Grandia.Sdk;

/// <summary>
/// SoftHD basename: <c>{stem}_{kind}__atlas.png</c> or
/// <c>{stem}_{kind}__spriteinfo.bin</c>. Locale variants keep a
/// <c>.jp</c> / <c>.sc</c> / <c>.tc</c> token before or after the extension.
/// </summary>
public readonly record struct HdTextureName(string Stem, HdAssetKind Kind, HdAssetFile File, string? Locale);

/// <summary>Parse SoftHD paths and SPRIV spriteinfo (magic <c>SPRIV</c>).</summary>
public static class HdTexturePath
{
    public static bool TryParse(string? path, out HdTextureName name)
    {
        name = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var baseName = Basename(path);
        if (baseName.Length == 0)
        {
            return false;
        }

        var lower = baseName.ToLowerInvariant();
        var file = HdAssetFile.Unknown;
        var mark = -1;
        var markLen = 0;
        var tables = lower.IndexOf("__atlas_tables", StringComparison.Ordinal);
        var atlas = lower.IndexOf("__atlas", StringComparison.Ordinal);
        var spri = lower.IndexOf("__spriteinfo", StringComparison.Ordinal);
        if (tables >= 0 && (spri < 0 || tables < spri))
        {
            file = HdAssetFile.AtlasTables;
            mark = tables;
            markLen = 14;
        }
        else if (atlas >= 0 && (spri < 0 || atlas < spri))
        {
            file = HdAssetFile.Atlas;
            mark = atlas;
            markLen = 7;
        }
        else if (spri >= 0)
        {
            file = HdAssetFile.SpriteInfo;
            mark = spri;
            markLen = 12;
        }

        string prefix;
        string tail = "";
        if (file != HdAssetFile.Unknown && mark > 0)
        {
            prefix = baseName[..mark];
            tail = lower[(mark + markLen)..];
        }
        else
        {
            // SoftHD catalog objects often store `{stem}_{kind}` without
            // `__atlas` / `__spriteinfo` (or a directory prefix only).
            prefix = baseName;
        }

        var under = prefix.LastIndexOf('_');
        if (under <= 0 || under == prefix.Length - 1)
        {
            return false;
        }

        var kindToken = prefix[(under + 1)..];
        var kind = ParseKind(kindToken);
        if (kind == HdAssetKind.Unknown)
        {
            return false;
        }

        var stem = prefix[..under].ToUpperInvariant();
        if (stem.Length == 0)
        {
            return false;
        }

        var locale = ParseLocale(tail);
        name = new HdTextureName(stem, kind, file, locale);
        return true;
    }

    public static bool IsHdAssetPath(string? path) => TryParse(path, out _);

    public static string Token(HdAssetKind kind) =>
        kind switch
        {
            HdAssetKind.Maps => "maps",
            HdAssetKind.Tenants => "tenants",
            HdAssetKind.Anim => "anim",
            HdAssetKind.MapEff => "mapeff",
            HdAssetKind.Faces => "faces",
            HdAssetKind.Party => "party",
            HdAssetKind.AreaMap => "areamap",
            HdAssetKind.Logo => "logo",
            HdAssetKind.Title => "title",
            HdAssetKind.Win => "win",
            HdAssetKind.Pgmdt => "pgmdt",
            HdAssetKind.CodeFonts => "codefonts",
            _ => "",
        };

    /// <summary>
    /// SPRIV: <c>SPRIV</c> + version + u16 count, then count × 8-byte
    /// <c>{x,y,w,h}</c>. Version 1 (map <c>anim</c>) packs
    /// <c>u8 ver + u16 count</c> at +5 (records at +8). Version 2+
    /// (faces / maps / tenants / party) uses <c>u16 ver + u16 count</c>
    /// (records at +9). Trailing animation bytes are ignored.
    /// </summary>
    public static IReadOnlyList<HdSpriteRect> ParseSpriteInfo(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 8
            || blob[0] != (byte)'S'
            || blob[1] != (byte)'P'
            || blob[2] != (byte)'R'
            || blob[3] != (byte)'I'
            || blob[4] != (byte)'V')
        {
            return [];
        }

        var version = blob[5];
        var dataOff = 9;
        var countOff = 7;
        if (version == 1)
        {
            dataOff = 8;
            countOff = 6;
        }
        else if (blob.Length < 9)
        {
            return [];
        }

        var count = (int)BitConverter.ToUInt16(blob.Slice(countOff));
        const int stride = 8;
        var max = (blob.Length - dataOff) / stride;
        if (count > max)
        {
            count = max;
        }

        var rects = new HdSpriteRect[count];
        var off = dataOff;
        for (var i = 0; i < count; i++)
        {
            var x = BitConverter.ToUInt16(blob.Slice(off));
            var y = BitConverter.ToUInt16(blob.Slice(off + 2));
            var w = BitConverter.ToUInt16(blob.Slice(off + 4));
            var h = BitConverter.ToUInt16(blob.Slice(off + 6));
            rects[i] = new HdSpriteRect(x, y, w, h);
            off += stride;
        }

        return rects;
    }

    private static HdAssetKind ParseKind(string token)
    {
        return token.ToLowerInvariant() switch
        {
            "maps" => HdAssetKind.Maps,
            "tenants" => HdAssetKind.Tenants,
            "anim" => HdAssetKind.Anim,
            "mapeff" => HdAssetKind.MapEff,
            "faces" => HdAssetKind.Faces,
            "party" => HdAssetKind.Party,
            "areamap" => HdAssetKind.AreaMap,
            "logo" => HdAssetKind.Logo,
            "title" => HdAssetKind.Title,
            "win" => HdAssetKind.Win,
            "pgmdt" => HdAssetKind.Pgmdt,
            "codefonts" => HdAssetKind.CodeFonts,
            _ => HdAssetKind.Unknown,
        };
    }

    private static string? ParseLocale(string tail)
    {
        if (string.IsNullOrEmpty(tail))
        {
            return null;
        }

        var parts = tail.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part is "png" or "bin")
            {
                continue;
            }

            if (part is "jp" or "kr" or "sc" or "tc")
            {
                return part;
            }
        }

        return null;
    }

    private static string Basename(string path)
    {
        var n = path.LastIndexOfAny(['/', '\\']);
        return n >= 0 ? path[(n + 1)..] : path;
    }
}
