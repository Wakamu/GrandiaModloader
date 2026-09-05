using Grandia.Sdk;

namespace Grandia.Runtime;

internal static class Sec7Hydrator
{
    public static void Attach(Map map, byte[]? mdp, Action<string>? log)
    {
        map.Zones.Ensure = null;
        map.Hooks.Ensure = null;
        map.EnsureEncounters = null;

        var sec7 = MdpHookIds.TrySlice(mdp, 7);
        var encounters = new List<MapEncounter>();
        var zoneCount = 0;
        var hookCount = 0;

        try
        {
            if (sec7 is { Length: >= MdpSec7.HeaderSize })
            {
                var tables = MdpSec7.Parse(sec7);
                map.Zones.Hydrate(tables.Zones.Select((raw, i) => Zone.FromRaw(i, raw)));
                map.Hooks.Hydrate(tables.Hooks.Select(Hook.FromRaw));
                encounters.AddRange(MapEncounter.FromSec7(tables));
                zoneCount = tables.Zones.Count;
                hookCount = tables.Hooks.Count;
            }

            encounters.AddRange(MapEncounter.FromField(MdpHookIds.TrySlice(mdp, 8), MdpHookIds.TrySlice(mdp, 30)));
            map.HydrateEncounters(encounters);
            log?.Invoke(
                $"hydrated {zoneCount} zone(s), {hookCount} hook(s), {map.Encounters.Count} encounter(s) on {map.Stem}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"sec7 hydrate {map.Stem}: {ex.Message}");
        }
    }
}
