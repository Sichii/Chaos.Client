#region
using Chaos.Client.Data.Abstractions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class PanelIconRepository : RepositoryBase
{
    private readonly Palette Gui06Palette = Palette.FromEntry(DatArchives.Setoa["gui06.pal"]);

    private Palettized<EpfFrame>? GetFrame(int spriteId, string sheetName)
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

    public Palettized<EpfFrame>? GetSkillIcon(int spriteId) => GetFrame(spriteId, "skill001");
    public Palettized<EpfFrame>? GetSkillLearnableIcon(int spriteId) => GetFrame(spriteId, "skill002");
    public Palettized<EpfFrame>? GetSkillLockedIcon(int spriteId) => GetFrame(spriteId, "skill003");
    public Palettized<EpfFrame>? GetSpellIcon(int spriteId) => GetFrame(spriteId, "spell001");
    public Palettized<EpfFrame>? GetSpellLearnableIcon(int spriteId) => GetFrame(spriteId, "spell002");
    public Palettized<EpfFrame>? GetSpellLockedIcon(int spriteId) => GetFrame(spriteId, "spell003");
}