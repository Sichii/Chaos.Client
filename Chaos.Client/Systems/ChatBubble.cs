#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Chat bubble rendered as a fixed-width rounded capsule with an optional tail. Width is based on a fixed monospaced
///     character cell width, not measured glyph width.
/// </summary>
public sealed class ChatBubble : IDisposable
{
    private const int MaxLineChars = 18;

    // You said the font is monospaced and predetermined.
    // Set this to the exact width of one character cell in your font.
    private const int CharCellWidth = 6;

    private const int LineHeight = 12;

    // Bubble geometry
    private const int LeftCapWidth = 7;
    private const int RightCapWidth = 7;
    private const int InnerPaddingLeft = 2;
    private const int InnerPaddingRight = 4;
    private const int InnerPaddingTop = 3;
    private const int InnerPaddingBottom = 3;
    private const int BubbleHeight = 17;

    private const int TailHeight = 4;

    private const float DisplayDurationMs = 3000f;

    private static readonly Color BorderColor = Color.White;

    private static readonly Color BackgroundColor = new(
        0,
        0,
        0,
        85);

    private static readonly Color NormalTextColor = Color.White;
    private static readonly Color ShoutTextColor = Color.Yellow;

    private float elapsedMs;
    public bool TailOnTop { get; set; }

    public uint EntityId { get; }
    public int Height { get; }
    public Texture2D Texture { get; }
    public int Width { get; }

    public bool IsExpired => elapsedMs >= DisplayDurationMs;

    private ChatBubble(
        uint entityId,
        Texture2D texture,
        int width,
        int height)
    {
        EntityId = entityId;
        Texture = texture;
        Width = width;
        Height = height;
    }

    public void Dispose() => Texture.Dispose();

    public static ChatBubble Create(
        GraphicsDevice device,
        uint entityId,
        string message,
        bool isShout)
    {
        var lines = WordWrap(message);
        var textColor = isShout ? ShoutTextColor : NormalTextColor;

        // Exact content width: 18 monospaced cells.
        var textAreaWidth = MaxLineChars * CharCellWidth + 2;

        // Exact bubble width.
        var bubbleWidth = LeftCapWidth + InnerPaddingLeft + textAreaWidth + InnerPaddingRight + RightCapWidth;

        var bubbleHeight = Math.Max(BubbleHeight, InnerPaddingTop + lines.Count * LineHeight + InnerPaddingBottom);
        var totalHeight = bubbleHeight + TailHeight;
        var totalWidth = bubbleWidth;

        var pixels = new Color[totalWidth * totalHeight];

        DrawBubbleBody(
            pixels,
            totalWidth,
            0,
            0,
            bubbleWidth,
            bubbleHeight,
            BorderColor,
            BackgroundColor);

        DrawTailBottomCenter(
            pixels,
            totalWidth,
            bubbleWidth / 2,
            bubbleHeight - 1,
            BorderColor,
            BackgroundColor);

        var textSurfaceHeight = Math.Max(1, lines.Count * LineHeight);

        using var textTexture = RenderBubbleText(
            device,
            lines,
            textAreaWidth,
            textSurfaceHeight,
            textColor);

        OverlayTexture(
            pixels,
            totalWidth,
            totalHeight,
            textTexture,
            LeftCapWidth + InnerPaddingLeft,
            InnerPaddingTop);

        var texture = new Texture2D(device, totalWidth, totalHeight);
        texture.SetData(pixels);

        return new ChatBubble(
            entityId,
            texture,
            totalWidth,
            totalHeight);
    }

