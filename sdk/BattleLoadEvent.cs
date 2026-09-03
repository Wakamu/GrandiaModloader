namespace Grandia.Sdk;

/// <summary>
/// Battle context init at +0x12BDD0, before ally pack/spawn.
/// Background and battle BGM are already playing — this event cannot change them.
/// <see cref="Party"/> starts as the field roster (or <see cref="GameParty.SetIds"/> if set).
/// Mutate it for this fight only. <see cref="SetEnemies"/> (or
/// <see cref="Slot"/> + <see cref="SpeciesMap"/>) is written back before
/// stock pack/spawn. Leave <see cref="EncounterRow"/> alone — it is the
/// field wanderer group; changing it skips post-fight despawn.
/// The host snapshots field MapObj+0x0A and restores it after combat.
/// </summary>
public sealed class BattleLoadEvent
{
    public const int EncounterDumpSize = EncounterSlot.DumpSize;
    public const int EncounterSlotOff = EncounterSlot.RecordOff;
    public const int EncounterSlotSize = EncounterSlot.Size;

    public BattleLoadEvent(int[] party, int formation = 0, MapId map = default, MapId dest = default,
        int spawn = 0, int battleMode = 0, byte[]? encounter = null, int packW0 = 0, int packW2 = 0,
        int packW4 = 0, int packW6 = 0, byte[]? speciesMap = null)
    {
        Party = new PartyRoster(party);
        Formation = formation;
        Map = map;
        Dest = dest;
        Spawn = spawn;
        BattleMode = battleMode;
        PackW0 = packW0;
        PackW2 = packW2;
        PackW4 = packW4;
        PackW6 = packW6;
        Encounter = new byte[EncounterDumpSize];
        if (encounter is { Length: > 0 })
        {
            Array.Copy(encounter, Encounter, Math.Min(EncounterDumpSize, encounter.Length));
        }

        SpeciesMap = new byte[16];
        if (speciesMap is { Length: > 0 })
        {
            Array.Copy(speciesMap, SpeciesMap, Math.Min(16, speciesMap.Length));
        }

        From = new MapId(unchecked((ushort)spawn));
    }

    public PartyRoster Party { get; }

    /// <summary>Battle-ctx formation / P_DAT pack index (<c>ctx[0x243]</c>).</summary>
    public int Formation { get; }

    /// <summary>Field map id (MapObj+8).</summary>
    public MapId Map { get; }

    /// <summary>Travel dest word (MapObj+0x5B2), often 0 in random fights.</summary>
    public MapId Dest { get; }

    /// <summary>
    /// MapObj+0x5B4. In battle this is the previous field map (0 if none), not an OnMapLoad spawn index.
    /// </summary>
    public int Spawn { get; }

    /// <summary>Previous field as a map id (<see cref="Spawn"/>).</summary>
    public MapId From { get; }

    /// <summary>0 field, 2 battle load, 3 combat. Often still 0 at this hook.</summary>
    public int BattleMode { get; }

    /// <summary>P_DAT form-row words at ctx+0x7A200 (w0, w2, w4, w6).</summary>
    public int PackW0 { get; }

    public int PackW2 { get; }

    public int PackW4 { get; }

    public int PackW6 { get; }

    /// <summary>
    /// Header plus up to <see cref="EncounterSlot.MaxCount"/> groups of the live
    /// encounter record (cached at 0x713B84). Bytes 0-15: header (byte 1 table,
    /// 3 formation, 11 approach, 13 row). Bytes 16+: 20-byte groups consumed by
    /// <c>+0x12BDD0</c> (<c>+0x12BF80</c> loop). The host writes group count
    /// (byte 6) and every group. Header row stays so the field group despawns.
    /// </summary>
    public byte[] Encounter { get; }

    /// <summary>
    /// First 16 bytes of <c>ctx+0x64a07</c> (M_DAT species remap). Slot byte [4]
    /// indexes this table to a form-row. Per-map: Marna Centipede is
    /// <c>SpeciesMap[1]=0x69</c>, E010's [1] is 0x39. Written back at this event
    /// (after M_DAT copy). Do not write this at <see cref="IMod.OnBattleSetup"/>.
    /// </summary>
    public byte[] SpeciesMap { get; }

    /// <summary>Encounter table id (<c>Encounter[1]</c>). Shared by rooms in the same area.</summary>
    public int EncounterTable
    {
        get => Encounter[1];
        set => Encounter[1] = ClampByte(value);
    }

