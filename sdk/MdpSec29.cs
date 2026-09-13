namespace Grandia.Sdk;

/// <summary>
/// MDP sec[29] positional SFX table: 8-byte header + 16-byte rows until
/// id <c>0xFF</c>. Same layout the field mixer copies onto the heap.
/// </summary>
public static class MdpSec29
{
    public const int HeaderSize = 8;
    public const int RowSize = 16;

    /// <summary>Field heap copy is 1024 bytes (<c>0x200</c> words).</summary>
    public const int HeapSize = 1024;

    /// <summary>Live rows that fit before the <c>0xFF</c> terminator (62).</summary>
    public const int MaxLive = (HeapSize - HeaderSize) / RowSize - 1;

    public static MapSfxTable Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < HeaderSize)
        {
            return new MapSfxTable();
        }

        var flags = BitConverter.ToInt32(blob);
        var range = BitConverter.ToInt32(blob.Slice(4));
        if (range < 0)
        {
            range = 0;
        }

        var rows = new List<MapSfx>();
        var off = HeaderSize;
        while (off + RowSize <= blob.Length)
        {
            var rec = blob.Slice(off, RowSize);
            var id = rec[0];
            if (id == 0xFF)
            {
                break;
            }

            rows.Add(MapSfx.FromRaw(rows.Count, rec.ToArray()));
            off += RowSize;
        }

        return new MapSfxTable(flags, range, rows);
    }

    /// <summary>Rebuild sec[29] from the table (skips <see cref="MapSfx.Removed"/>).</summary>
    public static byte[] Emit(MapSfxTable table)
    {
        var live = table.Items.Where(e => !e.Removed).ToList();
        if (live.Count > MaxLive)
        {
            live = live.Take(MaxLive).ToList();
        }

        var blob = new byte[HeaderSize + (live.Count + 1) * RowSize];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 4), table.Flags);
        BitConverter.TryWriteBytes(blob.AsSpan(4, 4), table.Range);
        var off = HeaderSize;
        foreach (var row in live)
        {
            WriteRow(blob.AsSpan(off, RowSize), row);
            off += RowSize;
        }

        blob[off] = 0xFF;
        return blob;
    }

    public static byte[] Apply(MapSfxTable table) => Emit(table);

    internal static void WriteRow(Span<byte> dest, MapSfx row)
    {
        dest[0] = (byte)MapSfx.ClampId(row.Id);
        dest[1] = 0xFF;
        dest[2] = (byte)(row.Kind == 0 ? 0x81 : MapSfx.ClampByte(row.Kind));
        dest[3] = (byte)MapSfx.ClampByte(row.Sfx);
        dest[4] = (byte)MapSfx.ClampByte(row.Flags);
        dest[5] = (byte)MapSfx.ClampByte(row.Period);
        dest[6] = 0xFF;
        dest[7] = (byte)MapSfx.ClampByte(row.Bias);
        WriteS16(dest.Slice(0xA), row.X);
        WriteS16(dest.Slice(0xC), row.Y);
        WriteS16(dest.Slice(0xE), row.Z);
    }

    private static void WriteS16(Span<byte> dest, int value)
    {
        var w = (ushort)(short)MapSfx.ClampCoord(value);
        dest[0] = (byte)(w & 0xFF);
        dest[1] = (byte)(w >> 8);
    }
}
