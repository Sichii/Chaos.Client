#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Simple FPS counter that updates once per second. Renders as a cached text label at its screen position.
/// </summary>
public sealed class FpsCounter : UIElement
{
    private readonly CachedText Text;
    private int Display;
    private float Elapsed;
    private int FrameCount;

    public FpsCounter()
    {
        Name = "FpsCounter";
        Text = new CachedText();
    }

    public override void Dispose()
    {
        Text.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        Text.Draw(spriteBatch, new Vector2(ScreenX, ScreenY));
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        FrameCount++;
        Elapsed += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (Elapsed < 1000f)
            return;

        var prev = Display;
        Display = FrameCount;
        FrameCount = 0;
        Elapsed -= 1000f;

        if (prev != Display)
            Text.Update($"FPS: {Display}", Color.White);
    }
}