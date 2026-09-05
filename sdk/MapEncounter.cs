namespace Grandia.Sdk;

/// <summary>
/// One <c>(SpeciesIndex, Count)</c> nibble pair from a scripted-battle row
/// or a field wanderer pack. <see cref="SpeciesIndex"/> selects
/// <c>SpeciesMap[index]</c> at battle start — the form-row is not known at
/// <c>OnMapLoad</c>.
/// </summary>
public readonly record struct EncounterPair(int SpeciesIndex, int Count)
{
    public override string ToString() => $"{SpeciesIndex}x{Count}";
}

/// <summary>
/// XZ wander box from a sec[30] actor (<c>+0x12..+0x18</c>).
/// </summary>
public readonly record struct WanderBox(short XMin, short Z0, short XMax, short Z1)
{
    public override string ToString() => $"{XMin},{Z0},{XMax},{Z1}";
}

/// <summary>
/// A fight on this map: sec[7] handler 0x19, or a field wanderer
/// (sec[30] actor + sec[8] kind-2 pack). Scripted rows hardcode
/// <see cref="EncounterRow"/> = 255 so no wanderer despawns.
/// </summary>
public sealed class MapEncounter
{
    public MapEncounter(int hookId, int encounterTable, IReadOnlyList<EncounterPair> pairs)
    {
        HookId = hookId;
        EncounterTable = encounterTable;
        Pairs = pairs;
    }

    /// <summary>Byte 0. Used by <c>call_hook N</c> / <c>call_hook N alt</c>. 0 for wanderers.</summary>
    public int HookId { get; }

    /// <summary>True when the row lives on table 3 (<c>call_hook N alt</c>).</summary>
    public bool Alt { get; init; }

    /// <summary>True when this is a table-1 AABB row, not a <c>call_hook</c>.</summary>
    public bool IsZone { get; init; }

    /// <summary>
    /// B00x / stage id. Scripted: handler blob <c>[5]</c>. Wanderer: sec[30] <c>[+6]</c>
    /// (copied to battle <c>Encounter[1]</c> at <c>+0x7B9DF</c>).
    /// </summary>
    public int EncounterTable { get; }

    /// <summary>
    /// Field group written to <c>Encounter[13]</c>. Scripted is always 255.
    /// Wanderer is the sec[30] id high byte (Marna Bugs = 9, Centipedes = 0x13).
    /// </summary>
    public int EncounterRow { get; init; } = 255;

    /// <summary>False for sec[30] wanderers.</summary>
    public bool Scripted { get; init; } = true;

    /// <summary>sec[30] id high byte (1-based slot on sequential maps).</summary>
    public int Slot { get; init; }

    /// <summary>Talk ids on this actor (0 unused). Slot <c>Kind</c> is the first one.</summary>
    public IReadOnlyList<int> TalkIds { get; init; } = [];

    public short X { get; init; }

    public short Y { get; init; }

    public short Z { get; init; }

    public WanderBox Wander { get; init; }

    /// <summary>sec[30] <c>+1E</c> event-flag bit.</summary>
    public int Flag { get; init; }

    /// <summary>sec[31] script ids at <c>+1A/+1B</c>.</summary>
    public int SpriteA { get; init; }

    public int SpriteB { get; init; }

    /// <summary>Catalog index × count pairs packed in the row or sec[8] <c>+0x10</c>.</summary>
    public IReadOnlyList<EncounterPair> Pairs { get; }

    /// <summary>Optional BE word at <c>[6:8]</c> (stock Ghost Ship uses <c>0x4A</c>).</summary>
    public int Word { get; init; }

    public int Flags { get; init; }

    public int Trig { get; init; }

    /// <summary>AABB when <see cref="IsZone"/>; default otherwise.</summary>
    public ZoneBox Aabb { get; init; }

    /// <summary>Assembler line that rebuilds this row (<c>hook …</c> or <c>zone …</c>). Wanderers use <c>wander …</c>.</summary>
    public string Line { get; init; } = "";

