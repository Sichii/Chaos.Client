#region
using Chaos.Client.Data.Models;
using DALib.Data;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class MetaFileRepository
{
    private readonly string MetaFileDirectory = Path.Combine(DataContext.DataPath, "metafile");

    /// <summary>
    ///     Loads a raw MetaFile from disk by name.
    /// </summary>
    public MetaFile? Get(string name)
    {
        var filePath = Path.Combine(MetaFileDirectory, name);

        if (!File.Exists(filePath))
            return null;

        try
        {
            return MetaFile.FromFile(filePath, true);
        } catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Loads and parses the ability metadata for a given base class (SClass{N}).
    /// </summary>
    public AbilityMetadata? GetAbilityMetadata(byte baseClass)
    {
        var metaFile = Get($"SClass{baseClass}");

        if (metaFile is null or { Count: 0 })
            return null;

        return AbilityMetadata.Parse(metaFile);
    }

    /// <summary>
    ///     Returns all metadata files whose names start with the given prefix (e.g., "ItemInfo", "SClass").
    /// </summary>
    public IReadOnlyList<MetaFile> GetAll(string prefix)
    {
        if (!Directory.Exists(MetaFileDirectory))
            return [];

        var results = new List<MetaFile>();

        foreach (var filePath in Directory.GetFiles(MetaFileDirectory))
        {
            var fileName = Path.GetFileName(filePath);

            if (fileName is null || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                results.Add(MetaFile.FromFile(filePath, true));
            } catch
            {
                // Skip corrupt files
            }
        }

        return results;
    }

    /// <summary>
    ///     Loads and parses all event metadata (SEvent1, SEvent2, ...).
    /// </summary>
    public IReadOnlyList<EventMetadataEntry> GetEventMetadata() => EventMetadataEntry.ParseAll(GetAll("SEvent"));

    /// <summary>
    ///     Loads and parses all item metadata (ItemInfo0, ItemInfo1, ...).
    /// </summary>
    public IReadOnlyList<ItemMetadataEntry> GetItemMetadata() => ItemMetadataEntry.ParseAll(GetAll("ItemInfo"));

    /// <summary>
    ///     Loads and parses the "Light" metadata file.
    /// </summary>
    public LightMetadata? GetLightMetadata()
    {
        var metaFile = Get("Light");

        if (metaFile is null or { Count: 0 })
            return null;

        return LightMetadata.Parse(metaFile);
    }

    /// <summary>
    ///     Loads and parses the "NationDesc" metadata file.
    /// </summary>
    public NationMetadata? GetNationMetadata()
    {
        var metaFile = Get("NationDesc");

        if (metaFile is null or { Count: 0 })
            return null;

        return NationMetadata.Parse(metaFile);
    }

    /// <summary>
    ///     Loads and parses the "NPCIllust" metadata file.
    /// </summary>
    public NpcIllustrationMetadata? GetNpcIllustrationMetadata()
    {
        var metaFile = Get("NPCIllust");

        if (metaFile is null or { Count: 0 })
            return null;

        return NpcIllustrationMetadata.Parse(metaFile);
    }
}