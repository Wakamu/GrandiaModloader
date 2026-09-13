namespace Grandia.Sdk;

/// <summary>
/// Last living field enemy just paid out at <c>+0x138790</c> (XP pot, gold pot,
/// rolled drops). Fires once per fight, before the result screen reads the pots.
/// <see cref="Exp"/> / <see cref="Gold"/> / <see cref="Drops"/> write back to
/// the battle context. <see cref="EncounterRow"/> is the field wanderer group
/// (same id <see cref="BattleLoadEvent.EncounterRow"/> uses to despawn).
/// </summary>
public sealed class VictoryEvent
{
    public const int MaxDrops = 16;

    public VictoryEvent(int exp, int gold, IEnumerable<Item>? drops, MapId map = default,
        MapId dest = default, int spawn = 0, int encounterTable = 0, int encounterRow = 0)
    {
        Exp = exp;
        Gold = gold;
        Drops = [];
        if (drops is not null)
        {
            foreach (var item in drops)
            {
                if (Drops.Count >= MaxDrops)
                {
                    break;
                }

                if (item != Item.None)
                {
                    Drops.Add(item);
                }
            }
        }

        Map = map;
        Dest = dest;
        Spawn = spawn;
        EncounterTable = encounterTable;
        EncounterRow = encounterRow;
    }

    /// <summary>Field map id (MapObj+8) from battle load.</summary>
    public MapId Map { get; }

    /// <summary>Travel dest word from battle load (often 0 in random fights).</summary>
    public MapId Dest { get; }

    /// <summary>MapObj+0x5B4 at battle load (previous field, or 0).</summary>
    public int Spawn { get; }

    /// <summary>Encounter table id (<c>Encounter[1]</c>).</summary>
    public int EncounterTable { get; }

    /// <summary>
    /// Rolled field-wanderer row (<c>Encounter[13]</c>). Identifies the group
    /// that despawns after this fight.
    /// </summary>
    public int EncounterRow { get; }

    /// <summary>XP pot at battle ctx+0x88 (sum of dead enemies' <c>+0x17E</c>).</summary>
    public int Exp { get; set; }

    /// <summary>Gold pot at battle ctx+0x8C (sum of dead enemies' <c>+0x180</c>).</summary>
    public int Gold { get; set; }

    /// <summary>Rolled drop items at ctx+0xEC14 (count at ctx+0x23F). Max 16.</summary>
    public List<Item> Drops { get; }
}
