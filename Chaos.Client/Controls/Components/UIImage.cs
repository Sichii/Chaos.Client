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
        if (!Visible || Texture is null)
            return;

        base.Draw(spriteBatch);

        DrawTexture(
            spriteBatch,
            Texture,
            new Vector2(ScreenX, ScreenY),
            Color.White);
    }

}