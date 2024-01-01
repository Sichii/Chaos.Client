using Chaos.Client.Common.Abstractions;
using DALib.Data;

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

    private MetaFile LoadMetaFile(string key) => MetaFile.FromFile($"metafile/{key}", true);
}