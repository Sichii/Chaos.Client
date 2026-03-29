#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Vertical darkness gradient overlay for NPC dialogs. Covers the bottom portion of the screen (y=254 to y=480),
///     fading linearly from fully transparent at the top to near-opaque black at the bottom. Matches the original client's
///     AlphaScreenPane (palette index 0x1F shadow color, 0-32 alpha scale).
/// </summary>
public sealed class AlphaScreenPane : UIElement
{
    private static Texture2D? GradientTexture;

    public AlphaScreenPane()
    {
        X = 0;
        Y = 274;
        Width = 640;
        Height = 98;
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

        const int height = 98;

        GradientTexture = new Texture2D(ChaosGame.Device, 1, height);
        var pixels = new Color[height];

        for (var i = 0; i < height; i++)
        {
            // Linear gradient: slightly dark at top, near-opaque black at bottom
            // Shifted +1 on the 0-32 darkness scale (~8/255) to darken uniformly
            var alpha = (byte)Math.Min(255, i * 247 / (height - 1) + 8);

            pixels[i] = new Color(
                (byte)0,
                (byte)0,
                (byte)0,
                alpha);
        }

        GradientTexture.SetData(pixels);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}