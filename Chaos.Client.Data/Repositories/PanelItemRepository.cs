using Chaos.Client.Common.Abstractions;
using DALib.Drawing;
using DALib.Utility;

namespace Chaos.Client.Data.Repositories;

public class PanelItemRepository : RepositoryBase
{
    private const int IMAGES_PER_FILE = 266;
    private readonly PaletteLookup ItemPalettes = PaletteLookup.FromArchive("itempal", "item", DatArchives.Seo);

    private string ConstructKeyForItemSpriteSheet(int fileId) => $"ITEMS_{fileId}";

    private int GetFileNumber(int itemId) => Convert.ToInt32(Math.Ceiling((decimal)itemId / IMAGES_PER_FILE));

    public Palettized<EpfFrame>? GetItem(int itemId)
    {
        if (itemId == 0)
            return null;

        try
        {
            var fileId = GetFileNumber(itemId);
            var spriteSheet = GetItemSpriteSheet(fileId);
            var frameId = (itemId - 1) % IMAGES_PER_FILE;

            return new Palettized<EpfFrame>
            {
                Entity = spriteSheet[frameId],
                Palette = ItemPalettes.GetPaletteForId(itemId)
            };
        } catch
        {
            return null;
        }
    }

    private EpfFile GetItemSpriteSheet(int fileId)
        => GetOrCreate(ConstructKeyForItemSpriteSheet(fileId), () => LoadItemSpriteSheet(fileId));

    private EpfFile LoadItemSpriteSheet(int fileId) => EpfFile.FromArchive($"item{fileId:D3}", DatArchives.Legend);
}