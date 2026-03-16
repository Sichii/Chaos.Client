#region
using Chaos.Client.Common.Abstractions;
using DALib.Data;
using DALib.Extensions;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class MapFileRepository : RepositoryBase
{
    public MapFile? GetMapFile(string key, int width, int height)
    {
        try
        {
            key = key.WithExtension(".map");

            return GetOrCreate(key, () => LoadMapFile(key, width, height));
        } catch
        {
            return null;
        }
    }

    private MapFile LoadMapFile(string key, int width, int height)
    {
        var path = Path.Combine(DataContext.DataPath, "maps", key);

        return MapFile.FromFile(path, width, height);
    }
}