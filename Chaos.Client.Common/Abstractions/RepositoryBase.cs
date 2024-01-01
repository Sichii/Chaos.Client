using Chaos.Extensions.Client.Common;
using Microsoft.Extensions.Caching.Memory;

namespace Chaos.Client.Common.Abstractions;

public abstract class RepositoryBase
{
    protected MemoryCache Cache { get; } = new(new MemoryCacheOptions());

    protected virtual void ConfigureEntry(ICacheEntry entry)
        => entry.SetPriority(CacheItemPriority.Normal)
                .SetSlidingExpiration(TimeSpan.FromMinutes(15))
                .RegisterPostEvictionCallback(HandleDisposableEntries);

    protected virtual T GetOrCreate<T>(string key, Func<T> factory)
        => Cache.SafeGetOrCreate(
            key,
            entry =>
            {
                ConfigureEntry(entry);

                return factory();
            });

    protected static void HandleDisposableEntries(
        object key,
        object? value,
        EvictionReason reason,
        object? state)
    {
        if (value is IDisposable disposable)
            disposable.Dispose();
    }
}