#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     A floating context menu that appears at a screen position with a list of text options. Clicking an option fires its
///     callback. Clicking outside or pressing Escape dismisses it.
/// </summary>
public sealed class ContextMenu : UIElement
{
    private const int ITEM_HEIGHT = 14;
    private const int PADDING_X = 6;
    private const int PADDING_Y = 2;

    private readonly GraphicsDevice Device;
    private readonly List<MenuItem> Items = [];
    private int HoveredIndex = -1;

    public ContextMenu(GraphicsDevice device)
    {
        Device = device;
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
            item.Cache.Dispose();

        Items.Clear();
    }

    public override void Dispose()
    {
        ClearItems();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || (Items.Count == 0))
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        for (var i = 0; i < Items.Count; i++)
        {
            var itemY = sy + PADDING_Y + i * ITEM_HEIGHT;

            if (i == HoveredIndex)
                DrawRect(
                    spriteBatch,
                    Device,
                    new Rectangle(
                        sx + 1,
                        itemY,
                        Width - 2,
                        ITEM_HEIGHT),
                    new Color(
                        80,
                        120,
                        200,
                        150));

            Items[i]
                .Cache
                .Draw(spriteBatch, new Vector2(sx + PADDING_X, itemY + 1));
        }
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

        foreach ((var text, var callback) in options)
        {
            var cache = new CachedText(Device);
            cache.Update(text, Color.White);
            Items.Add(new MenuItem(text, cache, callback));

            var textWidth = TextRenderer.MeasureWidth(text);

            if (textWidth > maxWidth)
                maxWidth = textWidth;
        }

        X = screenX;
        Y = screenY;
        Width = maxWidth + PADDING_X * 2;
        Height = Items.Count * ITEM_HEIGHT + PADDING_Y * 2;

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

    private record struct MenuItem(string Text, CachedText Cache, Action Callback);
}