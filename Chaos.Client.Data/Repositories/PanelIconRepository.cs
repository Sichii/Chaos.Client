#region
using Chaos.Client.Common.Abstractions;
using DALib.Drawing;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class PanelIconRepository : RepositoryBase
{
    private readonly Palette Gui06Palette = Palette.FromEntry(DatArchives.Setoa["gui06.pal"]);

    private Palettized<EpfFrame>? GetFrame(int spriteId, string sheetName)
    {
        try
        {
            var iconSheet = GetOrCreate(sheetName, () => EpfFile.FromArchive(sheetName, DatArchives.Setoa));

            if ((spriteId < 0) || (spriteId >= iconSheet.Count))
                return null;

            return new Palettized<EpfFrame>
            {
                Entity = iconSheet[spriteId],
                Palette = Gui06Palette
            };
        } catch
        {
            return null;
        }
    }

    public Palettized<EpfFrame>? GetSkillGreyIcon(int spriteId) => GetFrame(spriteId, "skill003");

    public Palettized<EpfFrame>? GetSkillIcon(int spriteId) => GetFrame(spriteId, "skill001");
    public Palettized<EpfFrame>? GetSpellIcon(int spriteId) => GetFrame(spriteId, "spell001");
}