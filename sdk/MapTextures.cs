namespace Grandia.Sdk;

/// <summary>
/// A crop of the original PS1 TIM (packed VRAM words). This is the
/// map texture / sprite sheet, not a SoftHD <c>00xx.png</c>.
/// </summary>
public sealed class MapTextureCrop
{
    public MapTextureCrop(
        int tpage,
        int u,
        int v,
        int texelWidth,
        int texelHeight,
        int vramX,
        int vramY,
        int wordWidth,
        int bpp,
        ushort[] words,
        int clut = 0)
    {
        Tpage = tpage;
        U = u;
        V = v;
        TexelWidth = texelWidth;
        TexelHeight = texelHeight;
        VramX = vramX;
        VramY = vramY;
        WordWidth = wordWidth;
        Bpp = bpp;
        Words = words;
        Clut = clut;
    }

    public int Tpage { get; }

    public int U { get; }

    public int V { get; }

    public int TexelWidth { get; }

    public int TexelHeight { get; }

    public int VramX { get; }

    public int VramY { get; }

    public int WordWidth { get; }

    public int Bpp { get; }

    /// <summary>GPU CLUT word (sec[32] / v4 pack). 0 = unknown — <see cref="Rgba"/> uses a gray ramp.</summary>
    public int Clut { get; }

    /// <summary>Row-major packed VRAM words (<see cref="WordWidth"/> × <see cref="TexelHeight"/>).</summary>
    public IReadOnlyList<ushort> Words { get; }

