namespace Grandia.Sdk;

/// <summary>
/// One 20-byte enemy group at encounter +0x10 (stride 0x14).
/// <see cref="SpeciesIndex"/> selects <c>SpeciesMap[index]</c> (the form-row).
/// <see cref="Count"/> is how many of that species spawn in this group.
/// Mixed fights are several of these; <see cref="BattleLoadEvent.SetEnemies"/>
/// takes one <see cref="EnemyGroup"/> per independent body. Tentacles are
/// extra pairs on the body slot, not their own group.
/// </summary>
public sealed class EncounterSlot
{
    public const int Size = 20;
    public const int RecordOff = 16;
    public const int MaxCount = 4;
    public const int DumpSize = RecordOff + Size * MaxCount;

    private readonly byte[] _enc;
    private readonly int _off;

    internal EncounterSlot(byte[] encounter, int index)
    {
        _enc = encounter;
        _off = RecordOff + index * Size;
        if (_off < 0 || _off + Size > encounter.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// Byte 0. Packed as <c>(value+1)&amp;7</c> into ctx+e668 (ring lane).
    /// Stage-local — Marna 6 is not a ground lane on E010.
    /// </summary>
    public int Placement
    {
        get => _enc[_off];
        set => _enc[_off] = ClampByte(value);
    }

    /// <summary>Byte 1. Copied to ctx+e670. Usually 0.</summary>
    public int Aux
    {
        get => _enc[_off + 1];
        set => _enc[_off + 1] = ClampByte(value);
    }

    /// <summary>
    /// U16 at +2. Stock dumps: Bugs 9, Centipedes 0x17. Not read by the +12BF80
    /// spawn loop; kept so a rewritten slot still matches a captured fight.
    /// </summary>
    public int Kind
    {
        get => _enc[_off + 2] | (_enc[_off + 3] << 8);
        set => WriteU16(_off + 2, value);
    }

    /// <summary>
    /// Byte 4. Index into <see cref="BattleLoadEvent.SpeciesMap"/> / ctx+0x64a07.
    /// That remap byte is the form-row (Marna Centipede: index 1 → 0x69).
    /// Skill/anim tables are keyed by this catalog index — keep a local
    /// monster on the slot the map already assigned (E010's 0x22 is index 4).
    /// Pair 0 of <see cref="SetPair"/>.
    /// </summary>
    public int SpeciesIndex
    {
        get => _enc[_off + 4];
        set => _enc[_off + 4] = ClampByte(value);
    }

    /// <summary>
    /// Count of pair 0 (byte +12). Extra species in this group use
    /// <see cref="SetPair"/>.
    /// </summary>
    public int Count
    {
        get => _enc[_off + 12];
        set => _enc[_off + 12] = ClampByte(value);
    }

    /// <summary>
    /// One of the 8 (catalog-index, count) pairs at +4 / +12. Pair 0 is
    /// <see cref="SpeciesIndex"/> / <see cref="Count"/>. Attached parts belong
    /// on pairs 1+ of the body group, not their own 20-byte slot.
    /// </summary>
    public void SetPair(int pair, int speciesIndex, int count)
    {
        if ((uint)pair >= 8u)
        {
            return;
        }

        _enc[_off + 4 + pair] = ClampByte(speciesIndex);
        _enc[_off + 12 + pair] = ClampByte(count);
    }

    internal static byte ClampByte(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > 255 ? (byte)255 : (byte)value;
    }

    private void WriteU16(int off, int value)
    {
        if (value < 0)
        {
            value = 0;
        }

        if (value > 65535)
        {
            value = 65535;
        }

        _enc[off] = (byte)value;
        _enc[off + 1] = (byte)(value >> 8);
    }
}
