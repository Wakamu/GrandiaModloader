using System.IO.Compression;

namespace Grandia.Sdk;

/// <summary>Minimal RGBA PNG (no extra packages). Same layout as the HD extract.</summary>
public static class MapTexturePng
{
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
        {
            return [];
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, (uint)width);
        WriteBe32(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(ms, "IHDR"u8, ihdr);
        var raw = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (1 + width * 4) + 1, width * 4);
        }

        using var z = new MemoryStream();
        using (var def = new ZLibStream(z, CompressionLevel.Fastest))
        {
            def.Write(raw);
        }

        Chunk(ms, "IDAT"u8, z.ToArray());
        Chunk(ms, "IEND"u8, []);
        return ms.ToArray();
    }

    public static byte[] Encode(MapTextureCrop crop, IReadOnlyList<ushort>? palette = null) =>
        Encode(crop.TexelWidth, crop.TexelHeight, crop.Rgba(palette));

    public static byte[] Encode(MapTextureCrop crop, MapTextureSheet sheet) =>
        Encode(crop.TexelWidth, crop.TexelHeight, crop.Rgba(sheet));

    private static void Chunk(Stream dest, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> be = stackalloc byte[4];
        WriteBe32(be, 0, (uint)data.Length);
        dest.Write(be);
        dest.Write(type);
        dest.Write(data);
        WriteBe32(be, 0, Crc32(type, data));
        dest.Write(be);
    }

    private static void WriteBe32(Span<byte> dest, int off, uint value)
    {
        dest[off] = (byte)(value >> 24);
        dest[off + 1] = (byte)(value >> 16);
        dest[off + 2] = (byte)(value >> 8);
        dest[off + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

/// <summary>
/// Writes original TIM crops the same way the HD extract wrote atlas frames:
/// <c>%AppData%\GrandiaExtract\{Stem}\Tim\{Kind}\{i:D4}.png</c>.
/// </summary>
public static class MapTextureExtract
{
    public static string Root(string stem, string? root = null) =>
        Path.Combine(
            root ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GrandiaExtract"),
            stem,
            "Tim");

    public static string Write(Map map, string? root = null)
    {
        var dest = Root(map.Stem, root);
        Directory.CreateDirectory(dest);
        var sheet = map.Textures.Sheet;
        var lines = new List<string> { $"# {map.Stem} original TIM" };
        WriteKind(dest, "Anim", map.Sprites.Anim, map, sheet, lines);
        WriteKind(dest, "Tenants", map.Sprites.Tenants, map, sheet, lines);
        WriteKind(dest, "Maps", map.Sprites.Maps, map, sheet, lines);
        WriteKind(dest, "MapEff", map.Sprites.MapEff, map, sheet, lines);
        var uvDir = Path.Combine(dest, "Uv");
        var uvN = 0;
        for (var i = 0; i < map.Sprites.Uv.Count; i++)
        {
            var uv = map.Sprites.Uv[i];
            if (!sheet.TryCrop(uv, out var crop) || crop.Empty)
            {
                continue;
            }

            Directory.CreateDirectory(uvDir);
            File.WriteAllBytes(Path.Combine(uvDir, $"{uv.Block}_{i:D4}.png"), MapTexturePng.Encode(crop, sheet));
            uvN++;
        }

        var poseDir = Path.Combine(dest, "Poses");
        var poseN = 0;
        foreach (var pose in map.Poses.Items)
        {
            for (var p = 0; p < pose.Parts.Count; p++)
            {
                if (!sheet.TryCrop(pose.Parts[p], out var crop) || crop.Empty)
                {
                    continue;
                }

                Directory.CreateDirectory(poseDir);
                File.WriteAllBytes(
                    Path.Combine(poseDir, $"{pose.Index:D3}_{p}.png"),
                    MapTexturePng.Encode(crop, sheet));
                poseN++;
            }
        }

        lines.Add($"# uv={uvN} poses={poseN} occupied={sheet.Occupied}");
        File.WriteAllText(Path.Combine(dest, "matches.txt"), string.Join('\n', lines) + '\n');
        return dest;
    }

    private static void WriteKind(
        string dest,
        string name,
        MapSpriteSheet sprites,
        Map map,
        MapTextureSheet sheet,
        List<string> lines)
    {
        var dir = Path.Combine(dest, name);
        var n = 0;
        foreach (var sprite in sprites.Items)
        {
            if (!map.TryCrop(sprite, out var crop) || crop.Empty)
            {
                lines.Add($"[{sprite.Index:D4}] {name} miss key={sprite.Key:X16}");
                continue;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{sprite.Index:D4}.png"), MapTexturePng.Encode(crop, sheet));
            n++;
            lines.Add(
                $"[{sprite.Index:D4}] {name}/{sprite.Index:D4}.png  uv={crop.U},{crop.V} {crop.TexelWidth}x{crop.TexelHeight} tpage=0x{crop.Tpage:X}");
        }

        lines.Add($"# {name} {n}/{sprites.Count}");
    }
}
