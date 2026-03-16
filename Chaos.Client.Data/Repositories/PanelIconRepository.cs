#region
using Chaos.Client.Common.Abstractions;
using DALib.Drawing;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class PanelIconRepository : RepositoryBase
{
    private const int IMAGES_PER_FILE = 266;
    private readonly Palette Gui06Palette = Palette.FromEntry(DatArchives.Setoa["gui06.pal"]);
    private string ConstructKeyForSkillIconSheet(int fileId) => $"SKILLICONS_{fileId}";

    private string ConstructKeyForSpellIconSheet(int fileId) => $"SPELLICONS_{fileId}";

    private int GetFileNumber(int itemId) => Convert.ToInt32(Math.Ceiling((decimal)itemId / IMAGES_PER_FILE));

    private Palettized<EpfFrame>? GetIconVariant(
        int id,
        string cachePrefix,
        string filePrefix,
        int variantPage)
    {
        if (id == 0)
            return null;

        try
        {
            var fileId = GetFileNumber(id);
            var variantFileId = fileId + variantPage - 1;
            var cacheKey = $"{cachePrefix}_{variantFileId}";
            var iconSheet = GetOrCreate(cacheKey, () => EpfFile.FromArchive($"{filePrefix}{variantFileId:D3}", DatArchives.Setoa));
            var frameId = (id - 1) % IMAGES_PER_FILE;

            return new Palettized<EpfFrame>
            {
                Entity = iconSheet[frameId],
                Palette = Gui06Palette
            };
        } catch
        {
            return null;
        }
    }

    public Palettized<EpfFrame>? GetSkillBlueIcon(int skillId)
        => GetIconVariant(
            skillId,
            "SKILLBLUE",
            "skill",
            2);

    public Palettized<EpfFrame>? GetSkillGreyIcon(int skillId)
        => GetIconVariant(
            skillId,
            "SKILLGREY",
            "skill",
            3);

    public Palettized<EpfFrame>? GetSkillIcon(int skillId)
    {
        if (skillId == 0)
            return null;

        try
        {
            var fileId = GetFileNumber(skillId);
            var iconSheet = GetSkillIconSheet(fileId);
            var frameId = (skillId - 1) % IMAGES_PER_FILE;

            return new Palettized<EpfFrame>
            {
                Entity = iconSheet[frameId],
                Palette = Gui06Palette
            };
        } catch
        {
            return null;
        }
    }

    private EpfFile GetSkillIconSheet(int fileId) => GetOrCreate(ConstructKeyForSkillIconSheet(fileId), () => LoadSkillIconSheet(fileId));

    public Palettized<EpfFrame>? GetSpellBlueIcon(int spellId)
        => GetIconVariant(
            spellId,
            "SPELLBLUE",
            "spell",
            2);

    public Palettized<EpfFrame>? GetSpellIcon(int spellId)
    {
        if (spellId == 0)
            return null;

        try
        {
            var fileId = GetFileNumber(spellId);
            var iconSheet = GetSpellIconSheet(fileId);
            var frameId = (spellId - 1) % IMAGES_PER_FILE;

            return new Palettized<EpfFrame>
            {
                Entity = iconSheet[frameId],
                Palette = Gui06Palette
            };
        } catch
        {
            return null;
        }
    }

    private EpfFile GetSpellIconSheet(int fileId) => GetOrCreate(ConstructKeyForSpellIconSheet(fileId), () => LoadSpellIconSheet(fileId));
    private EpfFile LoadSkillIconSheet(int fileId) => EpfFile.FromArchive($"skill{fileId:D3}", DatArchives.Setoa);

    private EpfFile LoadSpellIconSheet(int fileId) => EpfFile.FromArchive($"spell{fileId:D3}", DatArchives.Setoa);
}