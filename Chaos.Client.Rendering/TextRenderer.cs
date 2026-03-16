#region
using System.Text;
using System.Text.RegularExpressions;
using Chaos.Client.Data;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders text strings to Texture2D using bitmap fonts from Legend.dat. Supports mixed English (8x12) and Korean
///     (16x12) text via codepage 949.
/// </summary>
public static class TextRenderer
{
    private const int ENGLISH_GLYPH_WIDTH = 8;
    private const int KOREAN_GLYPH_WIDTH = 16;
    private const int GLYPH_HEIGHT = 12;
    private const int ENGLISH_ADVANCE = ENGLISH_GLYPH_WIDTH - 2;
    private const int KOREAN_ADVANCE = KOREAN_GLYPH_WIDTH - 2;

    private static FntFile? EnglishFont;
    private static FntFile? KoreanFont;
    private static Encoding? KoreanEncoding;

    private static void DrawTextLine(
        Span<byte> pixelBuffer,
        int bufferWidth,
        string text,
        int startX,
        int startY,
        SKColor color)
    {
        var cursorX = startX;

        foreach (var c in text)
        {
            if (c is >= (char)33 and <= (char)126)
            {
                Graphics.DrawGlyph(
                    EnglishFont!,
                    pixelBuffer,
                    bufferWidth,
                    c - 33,
                    cursorX,
                    startY,
                    color);
                cursorX += ENGLISH_ADVANCE;

                continue;
            }

            if (IsKorean(c))
            {
                var koreanIndex = GetKoreanGlyphIndex(c);

                if (koreanIndex >= 0)
                    Graphics.DrawGlyph(
                        KoreanFont!,
                        pixelBuffer,
                        bufferWidth,
                        koreanIndex,
                        cursorX,
                        startY,
                        color);

                cursorX += KOREAN_ADVANCE;

                continue;
            }

            // Space or unmapped — advance without drawing
            cursorX += ENGLISH_ADVANCE;
        }
    }

    private static void EnsureFontsLoaded()
    {
        if (EnglishFont is not null)
            return;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        EnglishFont = FntFile.FromArchive(
            "eng00",
            DatArchives.Legend,
            ENGLISH_GLYPH_WIDTH,
            GLYPH_HEIGHT);

        KoreanFont = FntFile.FromArchive(
            "han00",
            DatArchives.Legend,
            KOREAN_GLYPH_WIDTH,
            GLYPH_HEIGHT);
        KoreanEncoding = Encoding.GetEncoding(949);
    }

    /// <summary>
    ///     Maps a Unicode character to its Korean font glyph index via codepage 949 (EUC-KR).
    /// </summary>
    private static int GetKoreanGlyphIndex(char c)
    {
        Span<char> chars = [c];
        Span<byte> bytes = stackalloc byte[2];

        var count = KoreanEncoding!.GetBytes(chars, bytes);

        if (count != 2)
            return -1;

        var lead = bytes[0];
        var trail = bytes[1];

        // Hangul Jamo: lead 0xA4, trail 0xA1-0xD3 -> indices 0-50
        if ((lead == 0xA4) && trail is >= 0xA1 and <= 0xD3)
            return trail - 0xA1;

        // Hangul syllables: lead 0xB0-0xC8, trail 0xA1-0xFE -> indices 51-2400
        if (lead is >= 0xB0 and <= 0xC8 && trail is >= 0xA1 and <= 0xFE)
            return 51 + (lead - 0xB0) * 94 + (trail - 0xA1);

        return -1;
    }

    private static bool IsKorean(char c) => c > 127;

    /// <summary>
    ///     Returns the advance width of a single character.
    /// </summary>
    public static int MeasureCharWidth(char c) => IsKorean(c) ? KOREAN_ADVANCE : ENGLISH_ADVANCE;

    /// <summary>
    ///     Measures the pixel width of a text string.
    /// </summary>
    public static int MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        EnsureFontsLoaded();

        var width = 0;

        foreach (var c in text)
            width += IsKorean(c) ? KOREAN_ADVANCE : ENGLISH_ADVANCE;

        return width;
    }

    /// <summary>
    ///     Overload for backward compatibility with callers that pass fontSize/fontFamily (both ignored).
    /// </summary>
    public static float MeasureWidth(string text, float fontSize, string fontFamily = "") => MeasureWidth(text);

    /// <summary>
    ///     Renders a single line of text to a Texture2D using the game's bitmap font.
    /// </summary>
    public static Texture2D RenderText(
        GraphicsDevice device,
        string text,
        float fontSize = 0,
        Color? color = null,
        string fontFamily = "")
    {
        if (string.IsNullOrEmpty(text))
            text = " ";

        EnsureFontsLoaded();

        var textColor = color ?? Color.White;

        var skColor = new SKColor(
            textColor.R,
            textColor.G,
            textColor.B,
            textColor.A);
        var width = Math.Max(1, MeasureWidth(text));

        var pixelBuffer = new byte[width * GLYPH_HEIGHT * 4];

        DrawTextLine(
            pixelBuffer,
            width,
            text,
            0,
            0,
            skColor);

        var texture = new Texture2D(
            device,
            width,
            GLYPH_HEIGHT,
            false,
            SurfaceFormat.Color);
        texture.SetData(pixelBuffer);

        return texture;
    }

    /// <summary>
    ///     Renders word-wrapped text into a bounded area and returns a Texture2D.
    /// </summary>
    public static Texture2D RenderWrappedText(
        GraphicsDevice device,
        string text,
        int maxWidth,
        int maxHeight,
        float fontSize = 0,
        Color? color = null,
        string fontFamily = "")
    {
        if (string.IsNullOrEmpty(text))
            text = " ";

        EnsureFontsLoaded();

        var textColor = color ?? Color.White;

        var skColor = new SKColor(
            textColor.R,
            textColor.G,
            textColor.B,
            textColor.A);
        var lines = WrapText(text, maxWidth);

        var surfaceWidth = Math.Max(1, maxWidth);
        var surfaceHeight = Math.Max(1, maxHeight);

        var pixelBuffer = new byte[surfaceWidth * surfaceHeight * 4];

        var y = 0;

        foreach (var line in lines)
        {
            if ((y + GLYPH_HEIGHT) > surfaceHeight)
                break;

            DrawTextLine(
                pixelBuffer,
                surfaceWidth,
                line,
                0,
                y,
                skColor);
            y += GLYPH_HEIGHT;
        }

        var texture = new Texture2D(
            device,
            surfaceWidth,
            surfaceHeight,
            false,
            SurfaceFormat.Color);
        texture.SetData(pixelBuffer);

        return texture;
    }

    private static List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();

        // Handle literal escape sequences (\n, \r) in addition to actual control characters
        text = text.Replace("\\n", "\n")
                   .Replace("\\r", "\r");

        // Strip color codes ({=a, {=b, etc.) — rendering colors TBD
        text = Regex.Replace(text, @"\{=[a-zA-Z]", string.Empty);

        // Collapse consecutive tabs into a single newline
        while (text.Contains("\t\t"))
            text = text.Replace("\t\t", "\t");

        var paragraphs = text.Split('\r', '\n', '\t');

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                lines.Add(string.Empty);

                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentLine = string.Empty;

            foreach (var word in words)
            {
                var testLine = currentLine.Length == 0 ? word : currentLine + " " + word;

                if (MeasureWidth(testLine) > maxWidth)
                {
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    } else
                    {
                        lines.Add(word);
                        currentLine = string.Empty;
                    }
                } else
                    currentLine = testLine;
            }

            if (currentLine.Length > 0)
                lines.Add(currentLine);
        }

        return lines;
    }
}