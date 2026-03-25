#region
using DALib.Data;
#endregion

namespace Chaos.Client.Data.Models;

/// <summary>
///     A single parsed item from an ItemInfo metadata file.
/// </summary>
public sealed record ItemMetadataEntry
{
    public string Category { get; init; } = string.Empty;
    public byte Class { get; init; }
    public string Description { get; init; } = string.Empty;
    public int Level { get; init; }
    public required string Name { get; init; }
    public int Weight { get; init; }

    /// <summary>
    ///     Parses all entries from one or more ItemInfo MetaFiles into a single list.
    /// </summary>
    public static IReadOnlyList<ItemMetadataEntry> ParseAll(IEnumerable<MetaFile> metaFiles)
    {
        var items = new List<ItemMetadataEntry>();

        foreach (var metaFile in metaFiles)
            foreach (var entry in metaFile)
            {
                if (entry.Properties.Count < 5)
                    continue;

                int.TryParse(entry.Properties[0], out var level);
                byte.TryParse(entry.Properties[1], out var cls);
                int.TryParse(entry.Properties[2], out var weight);

                items.Add(
                    new ItemMetadataEntry
                    {
                        Name = entry.Key,
                        Level = level,
                        Class = cls,
                        Weight = weight,
                        Category = entry.Properties[3],
                        Description = entry.Properties[4]
                    });
            }

        return items;
    }
}