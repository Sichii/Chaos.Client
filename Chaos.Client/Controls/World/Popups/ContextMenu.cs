#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     A floating context menu that appears at a screen position with a list of text options. Clicking an option fires its
///     callback. Clicking outside or pressing Escape dismisses it.
/// </summary>
public sealed class ContextMenu : UIPanel
{
    private const int ITEM_HEIGHT = 14;
    private const int PADDING_X = 6;
    private const int PADDING_Y = 2;

    private static readonly Color HoverColor = new(
        80,
        120,
        200,
        150);

    private readonly List<MenuItem> Items = [];
    private int HoveredIndex = -1;

    public ContextMenu()
    {
        Visible = false;

        BackgroundColor = new Color(
            0,
            0,
            0,
            200);
        BorderColor = Color.Gray;
    }

    private void ClearItems()
    {
        foreach (var item in Items)
            RemoveChild(item.Label.Name);

        Items.Clear();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || (Items.Count == 0))
            return;

        // Set hover highlight as BackgroundColor on the hovered label (draws behind text)
        for (var i = 0; i < Items.Count; i++)
            Items[i].Label.BackgroundColor = i == HoveredIndex ? HoverColor : null;

        // Draw background/border + children (labels with hover bg)
        base.Draw(spriteBatch);
    }

    public void Hide()
    {
        Visible = false;
        ClearItems();
    }

    public void Show(int screenX, int screenY, params (string Text, Action Callback)[] options)
    {
        ClearItems();

        var maxWidth = 0;

        for (var i = 0; i < options.Length; i++)
        {
            (var text, var callback) = options[i];

            var label = new UILabel
            {
                Name = $"MenuItem{i}",
                X = 1,
                Y = PADDING_Y + i * ITEM_HEIGHT,
                Height = ITEM_HEIGHT,
                Text = text,
                PaddingLeft = PADDING_X - 1,
                PaddingTop = 1
            };

            AddChild(label);
            Items.Add(new MenuItem(label, callback));

            var textWidth = TextRenderer.MeasureWidth(text);

            if (textWidth > maxWidth)
                maxWidth = textWidth;
        }

        X = screenX;
        Y = screenY;
        Width = maxWidth + PADDING_X * 2;
        Height = Items.Count * ITEM_HEIGHT + PADDING_Y * 2;

        // Set label widths to fill the menu (matching original hover rect width)
        foreach (var item in Items)
            item.Label.Width = Width - 2;

        // Clamp to screen bounds
        if ((X + Width) > ChaosGame.VIRTUAL_WIDTH)
            X = ChaosGame.VIRTUAL_WIDTH - Width;

        if ((Y + Height) > ChaosGame.VIRTUAL_HEIGHT)
            Y = ChaosGame.VIRTUAL_HEIGHT - Height;

        HoveredIndex = -1;
        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible)
            return;

        var localX = input.MouseX - ScreenX;
        var localY = input.MouseY - ScreenY;

        // Check hover
        if ((localX >= 0) && (localX < Width) && (localY >= PADDING_Y) && (localY < (Height - PADDING_Y)))
            HoveredIndex = (localY - PADDING_Y) / ITEM_HEIGHT;
        else
            HoveredIndex = -1;

        // Left click — select option or dismiss
        if (input.WasLeftButtonPressed)
        {
            if ((HoveredIndex >= 0) && (HoveredIndex < Items.Count))
            {
                Items[HoveredIndex]
                    .Callback();
                Hide();
            } else
                Hide();

            return;
        }

        // Right click or Escape — dismiss
        if (input.WasRightButtonPressed || input.WasKeyPressed(Keys.Escape))
            Hide();
    }

    private record struct MenuItem(UILabel Label, Action Callback);
}