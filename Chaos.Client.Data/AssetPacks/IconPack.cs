#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     An ability-icon asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-ID lookup via
///     <see cref="TryGetIconImage" />. Lookup is case-insensitive on the filename. Decoded <see cref="SKImage" />
///     results must be disposed by the caller; typical pattern is to run them through
///     <c>Chaos.Client.Rendering.TextureConverter.ToTexture2D</c> and cache the resulting <c>Texture2D</c>.
/// </summary>
public sealed class IconPack : IDisposable
{
    private readonly ZipArchive Archive;
    private readonly Dictionary<string, ZipArchiveEntry> EntryIndex;

    public AssetPackManifest Manifest { get; }

    internal IconPack(ZipArchive archive, AssetPackManifest manifest)
    {
        Archive = archive;
        Manifest = manifest;

        //pre-index entries by lowercase name for case-insensitive, O(1) lookup
        EntryIndex = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
            EntryIndex[entry.FullName] = entry;
    }

    /// <summary>
    ///     Attempts to decode the PNG for the given (prefix, spriteId) pair. Filename convention:
    ///     <c>{prefix}{spriteId:D4}.png</c> at the archive root (e.g. <c>skill0001.png</c>, matching legacy EPF
    ///     naming). Case-insensitive.
    /// </summary>
    /// <param name="prefix">Typically <c>"skill"</c> or <c>"spell"</c>.</param>
    /// <param name="spriteId">The 1-based sprite ID, matching the legacy EPF slot numbering.</param>
    /// <param name="image">Decoded image on success. Caller owns disposal.</param>
    /// <returns>True if the entry was found and decoded successfully.</returns>
    public bool TryGetIconImage(string prefix, int spriteId, out SKImage? image)
    {
        image = null;

        if (spriteId <= 0)
            return false;

        var name = $"{prefix}{spriteId:D4}.png";

        if (!EntryIndex.TryGetValue(name, out var entry))
            return false;

        try
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            image = SKImage.FromEncodedData(ms);

            return image is not null;
        }
        catch
        {
            //swallow decode errors — treat corrupt/truncated entries as "not present", caller falls back to legacy
            image?.Dispose();
            image = null;

            return false;
        }
    }

    public void Dispose() => Archive.Dispose();
}
