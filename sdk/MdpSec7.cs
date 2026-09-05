namespace Grandia.Sdk;

/// <summary>
/// Parse / emit MDP sec[7] hook tables. Table 1 is 32-byte zones; tables 2/3
/// are 20-byte <c>call_hook</c> rows. Rebuilt size must stay ≤
/// <see cref="HeapBudget"/>.
/// </summary>
public sealed class MdpSec7
{
    public const int HeaderSize = 0x18;
    public const int HeapBudget = 0x4000;
    public static readonly int[] RowSizes = [0, 32, 20, 20];

    public int Flags { get; set; }
    public int Count0 { get; set; }
    public byte[] OpaquePrefix { get; set; } = [];
    public List<byte[]>[] Tables { get; } = [[], [], [], []];

    public List<byte[]> Zones => Tables[1];
    public List<byte[]> Hooks => Tables[2];
    public List<byte[]> AltHooks => Tables[3];

    public static MdpSec7 Parse(ReadOnlySpan<byte> sec7)
    {
        if (sec7.Length < HeaderSize)
        {
            throw new ArgumentException("sec[7] too short");
        }

        var flags = sec7[0];
        var counts = new[] { sec7[1], sec7[2], sec7[3], sec7[4] };
        var rels = new int[4];
        for (var i = 0; i < 4; i++)
        {
            rels[i] = BitConverter.ToInt32(sec7.Slice(8 + i * 4));
        }

        var packingRels = new List<int>();
        for (var ti = 1; ti <= 3; ti++)
        {
            if (counts[ti] != 0 && RowSizes[ti] != 0)
            {
                packingRels.Add(rels[ti]);
            }
        }

        var packingStart = packingRels.Count > 0 ? packingRels.Min() : sec7.Length;
        if (packingStart < HeaderSize)
        {
            throw new ArgumentException("invalid packing start");
        }

        var parsed = new MdpSec7
        {
            Flags = flags,
            Count0 = counts[0],
            OpaquePrefix = sec7.Slice(HeaderSize, packingStart - HeaderSize).ToArray(),
        };
        for (var ti = 1; ti <= 3; ti++)
        {
            var cnt = counts[ti];
            var rel = rels[ti];
            var rs = RowSizes[ti];
            if (cnt == 0 || rs == 0)
            {
                continue;
            }

            var end = rel + cnt * rs;
            if (end > sec7.Length)
            {
                throw new ArgumentException($"table{ti} truncated");
            }

            for (var i = 0; i < cnt; i++)
            {
                parsed.Tables[ti].Add(sec7.Slice(rel + i * rs, rs).ToArray());
            }
        }

        return parsed;
    }

    public byte[] Emit()
    {
        var counts = new[] { Count0 & 0xFF, 0, 0, 0 };
        for (var ti = 1; ti <= 3; ti++)
        {
            var rs = RowSizes[ti];
            foreach (var raw in Tables[ti])
            {
                if (raw.Length != rs)
                {
                    throw new ArgumentException($"table{ti} row length {raw.Length} != {rs}");
                }
            }

            counts[ti] = Tables[ti].Count;
            if (counts[ti] > 255)
            {
                throw new ArgumentException($"table{ti} count {counts[ti]} exceeds u8");
            }
        }

        var packingStart = HeaderSize + OpaquePrefix.Length;
        var rels = new int[4];
        rels[0] = Count0 != 0 ? 0x20 : 0;
        var body = new List<byte>();
        var cursor = packingStart;
        for (var ti = 1; ti <= 3; ti++)
        {
            var rs = RowSizes[ti];
            if (Tables[ti].Count == 0)
            {
                rels[ti] = 0;
                continue;
            }

            rels[ti] = cursor;
            foreach (var raw in Tables[ti])
            {
                body.AddRange(raw);
                cursor += rs;
            }
        }

        var outp = new byte[HeaderSize + OpaquePrefix.Length + body.Count];
        outp[0] = (byte)(Flags & 0xFF);
        for (var i = 0; i < 4; i++)
        {
            outp[1 + i] = (byte)(counts[i] & 0xFF);
            BitConverter.TryWriteBytes(outp.AsSpan(8 + i * 4), rels[i]);
        }

        OpaquePrefix.CopyTo(outp, HeaderSize);
        body.CopyTo(outp, packingStart);
        if (outp.Length > HeapBudget)
        {
            throw new ArgumentException(
                $"rebuilt sec[7] 0x{outp.Length:X} exceeds heap budget 0x{HeapBudget:X}");
        }

        return outp;
    }

