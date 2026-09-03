namespace Grandia.Sdk;

/// <summary>
/// Battle setup at +0x12B070, before table dispatch (+0x12B2FD) and B00x stage/BGM load.
/// Set <see cref="EncounterTable"/> / <see cref="SetEncounter"/> for stage and group
/// shape. Species form-row is <see cref="BattleLoadEvent.SetEnemies"/>. Leave
/// <see cref="EncounterRow"/> alone — it is the field wanderer group.
/// </summary>
public sealed class BattleSetupEvent
{
    public const int EncounterDumpSize = EncounterSlot.DumpSize;
    public const int EncounterSlotOff = EncounterSlot.RecordOff;
    public const int EncounterSlotSize = EncounterSlot.Size;

    public BattleSetupEvent(int formation = 0, MapId map = default, MapId dest = default, int spawn = 0,
        int battleMode = 0, byte[]? encounter = null)
    {
        Formation = formation;
        Map = map;
        Dest = dest;
        Spawn = spawn;
        BattleMode = battleMode;
        Encounter = new byte[EncounterDumpSize];
        if (encounter is { Length: > 0 })
        {
            Array.Copy(encounter, Encounter, Math.Min(EncounterDumpSize, encounter.Length));
        }

        From = new MapId(unchecked((ushort)spawn));
    }

    /// <summary>Battle-ctx formation / P_DAT pack index (<c>ctx[0x243]</c>).</summary>
    public int Formation { get; }

    /// <summary>Field map id (MapObj+8).</summary>
    public MapId Map { get; }

    /// <summary>Travel dest word (MapObj+0x5B2).</summary>
    public MapId Dest { get; }

    /// <summary>MapObj+0x5B4. Previous field map in battle (0 if none).</summary>
    public int Spawn { get; }

    /// <summary>Previous field as a map id (<see cref="Spawn"/>).</summary>
    public MapId From { get; }

    /// <summary>0 field, 2 battle load, 3 combat. Often still 0 at this hook.</summary>
    public int BattleMode { get; }

    /// <summary>
    /// Header plus up to <see cref="EncounterSlot.MaxCount"/> groups of the live
    /// encounter record (cached at 0x713B84). The host writes table (byte 1),
    /// approach (byte 11), group count, and every group. Byte 13 (field row)
    /// is never written. Species form-row is <see cref="BattleLoadEvent.SetEnemies"/>.
    /// </summary>
    public byte[] Encounter { get; }

    /// <summary>
    /// Encounter table id (<c>Encounter[1]</c>). Selects the B00x stage name and a
    /// small ctx setup fn — not the monster catalog. Species come from slot bytes
    /// into <c>ctx+0x64a07</c> (M_DAT), which is already the current map's list.
    /// </summary>
    public int EncounterTable
    {
        get => Encounter[1];
        set => Encounter[1] = ClampByte(value);
    }

    /// <summary>
    /// Field wanderer group (<c>Encounter[13]</c>). Read-only here so post-fight
    /// despawn still matches the group you touched.
    /// </summary>
    public int EncounterRow => Encounter[13];

    /// <summary>How many 20-byte groups follow the header (<c>Encounter[6]</c>).</summary>
    public int GroupCount
    {
        get => Encounter[6];
        set => Encounter[6] = ClampByte(value);
    }

    /// <summary>View of group <paramref name="index"/> (0 .. <see cref="EncounterSlot.MaxCount"/>-1).</summary>
    public EncounterSlot Slot(int index) => new(Encounter, index);

    /// <summary>Normal / initiative / ambush (<c>Encounter[11]</c>). Consumed at +0x12B57F.</summary>
    public BattleApproach Approach
    {
        get => (BattleApproach)Encounter[11];
        set => Encounter[11] = (byte)value;
    }

    /// <summary>
    /// One group on this fight: table (B00x stage), count, placement, kind, and
    /// species-map index. Does not change <see cref="EncounterRow"/>. Species
    /// form-row is <see cref="BattleLoadEvent.SetEnemies"/> / <see cref="BattleLoadEvent.SpeciesMap"/>.
    /// </summary>
    public void SetEncounter(int table, int count, int speciesIndex = 1, int placement = 0, int kind = 0)
    {
        EncounterTable = table;
        GroupCount = 1;
        Array.Clear(Encounter, EncounterSlotOff, EncounterSlotSize);
        var g = Slot(0);
        g.Placement = placement;
        g.Aux = 0;
        g.Kind = kind;
        g.SpeciesIndex = speciesIndex;
        g.Count = count;
    }

    /// <summary>Raw 20-byte group at +0x10. Prefer <see cref="Slot"/> / <see cref="SetEncounter"/>.</summary>
    public void SetEncounterSlot(int index, byte[] data)
    {
        if (data is null || index < 0)
        {
            return;
        }

        var off = EncounterSlotOff + index * EncounterSlotSize;
        if (off + EncounterSlotSize > Encounter.Length)
        {
            return;
        }

        Array.Clear(Encounter, off, EncounterSlotSize);
        Array.Copy(data, 0, Encounter, off, Math.Min(EncounterSlotSize, data.Length));
    }

    private static byte ClampByte(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        return value > 255 ? (byte)255 : (byte)value;
    }
}
