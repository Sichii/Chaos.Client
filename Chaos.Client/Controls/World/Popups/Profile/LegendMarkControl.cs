#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     A single legend mark row with an icon (UIImage) and text label (UILabel). The text is vertically centered relative
///     to the icon.
/// </summary>
public sealed class LegendMarkControl : UIElement
{
    private const int ICON_TEXT_GAP = 5;

    private readonly UIImage Icon;
    private readonly TextElement TextElement;
    private int IconHeight;

    private int IconWidth;

    public LegendMarkControl()
    {
        Icon = new UIImage();
        TextElement = new TextElement();
    }

    public void Clear()
    {
        Icon.Texture = null;
        TextElement.Update(string.Empty, Color.White);
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
        var textHeight = TextElement.Height;
        var textY = sy + (IconHeight - textHeight) / 2;
        TextElement.Draw(spriteBatch, new Vector2(sx + IconWidth + ICON_TEXT_GAP, textY));
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
        TextElement.Update(text, color);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}