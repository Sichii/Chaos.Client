#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Chat display panel (F key). Shows chat message history with word-wrap. Background loaded from _nchatbk.spf (shown
///     in tab area). Text rendered at ChatDisplayBounds (separate area of the HUD).
/// </summary>
public class ChatPanel : UIPanel
{
    private const int MAX_CHAT_LINES = 200;
    private const int GLYPH_HEIGHT = 12;
    private readonly List<ChatLine> ChatLog = [];

    private readonly Rectangle DisplayBounds;
    private readonly CachedText[] LineTextures;
    private readonly int MaxVisibleLines;
    private int LogVersion;
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public ChatPanel(GraphicsDevice device, Rectangle displayBounds)
    {
        Name = "Chat";
        DisplayBounds = displayBounds;

        Background = TextureConverter.LoadSpfTexture(device, "_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        LineTextures = new CachedText[MaxVisibleLines];

        for (var i = 0; i < MaxVisibleLines; i++)
            LineTextures[i] = new CachedText(device);
    }

    public void AddMessage(string text, Color color)
    {
        var maxWidth = DisplayBounds.Width;

        if (maxWidth <= 0)
            return;

        var remaining = text;

        while (remaining.Length > 0)
        {
            var lineEnd = FindLineBreak(remaining, maxWidth);

            var line = remaining[..lineEnd]
                .TrimEnd();

            remaining = remaining[lineEnd..]
                .TrimStart();

            ChatLog.Add(new ChatLine(line, color));
        }

        if (ChatLog.Count > MAX_CHAT_LINES)
            ChatLog.RemoveRange(0, ChatLog.Count - MAX_CHAT_LINES);

        // Auto-scroll to bottom on new message
        ScrollOffset = 0;
        LogVersion++;
    }

    public override void Dispose()
    {
        foreach (var texture in LineTextures)
            texture.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if ((MaxVisibleLines == 0) || (ChatLog.Count == 0))
            return;

        RefreshDisplay();

        var baseY = DisplayBounds.Y + DisplayBounds.Height;

        for (var i = MaxVisibleLines - 1; i >= 0; i--)
        {
            baseY -= GLYPH_HEIGHT;

            if (baseY < DisplayBounds.Y)
                break;

            LineTextures[i]
                .Draw(spriteBatch, new Vector2(DisplayBounds.X, baseY));
        }
    }

    private static int FindLineBreak(string text, int maxWidth)
    {
        var width = 0;
        var lastSpace = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
                lastSpace = i;

            width += TextRenderer.MeasureCharWidth(text[i]);

            if (width > maxWidth)
                return lastSpace > 0 ? lastSpace + 1 : Math.Max(1, i);
        }

        return text.Length;
    }

    private void RefreshDisplay()
    {
        if (RenderedVersion == LogVersion)
            return;

        RenderedVersion = LogVersion;

        var startIndex = Math.Max(0, ChatLog.Count - MaxVisibleLines - ScrollOffset);
        var lineIndex = 0;

        for (var i = startIndex; (i < ChatLog.Count) && (lineIndex < MaxVisibleLines); i++)
        {
            var line = ChatLog[i];

            LineTextures[lineIndex]
                .Update(line.Text, 0, line.Color);
            lineIndex++;
        }

        for (; lineIndex < MaxVisibleLines; lineIndex++)
            LineTextures[lineIndex]
                .Update(string.Empty, 0, Color.White);
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        if ((input.ScrollDelta != 0) && (ChatLog.Count > MaxVisibleLines))
        {
            // Scroll up = positive delta, scroll down = negative delta
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, ChatLog.Count - MaxVisibleLines);
            LogVersion++;
        }
    }

    private record struct ChatLine(string Text, Color Color);
}