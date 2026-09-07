using System.Linq;
using System.Text;

namespace Grandia.Sdk;

/// <summary>
/// <c>TEXT/EN/TEXT1.BIN</c> item-name tables (sections 5–7, 511 packed
/// C strings, id = index + 1). Display and description rows start with
/// <c>0x03</c> in the file; this type strips that on read and writes it
/// back. Other sections are copied unchanged.
/// </summary>
public static class ItemTextBin
{
    public const int ItemCount = 511;
    public const int SectionCount = 9;
    public const int ItemShortSection = 5;
    public const int ItemNameSection = 6;
    public const int ItemDescSection = 7;

    public sealed class Tables
    {
        public string[] ShortNames { get; } = new string[ItemCount];
        public string[] Names { get; } = new string[ItemCount];
        public string[] Descriptions { get; } = new string[ItemCount];
    }

    public static Tables Parse(byte[] data)
    {
        var tables = new Tables();
        if (data is not { Length: >= SectionCount * 4 })
        {
            return tables;
        }

        var offs = ReadOffs(data);
        Fill(tables.ShortNames, ReadCStrings(data, offs, ItemShortSection), stripMarker: false);
        Fill(tables.Names, ReadCStrings(data, offs, ItemNameSection), stripMarker: true);
        Fill(tables.Descriptions, ReadCStrings(data, offs, ItemDescSection), stripMarker: true);
        return tables;
    }

    public static byte[] Rebuild(byte[] source, Tables tables)
    {
        if (source is not { Length: >= SectionCount * 4 } || tables is null)
        {
            return source ?? [];
        }

        var offs = ReadOffs(source);
        var parts = new byte[SectionCount][];
        for (var i = 0; i < SectionCount; i++)
        {
            var start = offs[i];
            var end = i + 1 < SectionCount ? offs[i + 1] : source.Length;
            if (start < 0 || start > source.Length || end < start || end > source.Length)
            {
                parts[i] = [];
                continue;
            }

            parts[i] = source[start..end];
        }

        parts[ItemShortSection] = Pack(tables.ShortNames, marker: false);
        parts[ItemNameSection] = Pack(tables.Names, marker: true);
        parts[ItemDescSection] = Pack(tables.Descriptions, marker: true);

        var header = SectionCount * 4;
        var dest = new byte[header + parts.Sum(p => p.Length)];
        var cursor = header;
        for (var i = 0; i < SectionCount; i++)
        {
            BitConverter.TryWriteBytes(dest.AsSpan(i * 4), (uint)cursor);
            parts[i].CopyTo(dest, cursor);
            cursor += parts[i].Length;
        }

        return dest;
    }

    /// <summary>
    /// Rewrite item string tables without changing the file length or
    /// section offsets. A section is fully repacked when the new blob
    /// fits; otherwise each slot is patched only if the new C-string
    /// fits. Longer leftovers stay vanilla.
    /// </summary>
    public static InPlaceResult PatchInPlace(byte[] source, Tables tables)
    {
        if (source is not { Length: >= SectionCount * 4 } || tables is null)
        {
            return new InPlaceResult(source ?? [], 0, 0);
        }

        var dest = (byte[])source.Clone();
        var orig = Parse(source);
        var applied = 0;
        var skipped = 0;
        PatchSection(dest, source, ItemShortSection, orig.ShortNames, tables.ShortNames,
            marker: false, ref applied, ref skipped);
        PatchSection(dest, source, ItemNameSection, orig.Names, tables.Names,
            marker: true, ref applied, ref skipped);
        PatchSection(dest, source, ItemDescSection, orig.Descriptions, tables.Descriptions,
            marker: true, ref applied, ref skipped);
        return new InPlaceResult(dest, applied, skipped);
    }

    public readonly record struct InPlaceResult(byte[] Data, int Applied, int Skipped);

    private static int[] ReadOffs(byte[] data)
    {
        var offs = new int[SectionCount];
        for (var i = 0; i < SectionCount; i++)
        {
            offs[i] = (int)BitConverter.ToUInt32(data, i * 4);
        }

        return offs;
    }

    private static List<string> ReadCStrings(byte[] data, int[] offs, int section)
    {
        var start = offs[section];
        var end = section + 1 < offs.Length ? offs[section + 1] : data.Length;
        var list = new List<string>(ItemCount);
        if (start < 0 || start >= data.Length || end < start)
        {
            return list;
        }

        end = Math.Min(end, data.Length);
        var i = start;
        while (i < end && list.Count < ItemCount)
        {
            if (data[i] == 0)
            {
                i++;
                continue;
            }

            var j = i;
            while (j < end && data[j] != 0)
            {
                j++;
            }

            list.Add(Encoding.ASCII.GetString(data, i, j - i));
            i = j + 1;
        }

        return list;
    }