    /// <summary>
    /// Rolled row in that table (<c>Encounter[13]</c>). Identifies the field
    /// wanderer group (post-fight despawn). Do not write this to swap enemies —
    /// use <see cref="SetEnemies"/>. Two rows can be the same enemies.
    /// </summary>
    public int EncounterRow
    {
        get => Encounter[13];
        set => Encounter[13] = ClampByte(value);
    }

    /// <summary>How many 20-byte groups follow the header (<c>Encounter[6]</c>).</summary>
    public int GroupCount
    {
        get => Encounter[6];
        set => Encounter[6] = EncounterSlot.ClampByte(value);
    }

    /// <summary>View of group <paramref name="index"/> (0 .. <see cref="EncounterSlot.MaxCount"/>-1).</summary>
    public EncounterSlot Slot(int index) => new(Encounter, index);

    /// <summary>
    /// One group: set <see cref="GroupCount"/> to 1, fill slot 0.
    /// <paramref name="speciesIndex"/> 0 (default) keeps a local form-row on
    /// its existing <see cref="SpeciesMap"/> slot. Placement/kind -1 keep
    /// this fight's stock lane (do not copy Marna 6 onto another stage).
    /// </summary>
    public void SetEnemies(int count, int species, int placement = -1, int kind = -1, int speciesIndex = 0)
    {
        SetEnemies(new EnemyGroup(count, species, placement, kind, speciesIndex));
    }

    /// <inheritdoc cref="SetEnemies(int, int, int, int, int)"/>
    public void SetEnemies(int count, Species species, int placement = -1, int kind = -1, int speciesIndex = 0)
    {
        SetEnemies(count, (int)species, placement, kind, speciesIndex);
    }

    /// <summary>
    /// Replace this fight's groups (max <see cref="EncounterSlot.MaxCount"/>).
    /// Local form-rows stay on their catalog index (skill/anim tables are
    /// keyed by that slot, not only by the remap byte). Foreign form-rows
    /// overwrite a catalog slot from this fight first, then any unused live
    /// slot. Placement/kind -1 keep this group's stock lane (Marna 6 is a
    /// flyer ring on E010). Tentacle rows stay only when their body species is
    /// in the same call; they are packed as extra pairs on that body group
    /// (not their own slot). The same <see cref="EnemyGroup.Species"/> reuses
    /// one index. Leftover stock groups are cleared. Form-rows are written
    /// here (after M_DAT copy), not at setup.
    /// </summary>
    public void SetEnemies(params EnemyGroup[] groups)
    {
        if (groups is null || groups.Length == 0)
        {
            return;
        }

        var n = Math.Min(groups.Length, EncounterSlot.MaxCount);
        var nStock = Math.Min(GroupCount, EncounterSlot.MaxCount);
        var stockIndices = new List<int>();
        var stockPlace = new int[EncounterSlot.MaxCount];
        var stockKind = new int[EncounterSlot.MaxCount];
        var stockAux = new int[EncounterSlot.MaxCount];
        for (var i = 0; i < nStock; i++)
        {
            var s = Slot(i);
            stockPlace[i] = s.Placement;
            stockKind[i] = s.Kind;
            stockAux[i] = s.Aux;
            if (s.SpeciesIndex > 0 && !stockIndices.Contains(s.SpeciesIndex))
            {
                stockIndices.Add(s.SpeciesIndex);
            }
        }

        var filtered = new List<EnemyGroup>(n);
        var fightRows = new HashSet<int>();
        for (var i = 0; i < n; i++)
        {
            fightRows.Add(EncounterSlot.ClampByte(groups[i].Species));
        }

        for (var i = 0; i < n; i++)
        {
            var formRow = EncounterSlot.ClampByte(groups[i].Species);
            var parentRow = AttachmentParentRow(formRow);
            if (parentRow != 0 && !fightRows.Contains(parentRow))
            {
                continue;
            }

            filtered.Add(groups[i]);
        }

        n = Math.Min(filtered.Count, EncounterSlot.MaxCount);
        if (n == 0)
        {
            return;
        }

        fightRows.Clear();
        for (var i = 0; i < n; i++)
        {
            fightRows.Add(EncounterSlot.ClampByte(filtered[i].Species));
        }

        var assigned = new Dictionary<int, int>();
        var taken = new HashSet<int>();
        foreach (var g in filtered)
        {
            var formRow = EncounterSlot.ClampByte(g.Species);
            if (assigned.ContainsKey(formRow))
            {
                continue;
            }

            int idx;
            if (g.SpeciesIndex > 0)
            {
                idx = g.SpeciesIndex;
            }
            else
            {
                idx = PickSpeciesIndex(formRow, taken, fightRows, stockIndices);
            }

            if ((uint)idx >= (uint)SpeciesMap.Length)
            {
                idx = SpeciesMap.Length - 1;
            }

            taken.Add(idx);
            assigned[formRow] = idx;
            if (SpeciesMap[idx] != formRow)
            {
                SetSpecies(idx, formRow);
            }
        }

        var bodies = new List<EnemyGroup>();
        var parts = new Dictionary<int, List<EnemyGroup>>();
        foreach (var g in filtered)
        {
            var formRow = EncounterSlot.ClampByte(g.Species);
            var parentRow = AttachmentParentRow(formRow);
            if (parentRow != 0 && assigned.ContainsKey(parentRow))
            {
                if (!parts.TryGetValue(parentRow, out var list))
                {
                    list = [];
                    parts[parentRow] = list;
                }

                list.Add(g);
                continue;
            }

            bodies.Add(g);
        }

        var nSlots = Math.Min(bodies.Count, EncounterSlot.MaxCount);
        if (nSlots == 0)
        {
            return;
        }

        GroupCount = nSlots;
        Array.Clear(Encounter, EncounterSlotOff, EncounterSlotSize * EncounterSlot.MaxCount);

        for (var i = 0; i < nSlots; i++)
        {
            var g = bodies[i];
            var formRow = EncounterSlot.ClampByte(g.Species);
            var slot = Slot(i);
            slot.Placement = g.Placement >= 0 ? g.Placement : stockPlace[i];
            slot.Aux = i < nStock ? stockAux[i] : 0;
            slot.Kind = g.Kind >= 0 ? g.Kind : stockKind[i];
            slot.SetPair(0, assigned[formRow], g.Count);
            if (!parts.TryGetValue(formRow, out var kids))
            {
                continue;
            }

            for (var p = 0; p < kids.Count && p < 7; p++)
            {
                var kid = kids[p];
                slot.SetPair(p + 1, assigned[EncounterSlot.ClampByte(kid.Species)], kid.Count);
            }
        }
    }

