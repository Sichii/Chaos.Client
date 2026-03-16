#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Read-only text label that participates in the UI element tree. Wraps CachedText for efficient re-rendering only
///     when content changes.
/// </summary>
public class UILabel : UIElement
{
    private readonly CachedText Cache;
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public float FontSize { get; set; }
    public int PaddingLeft { get; set; } = 1;
    public int PaddingTop { get; set; } = 1;

    public string Text { get; private set; } = string.Empty;
    public Color TextColor { get; private set; } = Color.White;

    public UILabel(GraphicsDevice device) => Cache = new CachedText(device);

    public override void Dispose()
    {
        Cache.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Cache.Texture is null)
            return;

        Cache.Alignment = Alignment;

        Cache.Draw(
            spriteBatch,
            new Rectangle(
                ScreenX + PaddingLeft,
                ScreenY + PaddingTop,
                Width - PaddingLeft,
                Height - PaddingTop));
    }

    public void SetText(string text, Color? color = null)
    {
        Text = text;

        if (color.HasValue)
            TextColor = color.Value;

        Cache.Update(text, FontSize, TextColor);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}