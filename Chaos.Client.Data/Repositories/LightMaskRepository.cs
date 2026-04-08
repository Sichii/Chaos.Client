#region
using Chaos.Client.Data.Abstractions;
using Chaos.Client.Data.Models;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using Microsoft.Extensions.Caching.Memory;
#endregion

namespace Chaos.Client.Data.Repositories;

public sealed class LightMaskRepository : RepositoryBase
{
    protected override void ConfigureEntry(ICacheEntry entry) => entry.SetPriority(CacheItemPriority.NeverRemove);

    public LightMask? Get(LanternSize size)
        => size switch
        {
            LanternSize.Small => Get("mask101"),
            LanternSize.Large => Get("mask102"),
            _                 => null
        };

    public LightMask Get(string epfName)
        => GetOrCreate(
            epfName,
            () =>
            {
                if (!DatArchives.Legend.TryGetValue($"{epfName}.epf", out var entry))
                    return null!;

                var epf = EpfFile.FromEntry(entry);

                if (epf.Count == 0)
                    return null!;

                var frame = epf[0];

                return new LightMask
                {
                    Pixels = frame.Data,
                    Width = frame.PixelWidth,
                    Height = frame.PixelHeight
                };
            });
}