#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Chant text rendered as plain blue text above an entity's head. No bubble, no name prefix. Max 32 characters, 18
///     chars per visual line with character wrap (not word wrap). If total character count is 10 or less, text is centered
///     per line; otherwise left-aligned.
/// </summary>
public sealed class ChantOverlay : UIImage
{
    private const int CHARS_PER_LINE = 18;
    private const int LINE_HEIGHT = 12;
    private const int MAX_CHARS = 32;
    private const int CENTER_THRESHOLD = 10;
    private const float DISPLAY_DURATION_MS = 3000f;

    private static readonly Color ChantColor = new(100, 149, 237);

    private float ElapsedMs;

    public uint EntityId { get; }
    public bool IsExpired => ElapsedMs >= DISPLAY_DURATION_MS;

    private ChantOverlay(
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

    public static ChantOverlay Create(uint entityId, string message)
    {
        var text = message.Length > MAX_CHARS ? message[..MAX_CHARS] : message;
        var centered = text.Length <= CENTER_THRESHOLD;

        // Character-wrap into visual lines
        var visualLines = new List<string>();
        var remaining = text;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= CHARS_PER_LINE)
            {
                visualLines.Add(remaining);

                break;
            }

            visualLines.Add(remaining[..CHARS_PER_LINE]);
            remaining = remaining[CHARS_PER_LINE..];
        }

        if (visualLines.Count == 0)
            visualLines.Add(" ");

        // Compute texture dimensions
        var textAreaWidth = CHARS_PER_LINE * 6 + 2;
        var totalHeight = visualLines.Count * LINE_HEIGHT;
        var totalWidth = textAreaWidth;

        var pixels = new Color[totalWidth * totalHeight];
        var y = 0;

        foreach (var visualLine in visualLines)
        {
            using var lineTexture = TextRenderer.RenderText(visualLine, ChantColor);

            var srcPixels = new Color[lineTexture.Width * lineTexture.Height];
            lineTexture.GetData(srcPixels);

            var offsetX = centered ? (totalWidth - lineTexture.Width) / 2 : 0;

            if (offsetX < 0)
                offsetX = 0;

            for (var row = 0; (row < lineTexture.Height) && ((y + row) < totalHeight); row++)
            {
                for (var col = 0; (col < lineTexture.Width) && ((offsetX + col) < totalWidth); col++)
                {
                    var src = srcPixels[row * lineTexture.Width + col];

                    if (src.A > 0)
                        pixels[(y + row) * totalWidth + offsetX + col] = src;
                }
            }

            y += LINE_HEIGHT;
        }

        var texture = new Texture2D(ChaosGame.Device, totalWidth, totalHeight);
        texture.SetData(pixels);

        return new ChantOverlay(
            entityId,
            texture,
            totalWidth,
            totalHeight);
    }

    public override void Update(GameTime gameTime, InputBuffer input) => ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
}