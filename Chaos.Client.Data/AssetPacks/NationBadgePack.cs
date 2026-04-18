#region
using System.IO.Compression;
using SkiaSharp;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     A nation-badge asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-nation lookup via
///     <see cref="TryGetBadgeImage" />. Filename convention: <c>nation{nationId:D4}.png</c> at the archive root
///     (1-based, matching the legacy <c>_nui_nat.spf</c> frame-index-plus-one convention). Decoded
///     <see cref="SKImage" /> results must be disposed by the caller.
/// </summary>
public sealed class NationBadgePack : IDisposable
{
    private readonly ZipArchive Archive;
    private readonly Dictionary<string, ZipArchiveEntry> EntryIndex;

    public AssetPackManifest Manifest { get; }

    internal NationBadgePack(ZipArchive archive, AssetPackManifest manifest)
    {
        Archive = archive;
        Manifest = manifest;

        EntryIndex = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
            EntryIndex[entry.FullName] = entry;
    }

    /// <summary>
    ///     Attempts to decode the PNG for the given nation ID. Returns false if the entry isn't present, decode
    ///     fails, or the entry is malformed — caller should fall back to the legacy <c>_nui_nat.spf</c> frame.
    /// </summary>
    public bool TryGetBadgeImage(byte nationId, out SKImage? image)
    {
        image = null;

        if (nationId == 0)
            return false;

        var name = $"nation{nationId:D4}.png";

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
            image?.Dispose();
            image = null;

            return false;
        }
    }

    public void Dispose() => Archive.Dispose();
}
