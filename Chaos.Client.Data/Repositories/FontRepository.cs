#region
using DALib.Drawing;
#endregion

namespace Chaos.Client.Data.Repositories;

/// <summary>
///     Provides bitmap font data (FntFile) for text rendering. English (8x12) and Korean (16x12) fonts are loaded from
///     Legend.dat on first access.
/// </summary>
public sealed class FontRepository
{
    private const int ENGLISH_GLYPH_WIDTH = 8;
    private const int KOREAN_GLYPH_WIDTH = 16;
    private const int GLYPH_HEIGHT = 12;

    public FntFile EnglishFont { get; } = FntFile.FromArchive(
        "eng00",
        DatArchives.Legend,
        ENGLISH_GLYPH_WIDTH,
        GLYPH_HEIGHT);

    public FntFile KoreanFont { get; } = FntFile.FromArchive(
        "han00",
        DatArchives.Legend,
        KOREAN_GLYPH_WIDTH,
        GLYPH_HEIGHT);
}