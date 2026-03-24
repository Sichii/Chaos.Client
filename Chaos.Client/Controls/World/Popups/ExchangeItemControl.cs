#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     A single item row in the exchange panel. Displays a 32x32 icon on the left and the item name (with count for
///     stackables) vertically centered beside it.
/// </summary>
public sealed class ExchangeItemControl : UIPanel
{
    private const int ICON_SIZE = 32;
    private const int ICON_PADDING = 2;
    private const int TEXT_OFFSET_X = 36;
    private readonly UIImage Icon;

    private readonly CachedText NameText;

    public ExchangeItemControl()
    {
        Height = ICON_SIZE + ICON_PADDING * 2;
        Visible = false;

        Icon = new UIImage
        {
            Name = "Icon",
            X = ICON_PADDING,
            Y = ICON_PADDING,
            Width = ICON_SIZE,
            Height = ICON_SIZE
        };

        AddChild(Icon);

        NameText = new CachedText();
    }

    public void ClearItem()
    {
        Icon.Texture = null;
        Visible = false;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        // Draw name text vertically centered with the icon
        var textY = ScreenY + (Height - TextRenderer.CHAR_HEIGHT) / 2;
        NameText.Draw(spriteBatch, new Vector2(ScreenX + TEXT_OFFSET_X, textY));
    }

    public void SetItem(ushort sprite, string name)
    {
        Icon.Texture = UiRenderer.Instance!.GetItemIcon(sprite);
        NameText.Update(name, Color.White);
        Visible = true;
    }
}