using Chaos.Client.Common.Abstractions;
using Chaos.Client.Data.Models;
using DALib.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace Chaos.Client.Data.Repositories;

public class UserControlRepository : RepositoryBase
{
    /// <inheritdoc />
    protected override void ConfigureEntry(ICacheEntry entry) => entry.SetPriority(CacheItemPriority.NeverRemove);

    public UserControlDetails? Get(string key)
    {
        try
        {
            return GetOrCreate(key, () => LoadUserControlDetails(key));
        } catch
        {
            return null;
        }
    }

    private UserControlDetails? LoadUserControlDetails(string key)
    {
        key = key.WithExtension(".txt");

        if (DatArchives.Cious.TryGetValue(key, out var entry))
            return UserControlDetails.FromEntry(entry);

        if (DatArchives.Setoa.TryGetValue(key, out entry))
            return UserControlDetails.FromEntry(entry);

        return null;
    }
}