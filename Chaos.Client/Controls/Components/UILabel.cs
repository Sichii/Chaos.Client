#region
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

    public bool ColorCodesEnabled
    {
        get => TextElement.ColorCodesEnabled;
        set => TextElement.ColorCodesEnabled = value;
    }

    public Color ForegroundColor
    {
        get => TextElement.Color;
        set => Invalidate(TextElement.Text, value);
    }

    /// <summary>
    ///     Vertical scroll offset in pixels for wrapped text content.
    /// </summary>
    public int ScrollOffset { get; set; }

    public bool Shadowed { get; set; }

    public string Text
    {
        get => TextElement.Text;
        set => Invalidate(value, TextElement.Color);
    }

    public bool TopAligned { get; set; }
    public bool WordWrap { get; set; }

    /// <summary>
    ///     Total pixel height of the rendered content. For wrapped text, this may exceed the label bounds.
    /// </summary>
    public int ContentHeight => TextElement.Height;

    public UILabel()
    {
        PaddingLeft = 1;
        PaddingRight = 1;
        PaddingTop = 1;
        PaddingBottom = 1;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || !TextElement.HasContent)
            return;

        base.Draw(spriteBatch);

        var innerX = ScreenX + PaddingLeft;
        var innerY = ScreenY + PaddingTop;
        var innerW = Width - PaddingLeft - PaddingRight;
        var innerH = Height - PaddingTop - PaddingBottom;

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
                TextElement.Color,
                ColorCodesEnabled);
        } else
        {
            TextElement.Alignment = Alignment;

            TextElement.Draw(
                spriteBatch,
                new Rectangle(
                    innerX,
                    innerY,
                    innerW,
                    TopAligned ? TextElement.Height : innerH));
        }
    }

    private void Invalidate(string text, Color color)
    {
        if (WordWrap)
            TextElement.UpdateWrapped(text, Width - PaddingLeft - PaddingRight, color);
        else if (Shadowed)
            TextElement.UpdateShadowed(text, color, Color.Black);
        else
            TextElement.Update(text, color);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}