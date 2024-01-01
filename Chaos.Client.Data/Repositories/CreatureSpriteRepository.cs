using System.Collections.Frozen;
using Chaos.Client.Common.Abstractions;
using DALib.Drawing;
using DALib.Utility;

namespace Chaos.Client.Data.Repositories;

public class CreatureSpriteRepository : RepositoryBase
{
    private readonly IDictionary<int, Palette> Palettes = Palette.FromArchive("mns", DatArchives.Hades)
                                                                 .ToFrozenDictionary();

    public Palettized<MpfFile>? GetCreatureSprite(int spriteId)
    {
        if (spriteId == 0)
            return null;

        try
        {
            return GetOrCreate(spriteId.ToString(), () => LoadCreatureSprite(spriteId));
        } catch
        {
            return null;
        }
    }

    private Palettized<MpfFile> LoadCreatureSprite(int spriteId)
        => new()
        {
            Entity = MpfFile.FromArchive($"mns{spriteId:D3}", DatArchives.Hades),
            Palette = Palettes[spriteId]
        };
}