    public bool Empty
    {
        get
        {
            for (var i = 0; i < Words.Count; i++)
            {
                if (Words[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>4bpp/8bpp indices, one byte per texel, row-major.</summary>
    public byte[] Indices()
    {
        var outp = new byte[TexelWidth * TexelHeight];
        if (WordWidth <= 0 || Bpp <= 0)
        {
            return outp;
        }

        var perWord = 16 / Bpp;
        var mask = (1 << Bpp) - 1;
        for (var row = 0; row < TexelHeight; row++)
        {
            for (var col = 0; col < TexelWidth; col++)
            {
                var word = Words[row * WordWidth + col / perWord];
                var shift = (col % perWord) * Bpp;
                outp[row * TexelWidth + col] = (byte)((word >> shift) & mask);
            }
        }

        return outp;
    }

    /// <summary>
    /// Texel RGBA (index 0 is transparent). Uses <paramref name="palette"/>
    /// or a 16-entry gray ramp.
    /// </summary>
    public byte[] Rgba(IReadOnlyList<ushort>? palette = null)
    {
        var idx = Indices();
        var dest = new byte[TexelWidth * TexelHeight * 4];
        for (var i = 0; i < idx.Length; i++)
        {
            var n = idx[i];
            var word = palette is { Count: > 0 } && n < palette.Count
                ? palette[n]
                : (ushort)((n * 0x1084) & 0x7FFF);
            var at = i * 4;
            dest[at] = Expand5(word & 0x1F);
            dest[at + 1] = Expand5((word >> 5) & 0x1F);
            dest[at + 2] = Expand5((word >> 10) & 0x1F);
            dest[at + 3] = n == 0 ? (byte)0 : (byte)255;
        }

        return dest;
    }

    public byte[] Rgba(MapTextureSheet sheet) =>
        sheet.TryPalette(Clut, out var pal) ? Rgba(pal) : Rgba();

    public uint Hash() =>
        Words is ushort[] arr
            ? HdSpriteKey.HashTexels(arr)
            : HdSpriteKey.HashTexels(Words.ToArray());

    public override string ToString() =>
        $"uv={U},{V} {TexelWidth}x{TexelHeight} @{VramX},{VramY} tpage=0x{Tpage:X} empty={Empty}";

    private static byte Expand5(int v) => (byte)((v << 3) | (v >> 2));
}

/// <summary>
/// Original PS1 field TIM: sec[1] + sec[27] (+ rare sec[16]) decoded
/// into a 1024×512 word buffer. Pose parts and sec[32] UVs crop from
/// this sheet. Cinematic close-ups (BA38 <c>tpage 0x8808</c> at Y=0)
/// are a later upload and stay empty here.
/// </summary>
public sealed class MapTextureSheet
{
    private readonly ushort[] _words;

    public MapTextureSheet(ushort[]? words = null, IReadOnlyList<MapTextureUpload>? uploads = null)
    {
        _words = words is { Length: >= MdpTim.Width * MdpTim.Height }
            ? words
            : new ushort[MdpTim.Width * MdpTim.Height];
        Uploads = uploads?.ToList() ?? [];
    }

    public int Width => MdpTim.Width;

    public int Height => MdpTim.Height;

    public IReadOnlyList<ushort> Words => _words;

    public IReadOnlyList<MapTextureUpload> Uploads { get; }

    public int Occupied => _words.Count(w => w != 0);

    public ushort WordAt(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height ? _words[y * Width + x] : (ushort)0;

    public bool TryPalette(int clut, out ushort[] palette)
    {
        palette = [];
        if (clut == 0)
        {
            return false;
        }

        var x = (clut & 0x3F) * 16;
        var y = clut >> 6;
        if (x < 0 || y < 0 || y >= Height || x + 16 > Width)
        {
            return false;
        }

        palette = new ushort[16];
        Array.Copy(_words, y * Width + x, palette, 0, 16);
        return palette.Any(w => w != 0);
    }

    public bool TryCrop(MapSpritePart part, out MapTextureCrop crop) =>
        TryCrop(part.Tpage, part.UvU, part.UvV, part.Width, part.Height, out crop);

    public bool TryCrop(MapSpriteUv uv, out MapTextureCrop crop) =>
        TryCrop(uv.Tpage, uv.U, uv.V, uv.Width, uv.Height, out crop, uv.Clut);

    /// <summary>
    /// HD sprite → original TIM. v4 rows use <see cref="MapSprite.Source"/>.
    /// Anim FNV keys hash <paramref name="cells"/> (usually
    /// <see cref="MapSpriteBank.Uv"/>) until the footer key matches.
    /// </summary>
    public bool TryCrop(MapSprite sprite, IEnumerable<MapSpriteUv>? cells, out MapTextureCrop crop)
    {
        if (sprite.Source is { } uv)
        {
            return TryCrop(uv, out crop);
        }

        crop = new MapTextureCrop(0, 0, 0, 0, 0, 0, 0, 0, 4, []);
        if (sprite.Key == 0 || cells is null)
        {
            return false;
        }

        foreach (var cell in cells)
        {
            if (!TryCrop(cell, out var cand) || cand.Empty)
            {
                continue;
            }

            if (HdSpriteKey.SignExtend(cand.Hash()) == sprite.Key)
            {
                crop = cand;
                return true;
            }
        }

        return false;
    }

    public bool TryCrop(int tpage, int u, int v, int width, int height, out MapTextureCrop crop, int clut = 0)
    {
        crop = new MapTextureCrop(tpage, u, v, width, height, 0, 0, 0, 4, [], clut);
        if (!HdSpriteKey.TryMapRect(tpage, u, v, width, height, out var x, out var y, out var ww, out var bpp))
        {
            return false;
        }

        var primary = CopyRect(x, y, ww, height);
        var altY = y ^ 256;
        if (IsEmpty(primary) && altY >= 0 && altY + height <= Height)
        {
            var alt = CopyRect(x, altY, ww, height);
            if (!IsEmpty(alt))
            {
                y = altY;
                primary = alt;
            }
        }

        crop = new MapTextureCrop(tpage, u, v, width, height, x, y, ww, bpp, primary, clut);
        return true;
    }

    private ushort[] CopyRect(int x, int y, int wordWidth, int height)
    {
        var dest = new ushort[wordWidth * height];
        for (var row = 0; row < height; row++)
        {
            var src = (y + row) * Width + x;
            Array.Copy(_words, src, dest, row * wordWidth, wordWidth);
        }

        return dest;
    }

    private static bool IsEmpty(ushort[] words)
    {
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// This map's original PS1 TIM sheet. Same lazy fopen hydrate as
/// <see cref="Map.Zones"/>. Read-only — not SoftHD
/// <see cref="Map.Sprites"/>.
/// </summary>
public sealed class MapTextureTable
{
    private MapTextureSheet _sheet = new();

    internal Action? Ensure { get; set; }

    public MapTextureSheet Sheet
    {
        get
        {
            Ensure?.Invoke();
            return _sheet;
        }
    }

    public IReadOnlyList<ushort> Words => Sheet.Words;

    public IReadOnlyList<MapTextureUpload> Uploads => Sheet.Uploads;

    public int Occupied => Sheet.Occupied;

    public bool TryCrop(MapSpritePart part, out MapTextureCrop crop) =>
        Sheet.TryCrop(part, out crop);

    public bool TryCrop(MapSpriteUv uv, out MapTextureCrop crop) =>
        Sheet.TryCrop(uv, out crop);

    public bool TryCrop(MapSprite sprite, IEnumerable<MapSpriteUv>? cells, out MapTextureCrop crop) =>
        Sheet.TryCrop(sprite, cells, out crop);

    public bool TryCrop(int tpage, int u, int v, int width, int height, out MapTextureCrop crop) =>
        Sheet.TryCrop(tpage, u, v, width, height, out crop);

    internal void Hydrate(MapTextureSheet sheet)
    {
        Ensure = null;
        _sheet = sheet;
    }
}
