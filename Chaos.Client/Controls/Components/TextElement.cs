#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Stores text draw state and draws via the font texture atlas. Re-measures only when content changes. No GPU
///     resources are held — the shared font atlas handles all rendering.
/// </summary>
public sealed class TextElement
{
    private bool IsShadowed;
    private Color RenderedShadowColor;
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public Color Color { get; private set; } = Color.White;
    public int Height { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public int Width { get; private set; }
    public IReadOnlyList<string>? WrappedLines { get; private set; }
    public bool HasContent => Width > 0;

    /// <summary>
    ///     Draws the text at the specified position.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Vector2 position)
    {
        if (!HasContent)
            return;

        if (IsShadowed)
            TextRenderer.DrawShadowedText(
                spriteBatch,
                position,
                Text,
                Color,
                RenderedShadowColor);
        else if (WrappedLines is not null)
            TextRenderer.DrawLines(
                spriteBatch,
                position,
                WrappedLines,
                Color);
        else
            TextRenderer.DrawText(
                spriteBatch,
                position,
                Text,
                Color);
    }

    /// <summary>
    ///     Draws the text within the specified bounds using the Alignment property.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (!HasContent)
            return;

        var x = Alignment switch
        {
            TextAlignment.Center => bounds.X + (bounds.Width - Width) / 2,
            TextAlignment.Right  => bounds.X + bounds.Width - Width,
            _                    => bounds.X
        };

        var y = bounds.Y + (bounds.Height - Height) / 2;

        Draw(spriteBatch, new Vector2(x, y));
    }

    /// <summary>
    ///     Updates text state if content or color changed.
    /// </summary>
    public void Update(string text, Color color)
    {
        if ((text == Text) && (color == Color))
            return;

        Text = text;
        Color = color;
        IsShadowed = false;
        WrappedLines = null;

        if (string.IsNullOrEmpty(text))
        {
            Width = 0;
            Height = 0;
        } else
        {
            Width = TextRenderer.MeasureWidth(text);
            Height = TextRenderer.CHAR_HEIGHT;
        }
    }

    /// <summary>
    ///     Updates text state for shadowed rendering. The bounding box includes shadow margins (+2 width, +1 height).
    /// </summary>
    public void UpdateShadowed(string text, Color color, Color shadowColor)
    {
        if ((text == Text) && (color == Color))
            return;

        Text = text;
        Color = color;
        RenderedShadowColor = shadowColor;
        IsShadowed = true;
        WrappedLines = null;

        if (string.IsNullOrEmpty(text))
        {
            Width = 0;
            Height = 0;
        } else
        {
            Width = TextRenderer.MeasureWidth(text) + 2;
            Height = TextRenderer.CHAR_HEIGHT + 1;
        }
    }

    /// <summary>
    ///     Updates text state for word-wrapped rendering. Pre-computes wrapped lines.
    /// </summary>
    public void UpdateWrapped(string text, int maxWidth, Color color)
    {
        if ((text == Text) && (color == Color))
            return;

        Text = text;
        Color = color;
        IsShadowed = false;

        if (string.IsNullOrEmpty(text))
        {
            WrappedLines = null;
            Width = 0;
            Height = 0;
        } else
        {
            WrappedLines = TextRenderer.WrapText(text, maxWidth);
            Width = maxWidth;
            Height = Math.Max(TextRenderer.CHAR_HEIGHT, WrappedLines.Count * TextRenderer.CHAR_HEIGHT);
        }
    }
}