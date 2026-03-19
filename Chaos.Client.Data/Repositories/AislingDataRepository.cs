#region
using System.Collections.Frozen;
using DALib.Data;
using DALib.Drawing;
#endregion

namespace Chaos.Client.Data.Repositories;

/// <summary>
///     Provides aisling palette data, dye color table, and equipment EPF loading from Khan archives. Palette lookups are
///     loaded once and frozen. EPF files are cached per session and cleared on map change via <see cref="ClearEpfCache" />
///     .
/// </summary>
public sealed class AislingDataRepository
{
    private readonly Dictionary<string, EpfFile?> EpfCache = new();

    public IDictionary<int, Palette> BodyPalettes { get; } = Palette.FromArchive("palm", DatArchives.Khanpal)
                                                                    .ToFrozenDictionary();

    public ColorTable DyeColorTable { get; }

    public PaletteLookup PalB { get; } = PaletteLookup.FromArchive("palb", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalC { get; } = PaletteLookup.FromArchive("palc", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalE { get; } = PaletteLookup.FromArchive("pale", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalF { get; } = PaletteLookup.FromArchive("palf", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalH { get; } = PaletteLookup.FromArchive("palh", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalI { get; } = PaletteLookup.FromArchive("pali", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalL { get; } = PaletteLookup.FromArchive("pall", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalP { get; } = PaletteLookup.FromArchive("palp", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalU { get; } = PaletteLookup.FromArchive("palu", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalW { get; } = PaletteLookup.FromArchive("palw", DatArchives.Khanpal)
                                                      .Freeze();

    public AislingDataRepository()
    {
        if (DatArchives.Legend.TryGetValue("color0.tbl", out var entry))
            DyeColorTable = ColorTable.FromEntry(entry);
        else
            DyeColorTable = new ColorTable();
    }

    /// <summary>
    ///     Clears the cached EPF files. Call on map change to free memory.
    /// </summary>
    public void ClearEpfCache() => EpfCache.Clear();

    private static DataArchive GetArchive(char typeLetter, bool isMale)
        => typeLetter switch
        {
            >= 'a' and <= 'd' => isMale ? DatArchives.Khanmad : DatArchives.Khanwad,
            >= 'e' and <= 'h' => isMale ? DatArchives.Khanmeh : DatArchives.Khanweh,
            >= 'i' and <= 'm' => isMale ? DatArchives.Khanmim : DatArchives.Khanwim,
            >= 'n' and <= 's' => isMale ? DatArchives.Khanmns : DatArchives.Khanwns,
            >= 't' and <= 'z' => isMale ? DatArchives.Khanmtz : DatArchives.Khanwtz,
            _                 => isMale ? DatArchives.Khanmad : DatArchives.Khanwad
        };

    /// <summary>
    ///     Loads an equipment EPF file from the appropriate Khan archive, with caching. The archive is determined by the type
    ///     letter and gender.
    /// </summary>
    /// <param name="typeLetter">
    ///     Equipment type letter (a-z) that determines which Khan archive to use
    /// </param>
    /// <param name="isMale">
    ///     Whether to use the male or female Khan archive variant
    /// </param>
    /// <param name="fileName">
    ///     EPF file name without extension (e.g. "ma00101")
    /// </param>
    public EpfFile? GetEquipmentEpf(char typeLetter, bool isMale, string fileName)
    {
        if (EpfCache.TryGetValue(fileName, out var cached))
            return cached;

        var archive = GetArchive(typeLetter, isMale);

        if (!archive.TryGetValue($"{fileName}.epf", out var entry))
        {
            EpfCache[fileName] = null;

            return null;
        }

        var epf = EpfFile.FromEntry(entry);
        EpfCache[fileName] = epf;

        return epf;
    }

    /// <summary>
    ///     Returns the palette lookup for the given equipment type letter.
    /// </summary>
    public PaletteLookup GetPaletteLookup(char typeLetter)
        => typeLetter switch
        {
            'a' or 'b' or 'n' => PalB,
            'c' or 'g' or 'j' => PalC,
            'e'               => PalE,
            'f'               => PalF,
            'h'               => PalH,
            'i'               => PalI,
            'l'               => PalL,
            'p' or 's'        => PalP,
            'u'               => PalU,
            'w'               => PalW,
            _                 => PalB
        };
}