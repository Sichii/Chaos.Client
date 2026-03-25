#region
using Chaos.Client.Data;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Pre-built texture atlases for English and Korean bitmap font glyphs. Glyphs are rasterized as white-on-transparent
///     so SpriteBatch vertex coloring can tint them to any color at draw time.
/// </summary>
public sealed class FontAtlas : IDisposable
{
    private const int GLYPH_HEIGHT = 12;

    private readonly TextureAtlas EnglishAtlas;
    private readonly AtlasRegion[] EnglishGlyphs;
    private readonly TextureAtlas KoreanAtlas;
    private readonly AtlasRegion[] KoreanGlyphs;

    public static FontAtlas Instance { get; private set; } = null!;

    private FontAtlas(GraphicsDevice device)
    {
        var fonts = DataContext.Fonts;

        EnglishAtlas = BuildAtlas(device, fonts.EnglishFont, out EnglishGlyphs);
        KoreanAtlas = BuildAtlas(device, fonts.KoreanFont, out KoreanGlyphs);
    }

    public void Dispose()
    {
        EnglishAtlas.Dispose();
        KoreanAtlas.Dispose();
    }

    private static TextureAtlas BuildAtlas(GraphicsDevice device, FntFile font, out AtlasRegion[] glyphs)
    {
        var atlas = new TextureAtlas(
            device,
            PackingMode.Grid,
            font.GlyphWidth,
            GLYPH_HEIGHT);
        var bytesPerRow = (font.GlyphWidth + 7) / 8;
        var bytesPerGlyph = bytesPerRow * GLYPH_HEIGHT;

        for (var i = 0; i < font.GlyphCount; i++)
        {
            var pixels = RasterizeGlyph(
                font.Data,
                i * bytesPerGlyph,
                bytesPerRow,
                font.GlyphWidth);

            atlas.Add(
                i,
                pixels,
                font.GlyphWidth,
                GLYPH_HEIGHT);
        }

        atlas.Build();

        glyphs = new AtlasRegion[font.GlyphCount];

        for (var i = 0; i < font.GlyphCount; i++)
            glyphs[i] = atlas.TryGetRegion(i) ?? default;

        return atlas;
    }

    public AtlasRegion GetEnglishGlyph(int glyphIndex) => EnglishGlyphs[glyphIndex];
    public AtlasRegion GetKoreanGlyph(int glyphIndex) => KoreanGlyphs[glyphIndex];

    public static void Initialize(GraphicsDevice device) => Instance = new FontAtlas(device);

    private static Color[] RasterizeGlyph(
        byte[] data,
        int offset,
        int bytesPerRow,
        int glyphWidth)
    {
        var pixels = new Color[glyphWidth * GLYPH_HEIGHT];

        for (var row = 0; row < GLYPH_HEIGHT; row++)
        {
            var rowOffset = offset + row * bytesPerRow;

            for (var byteIdx = 0; byteIdx < bytesPerRow; byteIdx++)
            {
                var dataByte = data[rowOffset + byteIdx];

                if (dataByte == 0)
                    continue;

                for (var bit = 7; bit >= 0; bit--)
                {
                    if ((dataByte & (1 << bit)) == 0)
                        continue;

                    var pixelX = byteIdx * 8 + (7 - bit);

                    if (pixelX >= glyphWidth)
                        continue;

                    pixels[row * glyphWidth + pixelX] = Color.White;
                }
            }
        }

        return pixels;
    }
}