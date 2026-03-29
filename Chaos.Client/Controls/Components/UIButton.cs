#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public class UIButton : UIElement
{
    // True when mouse was pressed down while hovering this button
    private bool PressedInside;

    /// <summary>
    ///     When true, textures smaller than the element's Width/Height are drawn centered. The element stays the size of the
    ///     largest texture. Used for buttons with differently-sized textures per state (e.g. status book tabs: small normal,
    ///     big selected).
    /// </summary>
    public bool CenterTexture { get; set; }

    public Texture2D? DisabledTexture { get; set; }
    public Texture2D? HoverTexture { get; set; }
    public bool IsHovered { get; private set; }
    public bool IsPressed { get; private set; }
    public bool IsSelected { get; set; }
    public Texture2D? NormalTexture { get; set; }
    public Texture2D? PressedTexture { get; set; }
    public Texture2D? SelectedTexture { get; set; }

    private Texture2D? ActiveTexture
    {
        get
        {
            if (!Enabled && DisabledTexture is not null)
                return DisabledTexture;

            if (IsSelected && SelectedTexture is not null)
                return SelectedTexture;

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (Enabled && IsPressed && PressedTexture is not null)
                return PressedTexture;

            if (Enabled && IsHovered && HoverTexture is not null)
                return HoverTexture;

            return NormalTexture;
        }
    }

    public override void Dispose()
    {
        // SelectedTexture may share the same object as PressedTexture — only dispose if distinct
        if (SelectedTexture is not null && (SelectedTexture != PressedTexture))
            SelectedTexture.Dispose();

        SelectedTexture = null;

        NormalTexture?.Dispose();
        NormalTexture = null;
        PressedTexture?.Dispose();
        PressedTexture = null;
        HoverTexture?.Dispose();
        HoverTexture = null;
        DisabledTexture?.Dispose();
        DisabledTexture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var texture = ActiveTexture;

        if (texture is null)
            return;

        var drawX = ScreenX;
        var drawY = ScreenY;

        if (CenterTexture)
        {
            drawX += (Width - texture.Width) / 2;
            drawY += (Height - texture.Height) / 2;
        }

        AtlasHelper.Draw(
            spriteBatch,
            texture,
            new Vector2(drawX, drawY),
            Color.White);
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