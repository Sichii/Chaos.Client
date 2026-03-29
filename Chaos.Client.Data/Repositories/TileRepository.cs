#region
using Chaos.Client.Data.Models;
using Chaos.Client.Data.Utilities;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class TileRepository
{
    private readonly TileAnimationTable BgAnimations = DatArchives.Seo.TryGetValue("gndani.tbl", out var bgAnimEntry)
        ? TileAnimationTable.FromEntry(bgAnimEntry)
        : new TileAnimationTable();

    private readonly TileAnimationTable FgAnimations = DatArchives.Ia.TryGetValue("stcani.tbl", out var fgAnimEntry)
        ? TileAnimationTable.FromEntry(fgAnimEntry)
        : new TileAnimationTable();

    private TilesetView Tileset = TilesetView.FromArchive("tilea", DatArchives.Seo);
    private bool UseSnowTileset;

    public PaletteLookup BackgroundPaletteLookup { get; } = PaletteLookup.FromArchive("mpt", DatArchives.Seo)
                                                                         .Freeze();

    public PaletteLookup ForegroundPaletteLookup { get; } = PaletteLookup.FromArchive("stc", DatArchives.Ia)
                                                                         .Freeze();

    /// <summary>
    ///     Ground tile attributes parsed from gndattr.tbl in seo.dat. Maps background tile IDs to their ground attributes
    ///     (color tint, walk-blocking, foreground height override).
    /// </summary>
    public Dictionary<int, GroundAttribute> GroundAttributes { get; } = DatArchives.Seo.TryGetValue("gndattr.tbl", out var gndAttrEntry)
        ? GroundAttributeParser.Parse(gndAttrEntry)
        : [];

    /// <summary>
    ///     SOTP (Sector Object Type Properties) data loaded from Ia archive. Used for tile walkability checks in the tab map.
    /// </summary>
    public byte[] SotpData { get; } = DatArchives.Ia.TryGetValue("sotp.dat", out var sotpEntry)
        ? sotpEntry.ToSpan()
                   .ToArray()
        : [];

    public Palettized<Tile>? GetBackgroundTile(int tileId)
    {
        if ((tileId <= 0) || ((tileId - 1) >= Tileset.Count))
            return null;

        return new Palettized<Tile>
        {
            Entity = Tileset[tileId - 1],
            Palette = BackgroundPaletteLookup.GetPaletteForId(tileId + 1)
        };
    }

    public TileAnimationEntry? GetBgAnimation(int tileId) => BgAnimations.TryGetEntry(tileId, out var entry) ? entry : null;

    public TileAnimationEntry? GetFgAnimation(int tileId) => FgAnimations.TryGetEntry(tileId, out var entry) ? entry : null;

    public Palettized<CompressedHpfFile>? GetForegroundTile(int tileId)
    {
        if (!DatArchives.Ia.TryGetValue($"stc{tileId:D5}.hpf", out var entry))
            return null;

        return new Palettized<CompressedHpfFile>
        {
            Entity = CompressedHpfFile.FromEntry(entry),
            Palette = ForegroundPaletteLookup.GetPaletteForId(tileId + 1)
        };
    }

    public void ToggleSnowTileset()
    {
        UseSnowTileset = !UseSnowTileset;

        Tileset = TilesetView.FromArchive(UseSnowTileset ? "tileas" : "tilea", DatArchives.Seo);
    }
}