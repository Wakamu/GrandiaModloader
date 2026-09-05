namespace Grandia.Sdk;

/// <summary>OFS directory + SCN bank: (u16 id, u16 offset)* until id 0xFFFF.</summary>
public static class FieldScriptBank
{
    public static List<(int Id, int Offset, int End)> ParseDirectory(ReadOnlySpan<byte> ofs, int bankSize)
    {
        var entries = new List<(int Id, int Off)>();
        for (var i = 0; i + 4 <= ofs.Length; i += 4)
        {
            var id = BitConverter.ToUInt16(ofs.Slice(i, 2));
            var off = BitConverter.ToUInt16(ofs.Slice(i + 2, 2));
            if (id == 0xFFFF)
            {
                break;
            }

            entries.Add((id, off));
        }

        var sorted = entries.OrderBy(e => e.Off).ToList();
        var outb = new List<(int, int, int)>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var (id, off) = sorted[i];
            var end = i + 1 < sorted.Count ? sorted[i + 1].Off : bankSize;
            if (end < off)
            {
                end = bankSize;
            }

            outb.Add((id, off, Math.Min(end, bankSize)));
        }

        return outb;
    }

    public static bool TrySlice(byte[] scn, IReadOnlyList<(int Id, int Offset, int End)> dir, int scriptId,
        out byte[] bytes)
    {
        foreach (var (id, off, end) in dir)
        {
            if (id != scriptId || off < 0 || off >= scn.Length)
            {
                continue;
            }

            var last = Math.Min(end, scn.Length);
            if (last < off)
            {
                continue;
            }

            bytes = scn.AsSpan(off, last - off).ToArray();
            return bytes.Length > 0;
        }

        bytes = [];
        return false;
    }

    public static List<(int Id, byte[] Bytes)> ExtractAll(byte[] scn, byte[] ofs)
    {
        var list = new List<(int, byte[])>();
        foreach (var (id, off, end) in ParseDirectory(ofs, scn.Length))
        {
            if (TrySlice(scn, [(id, off, end)], id, out var bytes))
            {
                list.Add((id, bytes));
            }
        }

        return list;
    }

    public static bool TryLoadFiles(string textRoot, string stem, out byte[] scn, out byte[] ofs)
    {
        scn = [];
        ofs = [];
        if (string.IsNullOrWhiteSpace(textRoot) || string.IsNullOrWhiteSpace(stem))
        {
            return false;
        }

        stem = stem.Trim();
        scn = ReadFirst(textRoot, stem + ".SCN", stem + ".scn");
        ofs = ReadFirst(textRoot, stem + ".OFS", stem + ".ofs");
        return scn.Length > 0 && ofs.Length > 0;
    }

    public static bool TryGet(string textRoot, string stem, int scriptId, out byte[] bytes)
    {
        bytes = [];
        if (!TryLoadFiles(textRoot, stem, out var scn, out var ofs))
        {
            return false;
        }

        return TrySlice(scn, ParseDirectory(ofs, scn.Length), scriptId, out bytes);
    }

    public static List<(int Id, byte[] Bytes)> LoadAll(string textRoot, string stem)
    {
        if (!TryLoadFiles(textRoot, stem, out var scn, out var ofs))
        {
            return [];
        }

        return ExtractAll(scn, ofs);
    }

    private static byte[] ReadFirst(string root, params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        return [];
    }
}
