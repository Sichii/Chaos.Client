#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Read-only text label that participates in the UI element tree. Wraps CachedText for efficient re-rendering only
///     when content changes. Supports scrollable wrapped text via SetWrappedText + ScrollOffset.
/// </summary>

// ReSharper disable once ClassCanBeSealed.Global
public class UILabel : UIElement
{
    private readonly CachedText Cache = new();
    private bool IsWrapped;
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public int PaddingLeft { get; set; } = 1;
    public int PaddingTop { get; set; } = 1;

    /// <summary>
    ///     Vertical scroll offset in pixels for wrapped text content.
    /// </summary>
    public int ScrollOffset { get; set; }

    public string Text { get; private set; } = string.Empty;
    public Color TextColor { get; private set; } = Color.White;

    /// <summary>
    ///     Total pixel height of the rendered content. For wrapped text, this may exceed the label bounds.
    /// </summary>
    public int ContentHeight => Cache.Texture?.Height ?? 0;

    public override void Dispose()
    {
        Cache.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Cache.Texture is null)
            return;

        base.Draw(spriteBatch);

        var innerX = ScreenX + PaddingLeft;
        var innerY = ScreenY + PaddingTop;
        var innerW = Width - PaddingLeft * 2;
        var innerH = Height - PaddingTop * 2;

        if (IsWrapped)
        {
            var srcH = Math.Min(innerH, Cache.Texture.Height - ScrollOffset);

            var sourceRect = new Rectangle(
                0,
                ScrollOffset,
                Cache.Texture.Width,
                srcH);

            spriteBatch.Draw(
                Cache.Texture,
                new Vector2(innerX, innerY),
                sourceRect,
                Color.White);
        } else
        {
            Cache.Alignment = Alignment;

            Cache.Draw(
                spriteBatch,
                new Rectangle(
                    innerX,
                    innerY,
                    innerW,
                    innerH));
        }
    }

    public void SetText(string text, Color? color = null)
    {
        IsWrapped = false;
        Text = text;

        if (color.HasValue)
            TextColor = color.Value;

        Cache.Update(text, TextColor);
    }

    /// <summary>
    ///     Sets word-wrapped text content. The text is wrapped to the label width and rendered at full height. Use
    ///     ScrollOffset to scroll when ContentHeight exceeds the label bounds.
    /// </summary>
    public void SetWrappedText(string text, Color? color = null)
    {
        IsWrapped = true;
        Text = text;

        if (color.HasValue)
            TextColor = color.Value;

        Cache.UpdateWrapped(text, Width - PaddingLeft * 2, TextColor);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}