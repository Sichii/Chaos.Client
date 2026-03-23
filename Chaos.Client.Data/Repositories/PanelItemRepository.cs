#region
using Chaos.Client.Data.Abstractions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class PanelItemRepository : RepositoryBase
{
    private const int IMAGES_PER_FILE = 266;

    private readonly PaletteLookup ItemPalettes = PaletteLookup.FromArchive("itempal", "item", DatArchives.Legend)
                                                               .Freeze();

    private string ConstructKeyForItemSpriteSheet(int fileId) => $"ITEMS_{fileId}";

    private int GetFileNumber(int itemId) => Convert.ToInt32(Math.Ceiling((decimal)itemId / IMAGES_PER_FILE));

    private EpfView GetItemSpriteSheet(int fileId)
        => GetOrCreate(ConstructKeyForItemSpriteSheet(fileId), () => EpfView.FromArchive($"item{fileId:D3}", DatArchives.Legend));

    public Palettized<EpfFrame>? GetPanelItemSprite(int itemId)
    {
        if (itemId == 0)
            return null;

        var fileId = GetFileNumber(itemId);
        var spriteSheet = GetItemSpriteSheet(fileId);
        var frameId = (itemId - 1) % IMAGES_PER_FILE;

        if (!spriteSheet.TryGetValue(frameId, out var frame))
            return null;

        return new Palettized<EpfFrame>
        {
            Entity = frame!,
            Palette = ItemPalettes.GetPaletteForId(itemId)
        };
    }
}