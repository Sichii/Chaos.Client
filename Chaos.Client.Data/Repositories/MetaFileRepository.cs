#region
using Chaos.Client.Data.Abstractions;
using DALib.Data;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class MetaFileRepository : RepositoryBase
{
    private readonly string MetaFileDirectory = Path.Combine(DataContext.DataPath, "metafile");

    public MetaFile? Get(string key)
    {
        try
        {
            return GetOrCreate(key, () => LoadMetaFile(key));
        } catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Returns all metadata files whose names start with the given prefix (e.g., "ItemInfo", "SClass").
    /// </summary>
    public IReadOnlyList<MetaFile> GetAll(string prefix)
    {
        var files = GetAvailableFiles();
        var results = new List<MetaFile>();

        foreach (var name in files)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var metaFile = Get(name);

            if (metaFile is not null)
                results.Add(metaFile);
        }

        return results;
    }

    /// <summary>
    ///     Returns the names of all metadata files available on disk.
    /// </summary>
    public IReadOnlyList<string> GetAvailableFiles()
    {
        if (!Directory.Exists(MetaFileDirectory))
            return [];

        return Directory.GetFiles(MetaFileDirectory)
                        .Select(Path.GetFileName)
                        .Where(name => name is not null)
                        .ToList()!;
    }

    private MetaFile LoadMetaFile(string key) => MetaFile.FromFile(Path.Combine(MetaFileDirectory, key), true);
}