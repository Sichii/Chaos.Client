#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     A button that alternates between two textures on a fixed interval when blinking is enabled.
/// </summary>
public class BlinkButton : UIButton
{
    private const float BLINK_INTERVAL_MS = 500f;

    private Texture2D? BlinkFrameA;
    private Texture2D? BlinkFrameB;
    private float BlinkTimer;
    private bool BlinkPhase;
    private bool Blinking;

    /// <summary>
    ///     Sets the two textures to alternate between when blinking.
    ///     Frame A is also assigned as the initial NormalTexture.
    /// </summary>
    public void SetBlinkFrames(Texture2D frameA, Texture2D frameB)
    {
        BlinkFrameA = frameA;
        BlinkFrameB = frameB;
        NormalTexture = frameA;
    }

    public void SetBlinking(bool enabled)
    {
        if (Blinking == enabled)
            return;

        Blinking = enabled;
        BlinkTimer = 0;
        BlinkPhase = false;
        NormalTexture = BlinkFrameA;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible)
            return;

        base.Update(gameTime);

        if (!Blinking || BlinkFrameA is null || BlinkFrameB is null)
            return;

        BlinkTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (BlinkTimer < BLINK_INTERVAL_MS)
            return;

        BlinkTimer -= BLINK_INTERVAL_MS;
        BlinkPhase = !BlinkPhase;
        NormalTexture = BlinkPhase ? BlinkFrameB : BlinkFrameA;
    }
}
