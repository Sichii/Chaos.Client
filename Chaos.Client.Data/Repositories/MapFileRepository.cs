using Chaos.Client.Common.Abstractions;
using DALib.Data;
using DALib.Extensions;

namespace Chaos.Client.Data.Repositories;

public sealed class MapFileRepository : RepositoryBase
{
    public MapFile? Get(string key, int width, int height)
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
        var path = $"map/{key}";

        return MapFile.FromFile(path, width, height);
    }
}