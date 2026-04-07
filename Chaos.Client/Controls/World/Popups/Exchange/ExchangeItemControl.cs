#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Controls.World.Popups.Exchange;

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

    private readonly UILabel NameLabel;

    public ExchangeItemControl()
    {
        Width = TEXT_OFFSET_X + 200;
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

        NameLabel = new UILabel
        {
            Name = "ItemName",
            X = TEXT_OFFSET_X,
            Y = (Height - TextRenderer.CHAR_HEIGHT) / 2,
            Width = 200,
            Height = TextRenderer.CHAR_HEIGHT
        };

        AddChild(NameLabel);
    }

    public void ClearItem()
    {
        Icon.Texture = null;
        Visible = false;
    }

    public void SetItem(ushort sprite, string name)
    {
        Icon.Texture = UiRenderer.Instance!.GetItemIcon(sprite);
        NameLabel.Text = name;
        NameLabel.ForegroundColor = Color.White;
        Visible = true;
    }
}