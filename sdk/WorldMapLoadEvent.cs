namespace Grandia.Sdk;

/// <summary>
/// Area map is opening at +0x59320. <see cref="Destinations"/> is the
/// <em>icons</em> on this set (up to 16), each with a stable icon
/// <see cref="WorldMapDestination.Slot"/>. Travel dest can vary by
/// <see cref="OriginContext"/> (field world-map exit param at <c>[64122A]</c>)
/// — Lama is one icon, North vs South is not a second slot. An unchanged
/// list does not rewrite exe tables.
/// </summary>
public sealed class WorldMapLoadEvent
{
    public const int SlotCount = 16;

    public WorldMapLoadEvent(int set, int amapIndex, int originContext = 0,
        IEnumerable<WorldMapDestination>? destinations = null)
    {
        Set = set;
        AmapIndex = amapIndex;
        OriginContext = originContext;
        Destinations = Copy(destinations);
    }

    /// <summary>Continent dest-table set (0 Parm … 3 late). From <c>0x600AB0</c>.</summary>
    public int Set { get; }

    /// <summary>Raw AMAP# digit (story bits at flag-blob +0x72).</summary>
    public int AmapIndex { get; }

    /// <summary>
    /// World-map exit param from the field you left (<c>[64122A]</c>).
    /// Selects which dest row an icon uses (Lama North vs South).
    /// </summary>
    public int OriginContext { get; }

    /// <summary>Icons on this set. Slots are icon indices, not dest-table rows.</summary>
    public List<WorldMapDestination> Destinations { get; }

    public void Add(MapId map, int x = 0, int y = 0, int aux = 0, int picture = 0,
        string? picturePath = null, int pictureWidth = 0, int pictureHeight = 0)
    {
        Destinations.Add(new WorldMapDestination(map, aux, x, y, revealed: true, slot: -1,
            picture: picture, picturePath: picturePath, pictureWidth: pictureWidth,
            pictureHeight: pictureHeight));
    }

    public bool Remove(MapId map) => Destinations.RemoveAll(d => d.Map == map) > 0;

    private static List<WorldMapDestination> Copy(IEnumerable<WorldMapDestination>? src)
    {
        var list = new List<WorldMapDestination>(SlotCount);
        if (src is null)
        {
            return list;
        }

        foreach (var dest in src)
        {
            if (list.Count >= SlotCount)
            {
                break;
            }

            list.Add(new WorldMapDestination(dest.Map, dest.Aux, dest.X, dest.Y, dest.Revealed,
                dest.Slot, dest.Variants, dest.Picture, dest.PicturePath, dest.PictureWidth,
                dest.PictureHeight));
        }

        return list;
    }
}
