namespace Grandia.Sdk;

public readonly struct MapId : IEquatable<MapId>
{
    public MapId(ushort value) => Value = value;

    public ushort Value { get; }

    public static MapId Parse(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            return default;
        }

        var hex = stem.Trim();
        var dot = hex.IndexOf('.');
        if (dot > 0)
        {
            hex = hex[..dot];
        }

        return ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? new MapId(value)
            : default;
    }

    public bool Equals(MapId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is MapId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(MapId a, MapId b) => a.Equals(b);

    public static bool operator !=(MapId a, MapId b) => !a.Equals(b);

    public override string ToString() => Value.ToString("X4");
}