    /// <summary><c>call_hook N</c> or <c>call_hook N alt</c>. Empty for zones and wanderers.</summary>
    public string CallLine =>
        !Scripted || IsZone ? "" : Alt ? $"call_hook {HookId} alt" : $"call_hook {HookId}";

    public override string ToString()
    {
        var pairs = Pairs.Count == 0 ? "" : " " + string.Join(' ', Pairs);
        var word = Word != 0 ? $" word=0x{Word:X}" : "";
        if (!Scripted)
        {
            return $"wander row={EncounterRow} table=0x{EncounterTable:X2}{pairs} pos={X},{Y},{Z}";
        }

        if (IsZone)
        {
            return $"zone {HookId} table=0x{EncounterTable:X2}{pairs}{word} aabb={Aabb}";
        }

        return $"hook {HookId}{(Alt ? " alt" : "")} table=0x{EncounterTable:X2}{pairs}{word}";
    }

    internal static IReadOnlyList<MapEncounter> FromSec7(MdpSec7 tables)
    {
        var list = new List<MapEncounter>();
        Collect(list, tables.Zones, sec7Table: 1);
        Collect(list, tables.Hooks, sec7Table: 2);
        Collect(list, tables.AltHooks, sec7Table: 3);
        return list;
    }

    /// <summary>
    /// Field wanderers: one entry per sec[30] actor that has a kind-2 talk.
    /// Pack nibbles come from that talk's sec[8] row at <c>+0x10</c> (<c>+0x7BD80</c>).
    /// </summary>
    internal static IReadOnlyList<MapEncounter> FromField(byte[]? sec8, byte[]? sec30)
    {
        var list = new List<MapEncounter>();
        if (sec30 is not { Length: >= 8 })
        {
            return list;
        }

        var n = BitConverter.ToInt32(sec30, 4);
        if (n <= 0 || 8 + n * 56 > sec30.Length)
        {
            return list;
        }

        var byTalk = IndexKind2(sec8);
        for (var i = 0; i < n; i++)
        {
            var rec = sec30.AsSpan(8 + i * 56, 56);
            var talks = new List<int>(4);
            var pairs = new List<EncounterPair>();
            for (var t = 0; t < 4; t++)
            {
                var talk = rec[2 + t];
                if (talk == 0)
                {
                    continue;
                }

                talks.Add(talk);
                if (!byTalk.TryGetValue(talk, out var inst))
                {
                    continue;
                }

                foreach (var pair in DecodeSec8Pairs(inst))
                {
                    pairs.Add(pair);
                }
            }

            if (talks.Count == 0 || talks.All(t => !byTalk.ContainsKey(t)))
            {
                continue;
            }

            var id = BitConverter.ToUInt16(sec30, 8 + i * 56);
            var x = BitConverter.ToInt16(sec30, 8 + i * 56 + 0xC);
            var y = BitConverter.ToInt16(sec30, 8 + i * 56 + 0xE);
            var z = BitConverter.ToInt16(sec30, 8 + i * 56 + 0x10);
            var wander = new WanderBox(
                BitConverter.ToInt16(sec30, 8 + i * 56 + 0x12),
                BitConverter.ToInt16(sec30, 8 + i * 56 + 0x14),
                BitConverter.ToInt16(sec30, 8 + i * 56 + 0x16),
                BitConverter.ToInt16(sec30, 8 + i * 56 + 0x18));
            var table = rec[6];
            var row = id >> 8;
            if (row == 0)
            {
                row = i;
            }

            var pairText = pairs.Count == 0 ? "" : " " + string.Join(' ', pairs);
            list.Add(new MapEncounter(0, table, pairs)
            {
                Scripted = false,
                EncounterRow = row,
                Slot = row,
                TalkIds = talks,
                X = x,
                Y = y,
                Z = z,
                Wander = wander,
                Flag = rec[0x1E],
                SpriteA = rec[0x1A],
                SpriteB = rec[0x1B],
                Line = $"wander row={row} table=0x{table:X2}{pairText} pos={x},{y},{z}",
            });
        }

        return list;
    }

