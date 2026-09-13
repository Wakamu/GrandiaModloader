namespace Grandia.Sdk;

/// <summary>
/// MDP sec[8] field instances: packed count + 48-byte rows + 2-byte pad.
/// Copied as 4 KiB onto <c>[0x63FA4C]</c> at <c>+0x61140</c>.
/// </summary>
public static class MdpSec8
{
    public const int RowSize = 48;
    public const int PackedFlag = 0x4000;

    /// <summary>Field heap copy is 0x800 words (4 KiB).</summary>
    public const int HeapSize = 4096;

    /// <summary>Rows that fit in the heap: (4096 − 4) / 48.</summary>
    public const int MaxLive = (HeapSize - 4) / RowSize;

    public static bool IsTown(int kind, int talkId) =>
        talkId != 0 && ((kind & 0xF) is 0 or 4);

    public static MapNpcTable Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 2)
        {
            return new MapNpcTable();
        }

        var n = BitConverter.ToUInt16(blob) & 0x3FFF;
        var rows = new List<MapNpc>(n);
        var off = 2;
        for (var i = 0; i < n; i++)
        {
            if (off + RowSize > blob.Length)
            {
                break;
            }

            rows.Add(MapNpc.FromRaw(i, blob.Slice(off, RowSize)));
            off += RowSize;
        }

        return new MapNpcTable(rows);
    }

    /// <summary>
    /// Rebuild sec[8] from every instance (skips <see cref="MapNpc.Removed"/>).
    /// Kind-2 wanderers and party rows that were not removed stay in the blob.
    /// </summary>
    public static byte[] Emit(MapNpcTable table)
    {
        var live = table.Instances.Where(e => !e.Removed).ToList();
        if (live.Count > MaxLive)
        {
            live = live.Take(MaxLive).ToList();
        }

        var blob = new byte[4 + live.Count * RowSize];
        BitConverter.TryWriteBytes(blob.AsSpan(0, 2), (ushort)(PackedFlag | live.Count));
        var off = 2;
        foreach (var row in live)
        {
            row.ToRaw().CopyTo(blob.AsSpan(off, RowSize));
            off += RowSize;
        }

        return blob;
    }

    public static byte[] Apply(MapNpcTable table) => Emit(table);
}
