#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

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

        spriteBatch.Draw(Texture, new Vector2(ScreenX, ScreenY), Color.White);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}