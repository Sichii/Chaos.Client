#region
using System.Collections.Frozen;
using Chaos.Client.Data.Abstractions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class CreatureSpriteRepository : RepositoryBase
{
    private readonly IDictionary<int, Palette> Palettes = Palette.FromArchive("mns", DatArchives.Hades)
                                                                 .ToFrozenDictionary();

    public Palettized<MpfView>? GetCreatureSprite(int spriteId)
    {
        if (spriteId == 0)
            return null;

        try
        {
            return GetOrCreate(spriteId, () => LoadCreatureSprite(spriteId));
        } catch
        {
            return null;
        }
    }

    private Palettized<MpfView>? LoadCreatureSprite(int spriteId)
    {
        if (!DatArchives.Hades.TryGetValue($"mns{spriteId:D3}.mpf", out var entry))
            return null;

        var mpfFile = MpfView.FromEntry(entry);

        if (Palettes.TryGetValue(mpfFile.PaletteNumber, out var palette))
            return new Palettized<MpfView>
            {
                Entity = mpfFile,
                Palette = palette
            };

        return null;
    }
}