namespace Grandia.Sdk;

/// <summary>
/// Who wrote a progress flag. Map-init bulk callers are not raised to mods.
/// </summary>
public enum EventFlagKind
{
    Other = 0,
    Loot = 1,
    Story = 2,
    MapInit = 3,
}
