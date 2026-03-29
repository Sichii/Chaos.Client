#region
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Extensions;
using DALib.IO;
#endregion

namespace Chaos.Client.Data.Models;

/// <summary>
///     Holds the raw bytes of an HPF archive entry, deferring decompression until <see cref="Decompress" /> is called.
///     This allows archive reads (sequential, not thread-safe) to be separated from decompression (CPU-bound,
///     parallelizable).
/// </summary>
public sealed class CompressedHpfFile
{
    private readonly byte[] RawBytes;

    /// <summary>
    ///     For uncompressed files, returns the pixel height. For compressed files, returns 0 (height unknown until
    ///     decompressed).
    /// </summary>
    public int EstimatedPixelHeight => IsCompressed ? 0 : (RawBytes.Length - 8) / CONSTANTS.HPF_TILE_WIDTH;

    /// <summary>
    ///     The pixel height of the image. Only valid after decompression for compressed files, but can be estimated from the
    ///     raw entry size for uncompressed files.
    /// </summary>
    public bool IsCompressed
        => (RawBytes.Length >= 4) && (RawBytes[0] == 0x55) && (RawBytes[1] == 0xAA) && (RawBytes[2] == 0x02) && (RawBytes[3] == 0xFF);

    private CompressedHpfFile(byte[] rawBytes) => RawBytes = rawBytes;

    /// <summary>
    ///     Decompresses the raw bytes into an HpfFile. Thread-safe — no archive access required.
    /// </summary>
    public HpfFile Decompress()
    {
        Span<byte> buffer = RawBytes;

        if (IsCompressed)
            Compression.DecompressHpf(ref buffer);

        return new HpfFile(
            buffer[..8]
                .ToArray(),
            buffer[8..]
                .ToArray());
    }

    public static CompressedHpfFile FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new CompressedHpfFile(segment.ToArray());
    }
}