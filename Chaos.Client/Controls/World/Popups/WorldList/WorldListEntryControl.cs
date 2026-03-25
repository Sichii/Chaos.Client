#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.WorldList;

/// <summary>
///     A single row in the world list panel: title + name + social status icon (far right).
/// </summary>
public sealed class WorldListEntryControl : UIPanel
{
    private const int ICON_SIZE = 11;
    private const int TITLE_WIDTH = 134;
    private const int NAME_WIDTH = 91;

    private readonly UIImage Icon;
    private readonly UILabel NameLabel;
    private readonly UILabel TitleLabel;

    public WorldListEntryControl(int rowWidth)
    {
        Height = 12;

        TitleLabel = new UILabel
        {
            Name = "Title",
            X = 0,
            Y = 0,
            Width = TITLE_WIDTH,
            Height = 12,
            Alignment = TextAlignment.Right,
            PaddingLeft = 0
        };

        AddChild(TitleLabel);

        NameLabel = new UILabel
        {
            Name = "Name",
            X = TITLE_WIDTH,
            Y = 0,
            Width = NAME_WIDTH,
            Height = 12,
            Alignment = TextAlignment.Right,
            PaddingLeft = 0
        };

        AddChild(NameLabel);

        Icon = new UIImage
        {
            Name = "StatusIcon",
            X = rowWidth - ICON_SIZE,
            Y = (12 - ICON_SIZE) / 2,
            Width = ICON_SIZE,
            Height = ICON_SIZE
        };

        AddChild(Icon);
    }

    public void Clear()
    {
        TitleLabel.Text = string.Empty;
        NameLabel.Text = string.Empty;
        Icon.Texture = null;
        Visible = false;
    }

    public void SetEntry(WorldListEntry entry, Texture2D? statusIcon, Color nameColor)
    {
        TitleLabel.Text = entry.Title ?? string.Empty;
        NameLabel.ForegroundColor = nameColor;
        NameLabel.Text = entry.Name;
        Icon.Texture = statusIcon;
        Visible = true;
    }
}