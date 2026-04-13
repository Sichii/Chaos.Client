#region
using Microsoft.Extensions.Caching.Memory;
#endregion

namespace Chaos.Client.Data.Abstractions;

public abstract class RepositoryBase
{
    protected MemoryCache Cache { get; } = new(new MemoryCacheOptions());

    private readonly Lock CacheLock = new();

    protected virtual void ConfigureEntry(ICacheEntry entry)
        => entry.SetPriority(CacheItemPriority.Normal)
                .SetSlidingExpiration(TimeSpan.FromMinutes(15))
                .RegisterPostEvictionCallback(HandleDisposableEntries);

    protected virtual T GetOrCreate<T>(string key, Func<T> factory)
    {
        if (Cache.TryGetValue(key, out T? cached))
            return cached!;

        using var scope = CacheLock.EnterScope();

        return Cache.GetOrCreate(
            key,
            entry =>
            {
                ConfigureEntry(entry);

                return factory();
            }) ?? throw new NullReferenceException();
    }

    protected virtual T GetOrCreate<T>(int key, Func<T> factory)
    {
        if (Cache.TryGetValue(key, out T? cached))
            return cached!;

        using var scope = CacheLock.EnterScope();

        return Cache.GetOrCreate(
            key,
            entry =>
            {
                ConfigureEntry(entry);

                return factory();
            }) ?? throw new NullReferenceException();
    }

    protected static void HandleDisposableEntries(
        object key,
        object? value,
        EvictionReason reason,
        object? state)
    {
        if (value is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    ///     Removes a cached entry by key.
    /// </summary>
    public void Invalidate(string key) => Cache.Remove(key);
}
