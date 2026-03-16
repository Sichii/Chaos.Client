#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public class UIButton : UIElement
{
    // True when mouse was pressed down while hovering this button
    private bool PressedInside;
    public Texture2D? HoverTexture { get; set; }
    public bool IsHovered { get; private set; }
    public bool IsPressed { get; private set; }
    public bool IsSelected { get; set; }
    public Texture2D? NormalTexture { get; set; }
    public Texture2D? PressedTexture { get; set; }
    public Texture2D? SelectedTexture { get; set; }

    public override void Dispose()
    {
        NormalTexture?.Dispose();
        NormalTexture = null;
        PressedTexture?.Dispose();
        PressedTexture = null;
        HoverTexture?.Dispose();
        HoverTexture = null;
        SelectedTexture?.Dispose();
        SelectedTexture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var texture = IsSelected && SelectedTexture is not null
            ? SelectedTexture
            : Enabled && IsPressed && PressedTexture is not null
                ? PressedTexture
                : Enabled && IsHovered && HoverTexture is not null
                    ? HoverTexture
                    : NormalTexture;

        if (texture is null)
            return;

        spriteBatch.Draw(texture, new Vector2(ScreenX, ScreenY), Color.White);
    }

    public event Action? OnClick;

    public void PerformClick() => OnClick?.Invoke();

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
        {
            IsHovered = false;
            IsPressed = false;
            PressedInside = false;

            return;
        }

        var hovering = ContainsPoint(input.MouseX, input.MouseY);
        IsHovered = hovering;

        // Track press origin — only buttons pressed while hovering can fire
        if (input.WasLeftButtonPressed && hovering)
            PressedInside = true;

        // Show pressed visual while held inside the button
        IsPressed = PressedInside && input.IsLeftButtonHeld && hovering;

        // Fire on release if the press originated inside and cursor is still inside
        if (input.WasLeftButtonReleased)
        {
            if (PressedInside && hovering)
                OnClick?.Invoke();

            PressedInside = false;
        }
    }
}