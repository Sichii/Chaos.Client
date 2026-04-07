#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

// ReSharper disable once ClassCanBeSealed.Global
public class UIAnimatedImage : UIElement
{
    private int FrameDirection = 1;
    private double FrameTimer;
    public int CurrentFrame { get; private set; }
    public int FrameIntervalMs { get; set; } = 100;
    public Texture2D[] Frames { get; init; } = [];
    public bool Looping { get; set; } = true;
    public bool PingPong { get; set; }

    public override void Dispose()
    {
        foreach (var frame in Frames)
            frame.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || (Frames.Length == 0))
            return;

        base.Draw(spriteBatch);

        var frame = Frames[CurrentFrame];

        AtlasHelper.Draw(
            spriteBatch,
            frame,
            new Vector2(ScreenX, ScreenY),
            Color.White);
    }

    public override void Update(GameTime gameTime)
    {
        if (Frames.Length <= 1)
            return;

        FrameTimer += gameTime.ElapsedGameTime.TotalMilliseconds;

        if (FrameTimer < FrameIntervalMs)
            return;

        FrameTimer -= FrameIntervalMs;

        var nextFrame = CurrentFrame + FrameDirection;

        if (nextFrame >= Frames.Length)
        {
            if (PingPong)
            {
                FrameDirection = -1;
                nextFrame = CurrentFrame + FrameDirection;
            } else if (Looping)
                nextFrame = 0;
            else
                return;
        } else if (nextFrame < 0)
        {
            FrameDirection = 1;
            nextFrame = CurrentFrame + FrameDirection;
        }

        CurrentFrame = nextFrame;
    }
}