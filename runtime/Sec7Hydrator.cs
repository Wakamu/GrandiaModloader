using Grandia.Sdk;

namespace Grandia.Runtime;

internal static class Sec7Hydrator
{
    private static readonly (HdAssetKind Kind, string Token)[] SpriteKinds =
    [
        (HdAssetKind.Anim, "anim"),
        (HdAssetKind.Tenants, "tenants"),
        (HdAssetKind.Maps, "maps"),
        (HdAssetKind.MapEff, "mapeff"),
    ];

    public static void Attach(Map map, byte[]? mdp, Action<string>? log, string? fieldDir = null)
    {
        map.Zones.Ensure = null;
        map.Hooks.Ensure = null;
        map.AltHooks.Ensure = null;
        map.Sfx.Ensure = null;
        map.Npcs.Ensure = null;
        map.Anims.Ensure = null;
        map.SpriteClips.Ensure = null;
        map.Poses.Ensure = null;
        map.Sprites.Ensure = null;
        map.Textures.Ensure = null;
        map.CameraPaths.Ensure = null;
        map.Camera.Ensure = null;
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
                map.AltHooks.Hydrate(tables.AltHooks.Select(Hook.FromRaw));
                encounters.AddRange(MapEncounter.FromSec7(tables));
                zoneCount = tables.Zones.Count;
                hookCount = tables.Hooks.Count + tables.AltHooks.Count;
            }

            encounters.AddRange(MapEncounter.FromField(MdpHookIds.TrySlice(mdp, 8), MdpHookIds.TrySlice(mdp, 30)));
            map.HydrateEncounters(encounters);
            var sfx = MdpSec29.Parse(MdpHookIds.TrySlice(mdp, 29) ?? []);
            map.Sfx.Hydrate(sfx.Flags, sfx.Range, sfx.Items);
            var npcs = MdpSec8.Parse(MdpHookIds.TrySlice(mdp, 8) ?? []);
            map.Npcs.Hydrate(npcs.Instances);
            var anims = MdpSec21.Parse(MdpHookIds.TrySlice(mdp, 21) ?? []);
            map.Anims.Hydrate(anims.Items);
            var sprites = MdpSec23.Parse(MdpHookIds.TrySlice(mdp, 23) ?? []);
            map.SpriteClips.Hydrate(sprites.Clips);
            map.Poses.Hydrate(sprites.Poses);
            var uv = MdpSec32.ParseAll(MdpHookIds.TrySlice(mdp, 32) ?? []);
            var sheets = LoadSpriteSheets(map.Stem, fieldDir);
            map.Sprites.Hydrate(uv, sheets);
            map.Textures.Hydrate(MdpTim.FromMdp(mdp));
            map.CameraPaths.Hydrate(MdpSec15.Parse(MdpHookIds.TrySlice(mdp, 15) ?? []).Items);
            map.Camera.Hydrate(MdpHookIds.TrySlice(mdp, 10));

        }
        catch (Exception ex)
        {
            log?.Invoke($"sec7 hydrate {map.Stem}: {ex.Message}");
        }
    }

    private static List<MapSpriteSheet> LoadSpriteSheets(string stem, string? fieldDir)
    {
        var sheets = new List<MapSpriteSheet>();
        if (string.IsNullOrWhiteSpace(fieldDir) || !Directory.Exists(fieldDir))
        {
            return sheets;
        }

        foreach (var (kind, token) in SpriteKinds)
        {
            var path = Path.Combine(fieldDir, $"{stem}_{token}__spriteinfo.bin");
            if (!File.Exists(path))
            {
                path = Path.Combine(fieldDir, $"{stem.ToLowerInvariant()}_{token}__spriteinfo.bin");
            }

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                sheets.Add(MapSpriteBank.FromSpriteInfo(kind, File.ReadAllBytes(path)));
            }
            catch (Exception)
            {
                // sidecar present but unreadable — leave that sheet empty
            }
        }

        return sheets;
    }
}
