#region
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Debug overlay that draws colored outlines and centered names for all visible UI elements. Toggle with F12.
/// </summary>
public static class DebugOverlay
{
    public static bool IsActive { get; set; }

    public static void Draw(SpriteBatch spriteBatch, GraphicsDevice device, UIPanel root)
    {
        if (!IsActive)
            return;

        spriteBatch.Begin(SpriteSortMode.Immediate, samplerState: GlobalSettings.Sampler);

        foreach (var child in root.Children)
            DrawElement(spriteBatch, device, child);

        spriteBatch.End();
    }

    private static void DrawElement(SpriteBatch spriteBatch, GraphicsDevice device, UIElement element)
    {
        if (!element.Visible)
            return;

        var sx = element.ScreenX;
        var sy = element.ScreenY;
        var w = element.Width;
        var h = element.Height;

        // Use texture dimensions as fallback when Width/Height are 0
        if ((w == 0) && (h == 0) && element is UIPanel { Background: not null } bgPanel)
        {
            w = bgPanel.Background.Width;
            h = bgPanel.Background.Height;
        }

        if ((w > 0) && (h > 0))
        {
            var color = element switch
            {
                UIButton  => Color.Lime,
                UITextBox => Color.Cyan,
                UILabel   => Color.Yellow,
                UIImage   => Color.Magenta,
                UIPanel   => Color.Red,
                _         => Color.White
            };

            var borderColor = color * 0.8f;

            UIElement.DrawBorder(
                spriteBatch,
                device,
                new Rectangle(
                    sx,
                    sy,
                    w,
                    h),
                borderColor);

            // Name label centered in bounds with dark background
            var name = element.Name.Length > 0
                ? element.Name
                : element.GetType()
                         .Name;

            var nameTexture = TextRenderer.RenderText(device, name, color);

            var tw = nameTexture.Width;
            var th = nameTexture.Height;
            var tx = sx + (w - tw) / 2;
            var ty = sy + (h - th) / 2;

            UIElement.DrawRect(
                spriteBatch,
                device,
                new Rectangle(
                    tx - 1,
                    ty - 1,
                    tw + 2,
                    th + 2),
                Color.Black * 0.7f);
            spriteBatch.Draw(nameTexture, new Vector2(tx, ty), Color.White);
            nameTexture.Dispose();
        }

        if (element is UIPanel panel)
            foreach (var child in panel.Children)
                DrawElement(spriteBatch, device, child);
    }

    public static void Toggle() => IsActive = !IsActive;
}