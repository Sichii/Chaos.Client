#region
using Chaos.Client.Data.Abstractions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class PanelSpriteRepository : RepositoryBase
{
    private const int ITEMS_PER_FILE = 266;

    private readonly Palette Gui06Palette = Palette.FromEntry(DatArchives.Setoa["gui06.pal"]);

    private readonly PaletteLookup ItemPalettes = PaletteLookup.FromArchive("itempal", "item", DatArchives.Legend)
                                                               .Freeze();

    private string ConstructKeyForItemSpriteSheet(int fileId) => $"ITEMS_{fileId}";

    private int GetFileNumber(int itemId) => Convert.ToInt32(Math.Ceiling((decimal)itemId / ITEMS_PER_FILE));

    private Palettized<EpfFrame>? GetIconFrame(int spriteId, string sheetName)
    {
        var iconSheet = GetOrCreate(sheetName, () => EpfView.FromArchive(sheetName, DatArchives.Setoa));

        if (!iconSheet.TryGetValue(spriteId, out var frame))
            return null;

        return new Palettized<EpfFrame>
        {
            Entity = frame!,
            Palette = Gui06Palette
        };
    }

    public Palettized<EpfFrame>? GetItemSprite(int itemId)
    {
        if (itemId == 0)
            return null;

        var fileId = GetFileNumber(itemId);
        var spriteSheet = GetItemSpriteSheet(fileId);
        var frameId = (itemId - 1) % ITEMS_PER_FILE;

        if (!spriteSheet.TryGetValue(frameId, out var frame))
            return null;

        return new Palettized<EpfFrame>
        {
            Entity = frame!,
            Palette = ItemPalettes.GetPaletteForId(itemId)
        };
    }

    private EpfView GetItemSpriteSheet(int fileId)
        => GetOrCreate(ConstructKeyForItemSpriteSheet(fileId), () => EpfView.FromArchive($"item{fileId:D3}", DatArchives.Legend));

    public Palettized<EpfFrame>? GetSkillIcon(int spriteId) => GetIconFrame(spriteId, "skill001");
    public Palettized<EpfFrame>? GetSkillLearnableIcon(int spriteId) => GetIconFrame(spriteId, "skill002");
    public Palettized<EpfFrame>? GetSkillLockedIcon(int spriteId) => GetIconFrame(spriteId, "skill003");
    public Palettized<EpfFrame>? GetSpellIcon(int spriteId) => GetIconFrame(spriteId, "spell001");
    public Palettized<EpfFrame>? GetSpellLearnableIcon(int spriteId) => GetIconFrame(spriteId, "spell002");
    public Palettized<EpfFrame>? GetSpellLockedIcon(int spriteId) => GetIconFrame(spriteId, "spell003");
}