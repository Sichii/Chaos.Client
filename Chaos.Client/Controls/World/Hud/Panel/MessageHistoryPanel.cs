#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Message history panel (Shift+F). Displays server message history (same text as the orange bar) in its own tab-sized
///     panel. Reads from the shared history list.
/// </summary>
public sealed class MessageHistoryPanel : UIPanel
{
    private const int GLYPH_HEIGHT = 12;
    private readonly Rectangle DisplayBounds;
    private readonly IReadOnlyList<string> History;
    private readonly CachedText[] LineTextures;
    private readonly int MaxVisibleLines;
    private int LastHistoryCount;
    private int RenderedHistoryCount = -1;
    private int RenderedScrollOffset = -1;
    private int ScrollOffset;

    public MessageHistoryPanel(GraphicsDevice device, Rectangle displayBounds, IReadOnlyList<string> history)
    {
        Name = "MessageHistory";
        DisplayBounds = displayBounds;
        History = history;

        Background = TextureConverter.LoadSpfTexture(device, "_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        LineTextures = new CachedText[MaxVisibleLines];

        for (var i = 0; i < MaxVisibleLines; i++)
            LineTextures[i] = new CachedText(device);
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

        if ((MaxVisibleLines == 0) || (History.Count == 0))
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

    private void RefreshDisplay()
    {
        if ((History.Count == RenderedHistoryCount) && (ScrollOffset == RenderedScrollOffset))
            return;

        RenderedHistoryCount = History.Count;
        RenderedScrollOffset = ScrollOffset;

        var startIndex = Math.Max(0, History.Count - MaxVisibleLines - ScrollOffset);
        var lineIndex = 0;

        for (var i = startIndex; (i < History.Count) && (lineIndex < MaxVisibleLines); i++)
        {
            LineTextures[lineIndex]
                .Update(History[i], Color.Orange);
            lineIndex++;
        }

        for (; lineIndex < MaxVisibleLines; lineIndex++)
            LineTextures[lineIndex]
                .Update(string.Empty, Color.White);
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        // Auto-scroll to bottom when new messages arrive
        if (History.Count != LastHistoryCount)
        {
            ScrollOffset = 0;
            LastHistoryCount = History.Count;
        }

        if ((input.ScrollDelta != 0) && (History.Count > MaxVisibleLines))
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, History.Count - MaxVisibleLines);
    }
}