    private static void Fill(string[] dest, List<string> src, bool stripMarker)
    {
        for (var i = 0; i < dest.Length; i++)
        {
            var s = i < src.Count ? src[i] : "";
            if (stripMarker && s.StartsWith('\u0003'))
            {
                s = s[1..];
            }

            dest[i] = s;
        }
    }

    private static byte[] Pack(string[] rows, bool marker)
    {
        var ms = new MemoryStream();
        foreach (var row in rows)
        {
            if (marker)
            {
                ms.WriteByte(0x03);
            }

            var text = row ?? "";
            var bytes = Encoding.ASCII.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
            ms.WriteByte(0);
        }

        return ms.ToArray();
    }

    private static byte[] Encode(string? text, bool marker)
    {
        var body = Encoding.ASCII.GetBytes(text ?? "");
        var dest = new byte[(marker ? 1 : 0) + body.Length + 1];
        var i = 0;
        if (marker)
        {
            dest[i++] = 0x03;
        }

        body.CopyTo(dest, i);
        dest[^1] = 0;
        return dest;
    }

    private static List<(int Off, int Len)> ReadSlots(byte[] data, int[] offs, int section)
    {
        var start = offs[section];
        var end = section + 1 < offs.Length ? offs[section + 1] : data.Length;
        var list = new List<(int, int)>(ItemCount);
        if (start < 0 || start >= data.Length || end < start)
        {
            return list;
        }

        end = Math.Min(end, data.Length);
        var i = start;
        while (i < end && list.Count < ItemCount)
        {
            if (data[i] == 0)
            {
                i++;
                continue;
            }

            var j = i;
            while (j < end && data[j] != 0)
            {
                j++;
            }

            var len = Math.Min(end, j + 1) - i;
            list.Add((i, len));
            i = j + 1;
        }

        return list;
    }

    private static void PatchSection(byte[] dest, byte[] source, int section, string[] original,
        string[] want, bool marker, ref int applied, ref int skipped)
    {
        var offs = ReadOffs(source);
        var start = offs[section];
        var end = section + 1 < SectionCount ? offs[section + 1] : source.Length;
        if (start < 0 || start > dest.Length || end < start || end > dest.Length)
        {
            return;
        }

        var span = end - start;
        var prefix = 0;
        while (prefix < span && source[start + prefix] == 0)
        {
            prefix++;
        }

        var budget = span - prefix;
        var chosen = FitRows(original, want, marker, budget);
        for (var i = 0; i < ItemCount; i++)
        {
            var a = original[i] ?? "";
            var b = want[i] ?? "";
            var c = chosen[i] ?? "";
            if (a == b)
            {
                continue;
            }

            if (c == b)
            {
                applied++;
            }
            else
            {
                skipped++;
            }
        }

        var packed = Pack(chosen, marker);
        if (prefix + packed.Length <= span)
        {
            Array.Clear(dest, start, span);
            packed.CopyTo(dest, start + prefix);
            return;
        }

        var slots = ReadSlots(source, offs, section);
        for (var i = 0; i < slots.Count && i < chosen.Length; i++)
        {
            var raw = Encode(chosen[i], marker);
            if (raw.Length > slots[i].Len)
            {
                continue;
            }

            raw.CopyTo(dest, slots[i].Off);
            Array.Clear(dest, slots[i].Off + raw.Length, slots[i].Len - raw.Length);
        }
    }

    private static string[] FitRows(string[] original, string[] want, bool marker, int span)
    {
        var chosen = new string[ItemCount];
        for (var i = 0; i < ItemCount; i++)
        {
            chosen[i] = original[i] ?? "";
        }

        var packed = Pack(chosen, marker);
        if (packed.Length > span)
        {
            return chosen;
        }

        var used = packed.Length;
        var order = new List<int>(ItemCount);
        for (var i = 0; i < ItemCount; i++)
        {
            var next = want[i] ?? "";
            if (next == chosen[i])
            {
                continue;
            }

            order.Add(i);
        }

        order.Sort((a, b) =>
        {
            var da = Encode(want[a], marker).Length - Encode(chosen[a], marker).Length;
            var db = Encode(want[b], marker).Length - Encode(chosen[b], marker).Length;
            return da.CompareTo(db);
        });

        foreach (var i in order)
        {
            var next = Encode(want[i], marker);
            var cur = Encode(chosen[i], marker);
            var n = used - cur.Length + next.Length;
            if (n > span)
            {
                continue;
            }

            chosen[i] = want[i] ?? "";
            used = n;
        }

        return chosen;
    }
}
