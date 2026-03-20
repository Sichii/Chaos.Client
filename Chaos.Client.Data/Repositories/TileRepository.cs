#region
using Chaos.Client.Data.Abstractions;
using DALib.Drawing;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class TileRepository : RepositoryBase
{
    private readonly PaletteLookup BackgroundPalettes = PaletteLookup.FromArchive("mpt", DatArchives.Seo);
    private readonly PaletteLookup ForegroundPalettes = PaletteLookup.FromArchive("stc", DatArchives.Ia);
    private Tileset Tileset = Tileset.FromArchive("tilea", DatArchives.Seo);
    private bool UseSnowTileset;

    /// <summary>
    ///     SOTP (Sector Object Type Properties) data loaded from Ia archive. Used for tile walkability checks in the tab map.
    /// </summary>
    public byte[] SotpData { get; } = DatArchives.Ia.TryGetValue("sotp.dat", out var sotpEntry)
        ? sotpEntry.ToSpan()
                   .ToArray()
        : [];

    private string ConstructBackgroundTileKey(int tileId) => $"BG_{tileId}{(UseSnowTileset ? "_S" : string.Empty)}";
    private string ConstructForegroundTileKey(int tileId) => $"FG_{tileId}";

    public Palettized<Tile>? GetBackgroundTile(int tileId)
    {
        try
        {
            return GetOrCreate(ConstructBackgroundTileKey(tileId), () => LoadBackgroundTile(tileId));
        } catch
        {
            return null;
        }
    }

    public Palettized<HpfFile>? GetForegroundTile(int tileId)
    {
        try
        {
            return GetOrCreate(ConstructForegroundTileKey(tileId), () => LoadForegroundTile(tileId));
        } catch
        {
            return null;
        }
    }

    private Palettized<Tile> LoadBackgroundTile(int tileId)
        => new()
        {
            Entity = Tileset[tileId - 1],
            Palette = BackgroundPalettes.GetPaletteForId(tileId + 1)
        };

    private Palettized<HpfFile> LoadForegroundTile(int tileId)
        => new()
        {
            Entity = HpfFile.FromArchive($"stc{tileId:D5}", DatArchives.Ia),
            Palette = ForegroundPalettes.GetPaletteForId(tileId + 1)
        };

    public void ToggleSnowTileset()
    {
        UseSnowTileset = !UseSnowTileset;

        Tileset = Tileset.FromArchive(UseSnowTileset ? "tileas" : "tilea", DatArchives.Seo);
    }
}