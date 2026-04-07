#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Vertical darkness gradient overlay for NPC dialogs. Covers the bottom portion of the screen (y=254 to y=480),
///     fading linearly from fully transparent at the top to near-opaque black at the bottom. Matches the original client's
///     AlphaScreenPane (palette index 0x1F shadow color, 0-32 alpha scale).
/// </summary>
public sealed class DialogAlphaGradient : UIElement
{
    private static Texture2D? GradientTexture;

    public DialogAlphaGradient()
    {
        X = 0;
        Y = 274;
        Width = 640;
        Height = 98;
        IsHitTestVisible = false;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        EnsureGradientTexture();

        spriteBatch.Draw(
            GradientTexture!,
            new Rectangle(
                ScreenX,
                ScreenY,
                Width,
                Height),
            Color.White);
    }

    private static void EnsureGradientTexture()
    {
        if (GradientTexture is not null)
            return;

        const int HEIGHT = 98;

        GradientTexture = new Texture2D(ChaosGame.Device, 1, HEIGHT);
        var pixels = new Color[HEIGHT];

        for (var i = 0; i < HEIGHT; i++)
        {
            // Linear gradient: slightly dark at top, near-opaque black at bottom
            // Shifted +1 on the 0-32 darkness scale (~8/255) to darken uniformly
            var alpha = (byte)Math.Min(255, i * 247 / (HEIGHT - 1) + 8);

            pixels[i] = new Color(
                (byte)0,
                (byte)0,
                (byte)0,
                alpha);
        }

        GradientTexture.SetData(pixels);
    }

}