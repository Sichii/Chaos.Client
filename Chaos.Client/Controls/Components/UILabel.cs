#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Read-only text label that participates in the UI element tree. Wraps CachedText for efficient re-rendering only
///     when content changes. When WordWrap is true, text is wrapped to the label width; use ScrollOffset to scroll when
///     ContentHeight exceeds the label bounds.
/// </summary>

// ReSharper disable once ClassCanBeSealed.Global
public class UILabel : UIElement
{
    private readonly TextElement TextElement = new();
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public Color ForegroundColor
    {
        get => TextElement.Color;
        set => Invalidate(TextElement.Text, value);
    }

    public int PaddingLeft { get; set; } = 1;
    public int PaddingTop { get; set; } = 1;

    /// <summary>
    ///     Vertical scroll offset in pixels for wrapped text content.
    /// </summary>
    public int ScrollOffset { get; set; }

    public string Text
    {
        get => TextElement.Text;
        set => Invalidate(value, TextElement.Color);
    }

    public bool WordWrap { get; set; }

    /// <summary>
    ///     Total pixel height of the rendered content. For wrapped text, this may exceed the label bounds.
    /// </summary>
    public int ContentHeight => TextElement.Height;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || !TextElement.HasContent)
            return;

        base.Draw(spriteBatch);

        var innerX = ScreenX + PaddingLeft;
        var innerY = ScreenY + PaddingTop;
        var innerW = Width - PaddingLeft * 2;
        var innerH = Height - PaddingTop * 2;

        if (TextElement.WrappedLines is not null)
        {
            var firstLine = ScrollOffset / TextRenderer.CHAR_HEIGHT;
            var maxLines = (innerH + TextRenderer.CHAR_HEIGHT - 1) / TextRenderer.CHAR_HEIGHT;

            TextRenderer.DrawLines(
                spriteBatch,
                new Vector2(innerX, innerY),
                TextElement.WrappedLines,
                firstLine,
                maxLines,
                TextElement.Color);
        } else
        {
            TextElement.Alignment = Alignment;

            TextElement.Draw(
                spriteBatch,
                new Rectangle(
                    innerX,
                    innerY,
                    innerW,
                    innerH));
        }
    }

    private void Invalidate(string text, Color color)
    {
        if (WordWrap)
            TextElement.UpdateWrapped(text, Width - PaddingLeft * 2, color);
        else
            TextElement.Update(text, color);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}