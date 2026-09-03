namespace Grandia.Sdk;

/// <summary>
/// One enemy group for <see cref="BattleLoadEvent.SetEnemies"/>.
/// <see cref="Grandia.Sdk.Species"/> is the form-row (e.g. Giant Centipede), not the
/// species-map index — the host assigns map indices unless
/// <see cref="SpeciesIndex"/> is set.
/// </summary>
public readonly struct EnemyGroup
{
    public EnemyGroup(int count, int species, int placement = -1, int kind = -1, int speciesIndex = 0)
    {
        Count = count;
        Species = species;
        Placement = placement;
        Kind = kind;
        SpeciesIndex = speciesIndex;
    }

    public EnemyGroup(int count, Species species, int placement = -1, int kind = -1, int speciesIndex = 0)
        : this(count, (int)species, placement, kind, speciesIndex)
    {
    }

    /// <summary>How many of this species spawn in the group.</summary>
    public int Count { get; }

    /// <summary>Form-row / <see cref="Grandia.Sdk.Species"/> (Giant Centipede is 0x69).</summary>
    public int Species { get; }

    /// <summary>
    /// Slot byte 0 (lane). Stage-local: Marna 6 is a ground lane there and an
    /// aerial ring on E010. -1 (default) keeps this group's stock lane.
    /// Attached parts (tentacles) are packed onto the body group; this
    /// value is ignored for them.
    /// </summary>
    public int Placement { get; }

    /// <summary>
    /// U16 at slot +2. -1 (default) keeps this group's stock kind.
    /// Spawn loop does not read it; kept to match captured fights.
    /// </summary>
    public int Kind { get; }

    /// <summary>
    /// Explicit <c>SpeciesMap</c> index. 0 (default) keeps a form-row that is
    /// already on this map at its catalog slot. Foreign species prefer a
    /// catalog index from this fight (grounded if you walked into a grounded
    /// group) instead of the first unused live slot.
    /// </summary>
    public int SpeciesIndex { get; }
}
