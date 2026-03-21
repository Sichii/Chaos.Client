#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.SelfProfile;

/// <summary>
///     A single legend mark row with an icon (UIImage) and text label (UILabel). The text is vertically centered relative
///     to the icon.
/// </summary>
public sealed class LegendMarkControl : UIElement
{
    private const int ICON_TEXT_GAP = 5;

    private readonly UIImage Icon;
    private readonly CachedText TextCache;
    private int IconHeight;

    private int IconWidth;

    public LegendMarkControl(GraphicsDevice device)
    {
        Icon = new UIImage();
        TextCache = new CachedText(device);
    }

    public void Clear()
    {
        Icon.Texture = null;
        TextCache.Update(string.Empty, Color.White);
    }

    public override void Dispose()
    {
        TextCache.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var sx = ScreenX;
        var sy = ScreenY;

        // Draw icon
        if (Icon.Texture is not null)
            AtlasHelper.Draw(
                spriteBatch,
                Icon.Texture,
                new Vector2(sx, sy),
                Color.White);

        // Draw text vertically centered relative to icon
        var textHeight = TextCache.Texture?.Height ?? 0;
        var textY = sy + (IconHeight - textHeight) / 2;
        TextCache.Draw(spriteBatch, new Vector2(sx + IconWidth + ICON_TEXT_GAP, textY));
    }

    public void SetMark(
        Texture2D? icon,
        string text,
        Color color,
        int iconWidth,
        int iconHeight)
    {
        Icon.Texture = icon;
        IconWidth = iconWidth;
        IconHeight = iconHeight;
        TextCache.Update(text, color);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}