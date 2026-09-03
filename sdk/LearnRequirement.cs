namespace Grandia.Sdk;

/// <summary>One magic/skill learn requirement (element or weapon level).</summary>
public sealed class LearnRequirement
{
    public LearnRequirement(LearnKind kind, int level)
    {
        Kind = kind;
        Level = level;
    }

    public LearnKind Kind { get; set; }

    public int Level { get; set; }
}
