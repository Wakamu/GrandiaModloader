namespace Grandia.Sdk;

/// <summary>
/// MDP sec[21] anim clip directory. Bound as a pointer into the fopen heap
/// at <c>+0x54A9C</c> → <c>[0x71CAE0]</c>. Header is a u16 count (high word
/// 0 on stock), then <c>{u16 id, u16 flags, u32 off_a, u32 off_b}</c>.
/// <c>flags</c> is the Stream A frame count (packed into <c>71A746</c>).
/// Stream A: <c>u16</c> header (low byte 0 = no turn) + N × s16 XYZ.
/// Stream B: <c>u16</c> count + <c>{u16 frame, u16 cmd}</c> where
/// <c>cmd&gt;&gt;14</c> is 0/1/2 = <c>call_hook</c> table 1/2/3,
/// 3 = delay <c>cmd&amp;0x3FFF</c> ticks. Both offsets are table-relative.
/// Empty Stream B is a count-0 stub, not <c>off_b=0</c> (latch would then
/// read the directory as cues).
/// </summary>
public static class MdpSec21
{
    public const int HeaderSize = 4;
    public const int RowSize = 12;

    public static MapAnimTable Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 2)
        {
            return new MapAnimTable();
        }

        var n = (int)BitConverter.ToUInt16(blob);
        if (n == 0)
        {
            return new MapAnimTable();
        }

        var dirEnd = HeaderSize + n * RowSize;
        if (dirEnd > blob.Length)
        {
            n = blob.Length >= HeaderSize ? (blob.Length - HeaderSize) / RowSize : 0;
            dirEnd = HeaderSize + n * RowSize;
            if (n == 0 || dirEnd > blob.Length)
            {
                return new MapAnimTable();
            }
        }

        var offs = new List<int>();
        var rows = new List<(int Id, int Flags, int OffA, int OffB)>(n);
        var cursor = HeaderSize;
        for (var i = 0; i < n; i++)
        {
            var id = BitConverter.ToUInt16(blob.Slice(cursor));
            var flags = BitConverter.ToUInt16(blob.Slice(cursor + 2));
            var offA = BitConverter.ToInt32(blob.Slice(cursor + 4));
            var offB = BitConverter.ToInt32(blob.Slice(cursor + 8));
            rows.Add((id, flags, offA, offB));
            Collect(offs, offA, blob.Length);
            Collect(offs, offB, blob.Length);
            cursor += RowSize;
        }

        offs.Sort();
        var items = new List<MapAnim>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            items.Add(new MapAnim(
                i,
                row.Id,
                row.Flags,
                Slice(blob, row.OffA, offs),
                Slice(blob, row.OffB, offs)));
        }

        return new MapAnimTable(items);
    }

    /// <summary>
    /// Rebuild sec[21] with rewritten offsets. The host mallocs this blob
    /// and swaps <c>[0x71CAE0]</c> after bind.
    /// </summary>
    public static byte[] Emit(MapAnimTable table)
    {
        var live = table.Items.Where(e => !e.Removed).ToList();
        var streams = new List<byte[]>(live.Count * 2);
        var recs = new List<(int Id, int Flags, int OffA, int OffB)>(live.Count);
        var cursor = HeaderSize + live.Count * RowSize;
        foreach (var row in live)
        {
            var a = row.StreamARaw;
            var b = row.StreamBRaw;
            var offA = 0;
            var offB = 0;
            if (a.Length > 0)
            {
                offA = cursor;
                streams.Add(a);
                cursor += a.Length;
            }

            // Latch does stream_b = base+off_b even when off_b is 0, so the
            // directory is read as cues. A real count-0 stream skips that.
            if (b.Length == 0)
            {
                b = [0, 0];
            }

            offB = cursor;
            streams.Add(b);
            cursor += b.Length;

            recs.Add((row.Id, row.Flags, offA, offB));
        }

        var blob = new byte[cursor];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 2), (ushort)live.Count);
        var off = HeaderSize;
        foreach (var rec in recs)
        {
            BitConverter.TryWriteBytes(blob.AsSpan(off, 2), (ushort)MapAnim.ClampU16(rec.Id));
            BitConverter.TryWriteBytes(blob.AsSpan(off + 2, 2), (ushort)MapAnim.ClampU16(rec.Flags));
            BitConverter.TryWriteBytes(blob.AsSpan(off + 4, 4), rec.OffA);
            BitConverter.TryWriteBytes(blob.AsSpan(off + 8, 4), rec.OffB);
            off += RowSize;
        }

        foreach (var stream in streams)
        {
            stream.CopyTo(blob.AsSpan(off));
            off += stream.Length;
        }

        return blob;
    }

    public static byte[] Apply(MapAnimTable table) => Emit(table);

    private static void Collect(List<int> offs, int off, int len)
    {
        if (off > 0 && off < len && !offs.Contains(off))
        {
            offs.Add(off);
        }
    }

    private static byte[] Slice(ReadOnlySpan<byte> blob, int off, List<int> sorted)
    {
        if (off <= 0 || off >= blob.Length)
        {
            return [];
        }

        var next = blob.Length;
        foreach (var o in sorted)
        {
            if (o > off)
            {
                next = o;
                break;
            }
        }

        return blob.Slice(off, next - off).ToArray();
    }
}
