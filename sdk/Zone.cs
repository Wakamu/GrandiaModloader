namespace Grandia.Sdk;

public sealed class Zone
{
    private string? _line;

    public Zone(int dest)
    {
        Dest = dest;
    }

    /// <summary>Table-1 row index (0-based).</summary>
    public int Index { get; internal set; }

    /// <summary>Byte 0 of the row (often 0).</summary>
    public int Id { get; internal set; }

    /// <summary>Setup-warp dest map, or 0 when this row is not handler 0x02.</summary>
    public int Dest { get; }

    public int Handler { get; internal set; }

    public ZoneBox Aabb { get; internal set; }

    public byte[]? Raw { get; internal set; }

    /// <summary>Full assembler <c>zone …</c> line, without the replace/add prefix.</summary>
    public string? Line
    {
        get => _line;
        set
        {
            _line = value;
            Dirty = true;
        }
    }

    public bool Dirty { get; set; }

    public bool Append { get; set; }

    /// <summary>Drop this table-1 row on the next sec[7] emit.</summary>
    public bool Removed { get; set; }

    public void Remove()
    {
        Removed = true;
        Dirty = true;
    }

    internal void HydrateLine(string line)
    {
        _line = line;
        Dirty = false;
    }

    public override string ToString() =>
        !string.IsNullOrWhiteSpace(_line) ? _line! : $"zone {Id} dest=0x{Dest:X}";

    internal static Zone FromRaw(int index, byte[] raw)
    {
        var dest = SetupDestOrZero(raw);
        var zone = new Zone(dest)
        {
            Index = index,
            Id = raw.Length > 0 ? raw[0] : 0,
            Handler = raw.Length > 1 ? raw[1] & 0x3F : 0,
            Aabb = raw.Length >= FieldHookAsm.ZoneRowSize ? FieldHookAsm.ReadAabb(raw) : default,
            Raw = raw,
        };
        zone.HydrateLine(FieldHookAsm.FormatZone(raw));
        return zone;
    }

    private static int SetupDestOrZero(byte[] raw)
    {
        var dest = FieldHookAsm.SetupDest(raw);
        return dest < 0 ? 0 : dest;
    }
}
