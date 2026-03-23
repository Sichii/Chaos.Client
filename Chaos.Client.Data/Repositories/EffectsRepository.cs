#region
using Chaos.Client.Data.Abstractions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class EffectsRepository : RepositoryBase
{
    private readonly PaletteLookup EffectPalettes = PaletteLookup.FromArchive("effpal", "eff", DatArchives.Roh)
                                                                 .Freeze();

    private readonly EffectTable EffectTable;

    public EffectsRepository()
    {
        if (DatArchives.Roh.TryGetValue("effect.tbl", out _))
            EffectTable = EffectTable.FromArchive(DatArchives.Roh);
        else
            EffectTable = new EffectTable();
    }

    /// <summary>
    ///     Returns an EFA effect file by ID, or null if not found.
    /// </summary>
    public EfaView? GetEfaEffect(int effectId)
    {
        if (effectId == 0)
            return null;

        var entryName = $"efct{effectId:D3}.efa";

        if (!DatArchives.Roh.TryGetValue(entryName, out var entry))
            return null;

        return GetOrCreate($"efa_{effectId}", () => EfaView.FromEntry(entry));
    }

    /// <summary>
    ///     Loads per-frame center points from an effect's .tbl file (e.g. eff246.tbl). Returns null if no .tbl file exists for
    ///     this effect.
    /// </summary>
    public (short X, short Y)[]? GetEffectCenterPoints(int effectId, int frameCount)
    {
        if (effectId <= 0)
            return null;

        var tblName = $"efct{effectId:D3}.tbl";

        if (!DatArchives.Roh.TryGetValue(tblName, out var tblEntry))
            return null;

        using var stream = tblEntry.ToStreamSegment();
        using var reader = new BinaryReader(stream);

        var points = new (short X, short Y)[frameCount];

        for (var i = 0; (i < frameCount) && (stream.Position < stream.Length); i++)
        {
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            points[i] = (x, y);
        }

        // Fill remaining with last point if .tbl is shorter than frame count
        if (frameCount > 0)
        {
            var lastValid = points[0];

            for (var i = 0; i < frameCount; i++)
                if (points[i] != default)
                    lastValid = points[i];
                else
                    points[i] = lastValid;
        }

        return points;
    }

    /// <summary>
    ///     Returns the frame sequence for an effect from the EffectTable. A single-element [0] means EFA. Returns null if the
    ///     effect ID has no entry.
    /// </summary>
    public EffectTableEntry? GetEffectTableEntry(int effectId)
    {
        if (effectId <= 0)
            return null;

        return EffectTable.TryGetEntry(effectId, out var entry) ? entry : null;
    }

    /// <summary>
    ///     Returns an EPF effect file with palette by ID, or null if not found.
    /// </summary>
    public Palettized<EpfView>? GetEpfEffect(int effectId)
    {
        if (effectId == 0)
            return null;

        var entryName = $"efct{effectId:D3}.epf";

        if (!DatArchives.Roh.TryGetValue(entryName, out var entry))
            return null;

        return GetOrCreate(
            $"epf_{effectId}",
            () => new Palettized<EpfView>
            {
                Entity = EpfView.FromEntry(entry),
                Palette = EffectPalettes.GetPaletteForId(effectId)
            });
    }
}