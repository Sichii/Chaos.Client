using Chaos.Client.Common.Abstractions;
using DALib.Drawing;
using DALib.Utility;

namespace Chaos.Client.Data.Repositories;

public class EffectsRepository : RepositoryBase
{
    private readonly PaletteLookup EffectPalettes = PaletteLookup.FromArchive("effpal", "eff", DatArchives.Roh);

    public EfaFile? GetEfaFile(int effectId)
    {
        if (effectId == 0)
            return null;

        try
        {
            return GetOrCreate(effectId.ToString(), () => LoadEfaFile(effectId));
        } catch
        {
            return null;
        }
    }

    public Palettized<EpfFile>? GetEpfFile(int effectId)
    {
        if (effectId == 0)
            return null;

        try
        {
            return GetOrCreate(effectId.ToString(), () => LoadEpfFile(effectId));
        } catch
        {
            return null;
        }
    }

    private EfaFile LoadEfaFile(int effectId) => EfaFile.FromArchive($"eff{effectId:D3}", DatArchives.Roh);

    private Palettized<EpfFile> LoadEpfFile(int effectId)
        => new()
        {
            Entity = EpfFile.FromArchive($"eff{effectId:D3}", DatArchives.Roh),
            Palette = EffectPalettes.GetPaletteForId(effectId)
        };
}