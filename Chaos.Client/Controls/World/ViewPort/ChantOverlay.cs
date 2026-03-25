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
public sealed class ChantOverlay : UIElement
{
    private const int CHARS_PER_LINE = 18;
    private const int LINE_HEIGHT = 12;
    private const int MAX_CHARS = 32;
    private const int CENTER_THRESHOLD = 10;
    private const float DISPLAY_DURATION_MS = 3000f;

    private static readonly Color ChantColor = new(100, 149, 237);

    private readonly bool Centered;
    private readonly List<string> VisualLines;

    private float ElapsedMs;

    public uint EntityId { get; }
    public bool IsExpired => ElapsedMs >= DISPLAY_DURATION_MS;

    private ChantOverlay(
        uint entityId,
        List<string> visualLines,
        bool centered,
        int width,
        int height)
    {
        EntityId = entityId;
        VisualLines = visualLines;
        Centered = centered;
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

        var textAreaWidth = CHARS_PER_LINE * 6 + 2;
        var totalHeight = visualLines.Count * LINE_HEIGHT;

        return new ChantOverlay(
            entityId,
            visualLines,
            centered,
            textAreaWidth,
            totalHeight);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var y = (float)ScreenY;

        foreach (var line in VisualLines)
        {
            var lineWidth = TextRenderer.MeasureWidth(line);
            var x = Centered ? ScreenX + (Width - lineWidth) / 2f : ScreenX;

            TextRenderer.DrawText(
                spriteBatch,
                new Vector2(x, y),
                line,
                ChantColor);
            y += LINE_HEIGHT;
        }
    }

    public override void Update(GameTime gameTime, InputBuffer input) => ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
}