#region
using System.Buffers;
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Chat bubble rendered as a fixed-width rounded capsule with an optional tail. Width is based on a fixed monospaced
///     character cell width, not measured glyph width.
/// </summary>
public sealed class ChatBubble : UIImage
{
    private const int MAX_LINE_CHARS = 18;
    private const int CHAR_CELL_WIDTH = TextRenderer.CHAR_WIDTH;
    private const int LINE_HEIGHT = TextRenderer.CHAR_HEIGHT;

    private const int LEFT_CAP_WIDTH = 7;
    private const int RIGHT_CAP_WIDTH = 7;
    private const int INNER_PADDING_LEFT = 2;
    private const int INNER_PADDING_RIGHT = 4;
    private const int INNER_PADDING_TOP = 3;
    private const int INNER_PADDING_BOTTOM = 3;
    private const int BUBBLE_HEIGHT = 17;

    private const int TAIL_HEIGHT = 4;

    private const float DISPLAY_DURATION_MS = 3000f;

    private static readonly Color BubbleBorderColor = Color.White;

    private static readonly Color BubbleFillColor = new(
        0,
        0,
        0,
        85);

    private static readonly Color NormalTextColor = Color.White;
    private static readonly Color ShoutTextColor = Color.Yellow;

    private float ElapsedMs;
    public bool TailOnTop { get; set; }

    public uint EntityId { get; }

    public bool IsExpired => ElapsedMs >= DISPLAY_DURATION_MS;

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

    public static ChatBubble Create(
        GraphicsDevice device,
        uint entityId,
        string message,
        bool isShout)
    {
        var lines = WordWrap(message);
        var textColor = isShout ? ShoutTextColor : NormalTextColor;

        var textAreaWidth = MAX_LINE_CHARS * CHAR_CELL_WIDTH + 2;

        var bubbleWidth = LEFT_CAP_WIDTH + INNER_PADDING_LEFT + textAreaWidth + INNER_PADDING_RIGHT + RIGHT_CAP_WIDTH;

        var bubbleHeight = Math.Max(BUBBLE_HEIGHT, INNER_PADDING_TOP + lines.Count * LINE_HEIGHT + INNER_PADDING_BOTTOM);
        var totalHeight = bubbleHeight + TAIL_HEIGHT;
        var totalWidth = bubbleWidth;

        var pixelCount = totalWidth * totalHeight;
        var pixels = ArrayPool<Color>.Shared.Rent(pixelCount);

        try
        {
            Array.Clear(pixels, 0, pixelCount);

            DrawBubbleBody(
                pixels,
                totalWidth,
                0,
                0,
                bubbleWidth,
                bubbleHeight,
                BubbleBorderColor,
                BubbleFillColor);

            DrawTailBottomCenter(
                pixels,
                totalWidth,
                bubbleWidth / 2,
                bubbleHeight - 1,
                BubbleBorderColor,
                BubbleFillColor);

            var textSurfaceHeight = Math.Max(1, lines.Count * LINE_HEIGHT);

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
                LEFT_CAP_WIDTH + INNER_PADDING_LEFT,
                INNER_PADDING_TOP);

            var texture = new Texture2D(device, totalWidth, totalHeight);
            texture.SetData(pixels, 0, pixelCount);

            return new ChatBubble(
                entityId,
                texture,
                totalWidth,
                totalHeight);
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
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
        var srcCount = src.Width * src.Height;
        var srcPixels = ArrayPool<Color>.Shared.Rent(srcCount);

        try
        {
            src.GetData(srcPixels, 0, srcCount);

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
        } finally
        {
            ArrayPool<Color>.Shared.Return(srcPixels);
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
        var resultCount = surfaceWidth * surfaceHeight;
        var resultPixels = ArrayPool<Color>.Shared.Rent(resultCount);

        try
        {
            Array.Clear(resultPixels, 0, resultCount);

            var y = 0;
            var srcPixels = Array.Empty<Color>();
            var srcRented = false;

            try
            {
                foreach (var line in lines)
                {
                    using var lineTexture = TextRenderer.RenderText(device, line, color);

                    var srcCount = lineTexture.Width * lineTexture.Height;

                    if (srcPixels.Length < srcCount)
                    {
                        if (srcRented)
                            ArrayPool<Color>.Shared.Return(srcPixels);

                        srcPixels = ArrayPool<Color>.Shared.Rent(srcCount);
                        srcRented = true;
                    }

                    lineTexture.GetData(srcPixels, 0, srcCount);

                    for (var row = 0; (row < lineTexture.Height) && ((y + row) < surfaceHeight); row++)
                    {
                        for (var col = 0; (col < lineTexture.Width) && (col < surfaceWidth); col++)
                        {
                            var src = srcPixels[row * lineTexture.Width + col];

                            if (src.A > 0)
                                resultPixels[(y + row) * surfaceWidth + col] = src;
                        }
                    }

                    y += LINE_HEIGHT;
                }
            } finally
            {
                if (srcRented)
                    ArrayPool<Color>.Shared.Return(srcPixels);
            }

            result.SetData(resultPixels, 0, resultCount);

            return result;
        } finally
        {
            ArrayPool<Color>.Shared.Return(resultPixels);
        }
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

    public override void Update(GameTime gameTime, InputBuffer input) => ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

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
            if (remaining.Length <= MAX_LINE_CHARS)
            {
                lines.Add(remaining);

                break;
            }

            var breakAt = MAX_LINE_CHARS;

            for (var i = MAX_LINE_CHARS - 1; i > 0; i--)
                if (remaining[i] == ' ')
                {
                    breakAt = i;

                    break;
                }

            var line = remaining[..breakAt]
                .TrimEnd();

            if (line.Length == 0)
                line = remaining[..MAX_LINE_CHARS];

            lines.Add(line);

            remaining = remaining[line.Length..]
                .TrimStart();
        }

        return lines;
    }
}