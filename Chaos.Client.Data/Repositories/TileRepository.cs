#region
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class TileRepository
{
    private readonly PaletteLookup BackgroundPalettes = PaletteLookup.FromArchive("mpt", DatArchives.Seo)
                                                                     .Freeze();

    private readonly PaletteLookup ForegroundPalettes = PaletteLookup.FromArchive("stc", DatArchives.Ia)
                                                                     .Freeze();

    private TilesetView Tileset = TilesetView.FromArchive("tilea", DatArchives.Seo);
    private bool UseSnowTileset;

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
            Palette = BackgroundPalettes.GetPaletteForId(tileId + 1)
        };
    }

    public Palettized<HpfFile>? GetForegroundTile(int tileId)
    {
        if (!DatArchives.Ia.TryGetValue($"stc{tileId:D5}.hpf", out var entry))
            return null;

        return new Palettized<HpfFile>
        {
            Entity = HpfFile.FromEntry(entry),
            Palette = ForegroundPalettes.GetPaletteForId(tileId + 1)
        };
    }

    public void ToggleSnowTileset()
    {
        UseSnowTileset = !UseSnowTileset;

        Tileset = TilesetView.FromArchive(UseSnowTileset ? "tileas" : "tilea", DatArchives.Seo);
    }
}