    /// <summary>
    ///     Draws a horizontally-stretchable rounded capsule similar to the screenshot. This does not rely on measured text
    ///     width.
    /// </summary>
    private static void DrawBubbleBody(
        Color[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height,
        Color border,
        Color fill)
    {
        // Corner insets — top and bottom caps (mirrored for bottom)
        ReadOnlySpan<int> topInset =
        [
            5,
            3,
            2,
            1,
            1
        ];

        ReadOnlySpan<int> bottomInset =
        [
            1,
            1,
            2,
            3,
            5
        ];

        for (var row = 0; row < height; row++)
        {
            int li,
                ri;

            if (row < topInset.Length)
            {
                li = topInset[row];
                ri = topInset[row];
            } else if (row >= (height - bottomInset.Length))
            {
                var bottomRow = row - (height - bottomInset.Length);
                li = bottomInset[bottomRow];
                ri = bottomInset[bottomRow];
            } else
            {
                li = 0;
                ri = 0;
            }

            var startX = x + li;
            var endX = x + width - 1 - ri;

            if (startX > endX)
                continue;

            // Fill
            for (var col = startX + 1; col <= (endX - 1); col++)
                SetPixel(
                    pixels,
                    stride,
                    col,
                    y + row,
                    fill);

            // Border
            SetPixel(
                pixels,
                stride,
                startX,
                y + row,
                border);

            SetPixel(
                pixels,
                stride,
                endX,
                y + row,
                border);

            // Top and bottom edge spans
            if ((row == 0) || (row == (height - 1)))
                for (var col = startX; col <= endX; col++)
                    SetPixel(
                        pixels,
                        stride,
                        col,
                        y + row,
                        border);

            // Smooth horizontal transitions when inset changes from previous row
            if (row > 0)
            {
                var prevLi = GetBubbleInset(
                    row - 1,
                    height,
                    topInset,
                    bottomInset);
                var prevRi = prevLi;

                if (li < prevLi)
                    for (var col = x + li; col < (x + prevLi); col++)
                        SetPixel(
                            pixels,
                            stride,
                            col,
                            y + row,
                            border);

                if (ri < prevRi)
                    for (var col = x + width - prevRi; col <= (x + width - 1 - ri); col++)
                        SetPixel(
                            pixels,
                            stride,
                            col,
                            y + row,
                            border);
            }

            if (row < (height - 1))
            {
                var nextLi = GetBubbleInset(
                    row + 1,
                    height,
                    topInset,
                    bottomInset);
                var nextRi = nextLi;

                if (li < nextLi)
                    for (var col = x + li; col < (x + nextLi); col++)
                        SetPixel(
                            pixels,
                            stride,
                            col,
                            y + row,
                            border);

                if (ri < nextRi)
                    for (var col = x + width - nextRi; col <= (x + width - 1 - ri); col++)
                        SetPixel(
                            pixels,
                            stride,
                            col,
                            y + row,
                            border);
            }
        }
    }

    private static void DrawTailBottomCenter(
        Color[] pixels,
        int stride,
        int centerX,
        int baseY,
        Color border,
        Color fill)
    {
        var startPx = centerX - 4;

        for (var i = 0; i < 7; i++)
            SetPixel(
                pixels,
                stride,
                startPx + i,
                baseY,
                fill);

        for (var i = 0; i < 7; i++)
            SetPixel(
                pixels,
                stride,
                startPx + i,
                baseY + 1,
                i is < 2 or > 4 ? border : fill);

        for (var i = 2; i < 5; i++)
            SetPixel(
                pixels,
                stride,
                startPx + i,
                baseY + 2,
                i == 3 ? fill : border);

        SetPixel(
            pixels,
            stride,
            startPx + 3,
            baseY + 3,
            border);

        SetPixel(
            pixels,
            stride,
            startPx + 3,
            baseY + 4,
            border);
    }

    private static int GetBubbleInset(
        int row,
        int height,
        ReadOnlySpan<int> topInset,
        ReadOnlySpan<int> bottomInset)
    {
        if (row < topInset.Length)
            return topInset[row];

        if (row >= (height - bottomInset.Length))
            return bottomInset[row - (height - bottomInset.Length)];

        return 0;
    }

    private static void OverlayTexture(
        Color[] dest,
        int destWidth,
        int destHeight,
        Texture2D src,
        int offsetX,
        int offsetY)
    {
        var srcPixels = new Color[src.Width * src.Height];
        src.GetData(srcPixels);

        for (var row = 0; row < src.Height; row++)
        {
            for (var col = 0; col < src.Width; col++)
            {
                var srcPixel = srcPixels[row * src.Width + col];

                if (srcPixel.A == 0)
                    continue;

                var dx = offsetX + col;
                var dy = offsetY + row;

                if ((dx >= 0) && (dx < destWidth) && (dy >= 0) && (dy < destHeight))
                    dest[dy * destWidth + dx] = srcPixel;
            }
        }
    }

    private static Texture2D RenderBubbleText(
        GraphicsDevice device,
        List<string> lines,
        int width,
        int height,
        Color color)
    {
        var surfaceWidth = Math.Max(1, width);
        var surfaceHeight = Math.Max(1, height);

        var result = new Texture2D(device, surfaceWidth, surfaceHeight);
        var resultPixels = new Color[surfaceWidth * surfaceHeight];

        var y = 0;

        foreach (var line in lines)
        {
            using var lineTexture = TextRenderer.RenderText(device, line, color);

            var srcPixels = new Color[lineTexture.Width * lineTexture.Height];
            lineTexture.GetData(srcPixels);

            for (var row = 0; (row < lineTexture.Height) && ((y + row) < surfaceHeight); row++)
            {
                for (var col = 0; (col < lineTexture.Width) && (col < surfaceWidth); col++)
                {
                    var src = srcPixels[row * lineTexture.Width + col];

                    if (src.A > 0)
                        resultPixels[(y + row) * surfaceWidth + col] = src;
                }
            }

            y += LineHeight;
        }

        result.SetData(resultPixels);

        return result;
    }

    private static void SetPixel(
        Color[] pixels,
        int stride,
        int x,
        int y,
        Color color)
    {
        if ((x < 0) || (y < 0))
            return;

        var h = pixels.Length / stride;

        if ((x >= stride) || (y >= h))
            return;

        pixels[y * stride + x] = color;
    }

    public void Update(float deltaMs) => elapsedMs += deltaMs;

    private static List<string> WordWrap(string text)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            lines.Add(" ");

            return lines;
        }

        var remaining = text.Trim();

        while (remaining.Length > 0)
        {
            if (remaining.Length <= MaxLineChars)
            {
                lines.Add(remaining);

                break;
            }

            var breakAt = MaxLineChars;

            for (var i = MaxLineChars - 1; i > 0; i--)
                if (remaining[i] == ' ')
                {
                    breakAt = i;

                    break;
                }

            var line = remaining[..breakAt]
                .TrimEnd();

            if (line.Length == 0)
                line = remaining[..MaxLineChars];

            lines.Add(line);

            remaining = remaining[line.Length..]
                .TrimStart();
        }

        return lines;
    }
}