    internal static MapEncounter? TryFromRow(byte[] raw, int sec7Table)
    {
        if (raw.Length < FieldHookAsm.HookRowSize || (raw[1] & 0x3F) != 0x19)
        {
            return null;
        }

        var pairs = new List<EncounterPair>();
        var last = -1;
        var packed = new EncounterPair[FieldHookAsm.ScriptedBattlePairOffs.Length];
        for (var i = 0; i < FieldHookAsm.ScriptedBattlePairOffs.Length; i++)
        {
            var b = raw[FieldHookAsm.ScriptedBattlePairOffs[i]];
            packed[i] = new EncounterPair(b >> 4, b & 0xF);
            if (b != 0)
            {
                last = i;
            }
        }

        for (var i = 0; i <= last; i++)
        {
            pairs.Add(packed[i]);
        }

        var isZone = sec7Table == 1 || raw.Length >= FieldHookAsm.ZoneRowSize;
        return new MapEncounter(raw[0], raw[5], pairs)
        {
            Alt = sec7Table == 3,
            IsZone = sec7Table == 1,
            Word = (raw[6] << 8) | raw[7],
            Flags = raw[4],
            Trig = raw[2],
            Aabb = isZone && raw.Length >= FieldHookAsm.ZoneRowSize
                ? FieldHookAsm.ReadAabb(raw)
                : default,
            Line = sec7Table == 1 && raw.Length >= FieldHookAsm.ZoneRowSize
                ? FieldHookAsm.FormatZone(raw)
                : FieldHookAsm.FormatHook(raw.AsSpan(0, FieldHookAsm.HookRowSize)),
        };
    }

    private static void Collect(List<MapEncounter> list, List<byte[]> rows, int sec7Table)
    {
        foreach (var raw in rows)
        {
            var enc = TryFromRow(raw, sec7Table);
            if (enc != null)
            {
                list.Add(enc);
            }
        }
    }

    private static Dictionary<int, byte[]> IndexKind2(byte[]? sec8)
    {
        var byTalk = new Dictionary<int, byte[]>();
        if (sec8 is not { Length: >= 2 })
        {
            return byTalk;
        }

        var n = BitConverter.ToUInt16(sec8, 0) & 0x3FFF;
        for (var i = 0; i < n; i++)
        {
            var at = 2 + i * 48;
            if (at + 48 > sec8.Length)
            {
                break;
            }

            var rec = sec8.AsSpan(at, 48);
            if ((rec[1] & 0xF) != 2)
            {
                continue;
            }

            var talk = rec[3];
            if (talk != 0 && !byTalk.ContainsKey(talk))
            {
                byTalk[talk] = rec.ToArray();
            }
        }

        return byTalk;
    }

    /// <summary>
    /// Four LE words at sec[8] <c>+0x10</c> → eight pairs, matching <c>+0x7BD80</c>
    /// (<c>word>>12</c> / <c>byte&amp;0xF</c> / low-byte nibbles). Trailing 0x0 pairs dropped.
    /// </summary>
    internal static IReadOnlyList<EncounterPair> DecodeSec8Pairs(ReadOnlySpan<byte> rec)
    {
        if (rec.Length < 0x18)
        {
            return [];
        }

        var packed = new EncounterPair[8];
        var last = -1;
        for (var w = 0; w < 4; w++)
        {
            var lo = rec[0x10 + w * 2];
            var hi = rec[0x10 + w * 2 + 1];
            packed[w * 2] = new EncounterPair(hi >> 4, hi & 0xF);
            packed[w * 2 + 1] = new EncounterPair(lo >> 4, lo & 0xF);
            if (hi != 0)
            {
                last = w * 2;
            }

            if (lo != 0)
            {
                last = w * 2 + 1;
            }
        }

        if (last < 0)
        {
            return [];
        }

        return packed.Take(last + 1).ToList();
    }
}
