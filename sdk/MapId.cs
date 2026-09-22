namespace Grandia.Sdk;

public readonly struct MapId : IEquatable<MapId>
{
    public MapId(ushort value) => Value = value;

    public MapId(Maps map) => Value = (ushort)map;

    public ushort Value { get; }

    /// <summary>True when <see cref="Value"/> is a named <see cref="Maps"/> stem.</summary>
    public bool IsKnown => Enum.IsDefined((Maps)Value);

    /// <summary>Named stem, or null when the id is not in <see cref="Maps"/>.</summary>
    public Maps? Known => IsKnown ? (Maps)Value : null;

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

    public static bool operator ==(MapId id, Maps map) => id.Value == (ushort)map;

    public static bool operator !=(MapId id, Maps map) => id.Value != (ushort)map;

    public static bool operator ==(Maps map, MapId id) => id == map;

    public static bool operator !=(Maps map, MapId id) => id != map;

    public static implicit operator MapId(Maps map) => new(map);

    public static explicit operator Maps(MapId id) => (Maps)id.Value;

    public override string ToString() => Value.ToString("X4");
}
