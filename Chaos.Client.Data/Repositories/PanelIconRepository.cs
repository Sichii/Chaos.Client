using Chaos.Client.Common.Abstractions;
using DALib.Drawing;
using DALib.Utility;

namespace Chaos.Client.Data.Repositories;

public class PanelIconRepository : RepositoryBase
{
    private const int IMAGES_PER_FILE = 266;
    private readonly Palette Gui06Palette = Palette.FromEntry(DatArchives.Setoa["gui06.pal"]);
    private string ConstructKeyForSkillIconSheet(int fileId) => $"SKILLICONS_{fileId}";

    private string ConstructKeyForSpellIconSheet(int fileId) => $"SPELLICONS_{fileId}";

    private int GetFileNumber(int itemId) => Convert.ToInt32(Math.Ceiling((decimal)itemId / IMAGES_PER_FILE));

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
    private EpfFile LoadSkillIconSheet(int fileId) => EpfFile.FromArchive($"skill{fileId:D3}", DatArchives.Legend);

    private EpfFile LoadSpellIconSheet(int fileId) => EpfFile.FromArchive($"spell{fileId:D3}", DatArchives.Legend);
}