    /// <summary>
    /// Form-row of the body this attached part belongs to, or 0 if this row
    /// is a normal independent enemy.
    /// </summary>
    private static int AttachmentParentRow(int formRow)
    {
        return formRow switch
        {
            0x97 or 0x98 => 0x96,
            0x9A or 0x9B => 0x99,
            _ => 0,
        };
    }

    /// <summary>
    /// Prefer the map's existing catalog slot for <paramref name="formRow"/> so
    /// skill scripts stay attached. Foreign rows take a catalog index from
    /// this fight first (so a grounded wanderer is not replaced by a flyer
    /// slot), then any other unused live slot.
    /// </summary>
    private int PickSpeciesIndex(int formRow, HashSet<int> taken, HashSet<int> fightRows,
        List<int> stockIndices)
    {
        for (var i = 1; i < SpeciesMap.Length; i++)
        {
            if (SpeciesMap[i] == formRow && !taken.Contains(i))
            {
                return i;
            }
        }

        foreach (var i in stockIndices)
        {
            if ((uint)i >= (uint)SpeciesMap.Length || taken.Contains(i))
            {
                continue;
            }

            if (!fightRows.Contains(SpeciesMap[i]))
            {
                return i;
            }
        }

        var empty = 0;
        for (var i = 1; i < SpeciesMap.Length; i++)
        {
            if (taken.Contains(i))
            {
                continue;
            }

            var cur = SpeciesMap[i];
            if (cur == 0)
            {
                if (empty == 0)
                {
                    empty = i;
                }

                continue;
            }

            if (!fightRows.Contains(cur))
            {
                return i;
            }
        }

        if (empty != 0)
        {
            return empty;
        }

        for (var i = 1; i < SpeciesMap.Length; i++)
        {
            if (!taken.Contains(i))
            {
                return i;
            }
        }

        return 1;
    }

    /// <summary>Write <see cref="SpeciesMap"/>[<paramref name="index"/>] (form-row, e.g. 0x69 Centipede).</summary>
    public void SetSpecies(int index, int formRow)
    {
        if ((uint)index >= (uint)SpeciesMap.Length)
        {
            return;
        }

        SpeciesMap[index] = EncounterSlot.ClampByte(formRow);
    }

    /// <summary>Normal / initiative / ambush (<c>Encounter[11]</c>).</summary>
    public BattleApproach Approach
    {
        get => (BattleApproach)Encounter[11];
        set => Encounter[11] = (byte)value;
    }

    /// <summary>
    /// Raw 20-byte group at +0x10. Prefer <see cref="Slot"/> / <see cref="SetEnemies"/>.
    /// </summary>
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
