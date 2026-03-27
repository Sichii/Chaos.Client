#region
using System.Text;
using Chaos.Client.Data.Models;
using DALib.Data;
#endregion

namespace Chaos.Client.Data.Utilities;

/// <summary>
///     Parses Skill_e.tbl and Skill_i.tbl from Legend.dat into an <see cref="AbilityAnimationTable" />. Each file is a
///     tab-delimited text table (EUC-KR encoded) mapping skill animation indices to sets of armor sprite IDs that support
///     the animation.
/// </summary>
public static class AbilityAnimationTableParser
{
    private static readonly Encoding EucKr = Encoding.GetEncoding(949);

    /// <summary>
    ///     Parses both .tbl entries into a single merged table. The normalEntry (Skill_e.tbl) provides u-type armor IDs. The
    ///     overcoatEntry (Skill_i.tbl) provides i-type armor IDs stored with a +1000 offset.
    /// </summary>
    public static AbilityAnimationTable Parse(DataArchiveEntry normalEntry, DataArchiveEntry? overcoatEntry)
    {
        var data = new Dictionary<byte, HashSet<int>>();

        ParseInto(data, normalEntry, 0);

        if (overcoatEntry is not null)
            ParseInto(data, overcoatEntry, 1000);

        return new AbilityAnimationTable(data);
    }

    private static void ParseInto(Dictionary<byte, HashSet<int>> data, DataArchiveEntry entry, int idOffset)
    {
        var text = EucKr.GetString(entry.ToSpan());

        var lines = text.Split(
            [
                '\r',
                '\n'
            ],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.StartsWith(';'))
                continue;

            var columns = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            if (columns.Length < 5)
                continue;

            if (!byte.TryParse(columns[0], out var entryIndex))
                continue;

            if (!data.TryGetValue(entryIndex, out var allowedIds))
            {
                allowedIds = [];
                data[entryIndex] = allowedIds;
            }

            // Columns 4+ are space-separated armor sprite IDs (may span multiple tab columns)
            for (var i = 4; i < columns.Length; i++)
            {
                var tokens = columns[i]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var token in tokens)
                    if (int.TryParse(token, out var armorId))
                        allowedIds.Add(armorId + idOffset);
            }
        }
    }
}