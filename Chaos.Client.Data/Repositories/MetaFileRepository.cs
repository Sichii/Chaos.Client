#region
using Chaos.Client.Data.Abstractions;
using DALib.Data;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class MetaFileRepository : RepositoryBase
{
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

    private MetaFile LoadMetaFile(string key) => MetaFile.FromFile(Path.Combine(DataContext.DataPath, "metafile", key), true);
}