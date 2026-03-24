#region
using System.Buffers;
using System.Text;
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
    public const int CHAR_WIDTH = 6;
    public const int CHAR_HEIGHT = 12;

    private const int ENGLISH_GLYPH_WIDTH = 8;
    private const int KOREAN_GLYPH_WIDTH = 16;
    private const int GLYPH_HEIGHT = CHAR_HEIGHT;
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
        var activeColor = color;

        for (var i = 0; i < text.Length; i++)
        {
            // Check for color code sequence: {=x (3 chars, no closing brace)
            if (IsColorCode(text, i))
            {
                activeColor = GetColorCode(text[i + 2])!.Value;
                i += 2; // skip past =x, loop increment handles {

                continue;
            }

            var c = text[i];

            if (c is >= (char)33 and <= (char)126)
            {
                Graphics.DrawGlyph(
                    EnglishFont!,
                    pixelBuffer,
                    bufferWidth,
                    c - 33,
                    cursorX,
                    startY,
                    activeColor);
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
                        activeColor);

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

        EnglishFont = DataContext.Fonts.EnglishFont;
        KoreanFont = DataContext.Fonts.KoreanFont;
        KoreanEncoding = Encoding.GetEncoding(949);
    }

    /// <summary>
    ///     Finds the character index at which to break a line to fit within maxWidth pixels. Prefers breaking at the last
    ///     space; falls back to force-breaking mid-word. Skips {=x} color codes for width measurement.
    /// </summary>
    public static int FindLineBreak(string text, int maxWidth)
    {
        var width = 0;
        var lastSpace = -1;

        for (var i = 0; i < text.Length; i++)
        {
            // Skip color codes — they have zero visual width
            if (IsColorCode(text, i))
            {
                i += 2; // skip past =x, loop increment handles {

                continue;
            }

            if (text[i] == ' ')
                lastSpace = i;

            width += MeasureCharWidth(text[i]);

            if (width > maxWidth)
                return lastSpace > 0 ? lastSpace + 1 : Math.Max(1, i);
        }

        return text.Length;
    }

    /// <summary>
    ///     Maps a color code character (the letter after {=) to its SKColor.
    /// </summary>
    private static SKColor? GetColorCode(char code)
        => code switch
        {
            'a' => new SKColor(128, 128, 128),
            'b' => new SKColor(255, 0, 0),
            'c' => new SKColor(255, 255, 0),
            'd' => new SKColor(0, 128, 0),
            'e' => new SKColor(192, 192, 192),
            'f' => new SKColor(0, 0, 255),
            'g' => new SKColor(220, 220, 220),
            'h' => new SKColor(128, 128, 128),
            'i' => new SKColor(152, 152, 152),
            'j' => new SKColor(128, 128, 128),
            'k' => new SKColor(112, 128, 144),
            'l' => new SKColor(54, 69, 79),
            'm' => new SKColor(28, 28, 28),
            'n' => new SKColor(0, 0, 0),
            'o' => new SKColor(255, 105, 180),
            'p' => new SKColor(128, 0, 128),
            'q' => new SKColor(57, 255, 20),
            's' => new SKColor(255, 165, 0),
            't' => new SKColor(139, 69, 19),
            'u' => new SKColor(255, 255, 255),
            'x' => SKColor.Empty,
            _   => null
        };

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

    /// <summary>
    ///     Returns true if the text at position i starts a {=x color code sequence.
    /// </summary>
    private static bool IsColorCode(string text, int i)
        => ((i + 2) < text.Length) && (text[i] == '{') && (text[i + 1] == '=') && GetColorCode(text[i + 2]) is not null;

    private static bool IsKorean(char c) => c > 127;

    /// <summary>
    ///     Returns the advance width of a single character.
    /// </summary>
    public static int MeasureCharWidth(char c) => IsKorean(c) ? KOREAN_ADVANCE : ENGLISH_ADVANCE;

    /// <summary>
    ///     Measures the pixel width of a text string. Skips {=x} color codes.
    /// </summary>
    public static int MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        EnsureFontsLoaded();

        var width = 0;

        for (var i = 0; i < text.Length; i++)
        {
            // Skip color codes — they have zero visual width
            if (IsColorCode(text, i))
            {
                i += 2;

                continue;
            }

            width += IsKorean(text[i]) ? KOREAN_ADVANCE : ENGLISH_ADVANCE;
        }

        return width;
    }

    /// <summary>
    ///     Renders a single line of text with a dual diagonal drop shadow. The shadow is drawn at (-1,+1) and (+1,+1) relative
    ///     to the main text, producing visible shadow on the left, right, and bottom edges. Matches the original Dark Ages
    ///     client name tag rendering.
    /// </summary>
    public static Texture2D RenderShadowedText(string text, Color textColor, Color shadowColor)
    {
        if (string.IsNullOrEmpty(text))
            text = " ";

        EnsureFontsLoaded();

        var skTextColor = new SKColor(
            textColor.R,
            textColor.G,
            textColor.B,
            textColor.A);

        var skShadowColor = new SKColor(
            shadowColor.R,
            shadowColor.G,
            shadowColor.B,
            shadowColor.A);

        var textWidth = Math.Max(1, MeasureWidth(text));

        // +2 width for 1px shadow margin on each side, +1 height for shadow below
        var surfaceWidth = textWidth + 2;
        var surfaceHeight = GLYPH_HEIGHT + 1;

        var byteCount = surfaceWidth * surfaceHeight * 4;
        var pixelBuffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            Array.Clear(pixelBuffer, 0, byteCount);

            // Shadow draws: down-right (+1,+1) and down-left (-1,+1) relative to main text at (1,0)
            DrawTextLine(
                pixelBuffer,
                surfaceWidth,
                text,
                2,
                1,
                skShadowColor);

            DrawTextLine(
                pixelBuffer,
                surfaceWidth,
                text,
                0,
                1,
                skShadowColor);

            // Main text: centered at (1,0)
            DrawTextLine(
                pixelBuffer,
                surfaceWidth,
                text,
                1,
                0,
                skTextColor);

            var texture = new Texture2D(
                TextureConverter.Device,
                surfaceWidth,
                surfaceHeight,
                false,
                SurfaceFormat.Color);
            texture.SetData(pixelBuffer, 0, byteCount);

            return texture;
        } finally
        {
            ArrayPool<byte>.Shared.Return(pixelBuffer);
        }
    }

    /// <summary>
    ///     Renders a single line of text to a Texture2D using the game's bitmap font.
    /// </summary>
    public static Texture2D RenderText(string text, Color? color = null)
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

        var byteCount = width * GLYPH_HEIGHT * 4;
        var pixelBuffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            Array.Clear(pixelBuffer, 0, byteCount);

            DrawTextLine(
                pixelBuffer,
                width,
                text,
                0,
                0,
                skColor);

            var texture = new Texture2D(
                TextureConverter.Device,
                width,
                GLYPH_HEIGHT,
                false,
                SurfaceFormat.Color);
            texture.SetData(pixelBuffer, 0, byteCount);

            return texture;
        } finally
        {
            ArrayPool<byte>.Shared.Return(pixelBuffer);
        }
    }

    /// <summary>
    ///     Renders word-wrapped text into a bounded area and returns a Texture2D.
    /// </summary>
    public static Texture2D RenderWrappedText(
        string text,
        int maxWidth,
        int maxHeight,
        Color? color = null)
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

        var byteCount = surfaceWidth * surfaceHeight * 4;
        var pixelBuffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            Array.Clear(pixelBuffer, 0, byteCount);

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
                TextureConverter.Device,
                surfaceWidth,
                surfaceHeight,
                false,
                SurfaceFormat.Color);
            texture.SetData(pixelBuffer, 0, byteCount);

            return texture;
        } finally
        {
            ArrayPool<byte>.Shared.Return(pixelBuffer);
        }
    }

    /// <summary>
    ///     Word-wraps text into lines that fit within maxWidth pixels. Splits on explicit newlines, then wraps each paragraph
    ///     by character width.
    /// </summary>
    public static List<string> WrapLines(string text, int maxWidth)
    {
        var lines = new List<string>();

        if ((maxWidth <= 0) || string.IsNullOrEmpty(text))
            return lines;

        foreach (var paragraph in text.Split('\n'))
        {
            var remaining = paragraph;

            while (remaining.Length > 0)
            {
                var lineEnd = FindLineBreak(remaining, maxWidth);

                lines.Add(
                    remaining[..lineEnd]
                        .TrimEnd());

                remaining = remaining[lineEnd..]
                    .TrimStart();
            }

            if (paragraph.Length == 0)
                lines.Add(string.Empty);
        }

        return lines;
    }

    private static List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();

        // Handle literal escape sequences (\n, \r) in addition to actual control characters
        text = text.Replace("\\n", "\n")
                   .Replace("\\r", "\r");

        // Color codes ({=a, {=b, etc.) are preserved — DrawTextLine renders them

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