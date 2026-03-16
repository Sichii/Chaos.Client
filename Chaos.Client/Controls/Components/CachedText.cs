#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public enum TextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
///     Caches a rendered text texture, re-rendering only when content changes. Avoids per-frame texture allocation and the
///     deferred SpriteBatch use-after-dispose issue.
/// </summary>
public sealed class CachedText : IDisposable
{
    private readonly GraphicsDevice Device;
    private Color RenderedColor;
    private string RenderedContent = string.Empty;
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public Texture2D? Texture { get; private set; }

    public CachedText(GraphicsDevice device) => Device = device;

    /// <inheritdoc />
    public void Dispose()
    {
        Texture?.Dispose();
        Texture = null;
    }

    /// <summary>
    ///     Draws the cached texture at the specified position (ignores Alignment).
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Vector2 position)
    {
        if (Texture is not null)
            spriteBatch.Draw(Texture, position, Color.White);
    }

    /// <summary>
    ///     Draws the cached texture within the specified bounds using the Alignment property.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (Texture is null)
            return;

        var x = Alignment switch
        {
            TextAlignment.Center => bounds.X + (bounds.Width - Texture.Width) / 2,
            TextAlignment.Right  => bounds.X + bounds.Width - Texture.Width,
            _                    => bounds.X
        };

        var y = bounds.Y + (bounds.Height - Texture.Height) / 2;

        spriteBatch.Draw(Texture, new Vector2(x, y), Color.White);
    }

    /// <summary>
    ///     Updates the cached texture if the text or color has changed.
    /// </summary>
    public void Update(
        string text,
        float fontSize,
        Color color,
        string fontFamily = "")
    {
        if ((text == RenderedContent) && (color == RenderedColor))
            return;

        Texture?.Dispose();

        Texture = string.IsNullOrEmpty(text)
            ? null
            : TextRenderer.RenderText(
                Device,
                text,
                fontSize,
                color,
                fontFamily);

        RenderedContent = text;
        RenderedColor = color;
    }
}