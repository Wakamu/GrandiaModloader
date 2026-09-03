namespace Grandia.Sdk;

/// <summary>
/// One world-map <em>icon</em>. Some icons (Lama Mountains) have more than
/// one travel dest; stock picks North vs South from the field you left
/// (<c>[64122A]</c>). <see cref="Map"/> is the dest for this open;
/// <see cref="Variants"/> is the other dest(s) the same icon can use.
/// </summary>
public sealed class WorldMapDestination
{
    public WorldMapDestination(MapId map, int aux = 0, int x = 0, int y = 0, bool revealed = true,
        int slot = -1, IEnumerable<MapId>? variants = null, int picture = -1,
        string? picturePath = null, int pictureWidth = 0, int pictureHeight = 0)
    {
        Map = map;
        Aux = aux;
        X = x;
        Y = y;
        Revealed = revealed;
        Slot = slot;
        Variants = variants?.ToList() ?? [];
        Picture = picture;
        PicturePath = picturePath;
        PictureWidth = pictureWidth;
        PictureHeight = pictureHeight;
    }

    /// <summary>Icon index 0..15, or -1 if this icon was added.</summary>
    public int Slot { get; set; }

    /// <summary>Dest for the current origin context (where you opened the map).</summary>
    public MapId Map { get; set; }

    /// <summary>Stock aux word for <see cref="Map"/> (spawn / entrance).</summary>
    public int Aux { get; set; }

    /// <summary>Icon offset on the area map (stock units, ≈±256).</summary>
    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>
    /// When true, the icon uses the stock Parm reveal bit so it shows.
    /// When false, the icon is hidden. Dest-table variants are left intact.
    /// </summary>
    public bool Revealed { get; set; }

    /// <summary>
    /// Other dest maps this icon can travel to (Lama South if <see cref="Map"/>
    /// is North, and the reverse). Origin fallbacks to the continent hub are
    /// omitted. Read-only snapshot from stock cursor contexts.
    /// </summary>
    public List<MapId> Variants { get; }

    /// <summary>
    /// Which stock icon's art to draw (0 = first location on this map).
    /// -1 = this slot's own AMAP sprite. Added dests default to 0 — unused
    /// slots have no sprite strip and look like noise.
    /// </summary>
    public int Picture { get; set; }

    /// <summary>
    /// Custom nameplate PNG. An embedded resource name (<c>hub.png</c> /
    /// <c>assets/hub.png</c>) is extracted from the mod DLL; a file path is
    /// used as-is. Drawn as an overlay on this pin only — stock locations
    /// that share <see cref="Picture"/> keep their original art.
    /// </summary>
    public string? PicturePath { get; set; }

    /// <summary>
    /// Overlay draw width in area-map screen units (same space as stock
    /// plates, ~128 wide). 0 = stock plate width. If only width is set,
    /// height follows the PNG aspect.
    /// </summary>
    public int PictureWidth { get; set; }

    /// <summary>
    /// Overlay draw height in area-map screen units (stock plates are 16).
    /// 0 = stock plate height, or PNG aspect when <see cref="PictureWidth"/>
    /// is set.
    /// </summary>
    public int PictureHeight { get; set; }

    /// <summary>Set <see cref="PicturePath"/> and optional draw size.</summary>
    public void SetPicture(string path, int width = 0, int height = 0)
    {
        PicturePath = path;
        PictureWidth = width;
        PictureHeight = height;
    }
}