    /// <summary>Apply dirty <see cref="Map.Hooks"/> / <see cref="Map.Zones"/> onto a vanilla sec[7].</summary>
    public static byte[] Apply(ReadOnlySpan<byte> vanilla, Map map)
    {
        var tables = Parse(vanilla);
        var zoneRows = tables.Zones.ToList();
        foreach (var zone in map.Zones.Items.Where(z => z.Dirty && !z.Removed && !z.Append
                     && !string.IsNullOrWhiteSpace(z.Line)))
        {
            var raw = FieldHookAsm.AssembleZone(FieldHookAsm.NormalizeZoneLine(zone.Line!));
            var at = zone.Index;
            if (at < 0 || at >= zoneRows.Count)
            {
                at = MatchZone(zoneRows, zone.Dest);
            }

            zoneRows[at] = raw;
        }

        foreach (var zone in map.Zones.Items.Where(z => z.Dirty && z.Removed && !z.Append)
                     .OrderByDescending(z => z.Index))
        {
            if (zone.Index >= 0 && zone.Index < zoneRows.Count)
            {
                zoneRows.RemoveAt(zone.Index);
            }
        }

        foreach (var zone in map.Zones.Items.Where(z => z.Dirty && z.Append && !z.Removed
                     && !string.IsNullOrWhiteSpace(z.Line)))
        {
            if (zoneRows.Count >= 255)
            {
                throw new InvalidOperationException("table 1 count would exceed 255");
            }

            zoneRows.Add(FieldHookAsm.AssembleZone(FieldHookAsm.NormalizeZoneLine(zone.Line!)));
        }

        tables.Zones.Clear();
        tables.Zones.AddRange(zoneRows);

        foreach (var hook in map.Hooks.Items.Where(h => h.Dirty && !string.IsNullOrWhiteSpace(h.Line)))
        {
            var raw = FieldHookAsm.AssembleHook(hook.Line!, hook.Id);
            if (hook.Append)
            {
                if (tables.Hooks.Count >= 255)
                {
                    throw new InvalidOperationException("table 2 count would exceed 255");
                }

                tables.Hooks.Add(raw);
            }
            else
            {
                tables.Hooks[MatchHook(tables.Hooks, hook.Id)] = raw;
            }
        }

        return tables.Emit();
    }

    private static int MatchZone(IReadOnlyList<byte[]> rows, int dest)
    {
        var hits = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (FieldHookAsm.SetupDest(rows[i]) == dest)
            {
                hits.Add(i);
            }
        }

        if (hits.Count == 0)
        {
            throw new InvalidOperationException($"no table-1 row matches dest=0x{dest:X}");
        }

        if (hits.Count > 1)
        {
            throw new InvalidOperationException(
                $"{hits.Count} table-1 rows match dest=0x{dest:X}; add aabb= to disambiguate");
        }

        return hits[0];
    }

    private static int MatchHook(IReadOnlyList<byte[]> rows, int hookId)
    {
        var hits = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Length > 0 && rows[i][0] == hookId)
            {
                hits.Add(i);
            }
        }

        if (hits.Count == 0)
        {
            throw new InvalidOperationException($"no table-2 hook id {hookId}");
        }

        if (hits.Count > 1)
        {
            throw new InvalidOperationException($"{hits.Count} table-2 rows have id {hookId}");
        }

        return hits[0];
    }
}
