#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     A simple horizontal progress bar. Draws a filled rectangle whose width is a percentage of the element's total
///     width. Optionally draws a background color behind the fill. Set <see cref="FillColor" /> and <see cref="Percent" />
///     to control appearance.
/// </summary>
public sealed class UIProgressBar : UIElement
{
    /// <summary>
    ///     The fill color of the progress portion. Null = no fill drawn.
    /// </summary>
    public Color? FillColor { get; set; }

    /// <summary>
    ///     Fill percentage from 0.0 to 1.0.
    /// </summary>
    public float Percent { get; set; }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var fillWidth = (int)(Width * Math.Clamp(Percent, 0f, 1f));

        if ((fillWidth > 0) && FillColor.HasValue)
            DrawRect(
                spriteBatch,
                spriteBatch.GraphicsDevice,
                new Rectangle(
                    ScreenX,
                    ScreenY,
                    fillWidth,
                    Height),
                FillColor.Value);
    }

    public override void Update(GameTime gameTime, InputBuffer input) { }
}