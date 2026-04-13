#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

// ReSharper disable once ClassCanBeSealed.Global
public class UIImage : UIElement
{
    public Texture2D? Texture { get; set; }

    public override void Dispose()
    {
        Texture?.Dispose();
        Texture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        //always run base.Draw so ClipRect updates for hit-testing — even when Texture is null.
        //a textureless visible image still has bounds and may be hit-tested.
        base.Draw(spriteBatch);

        if (Texture is null)
            return;

        DrawTexture(
            spriteBatch,
            Texture,
            new Vector2(ScreenX, ScreenY),
            Color.White);
    }

}