#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

public sealed class HealthBar : UIElement
{
    private const int TOTAL_WIDTH = 27;
    private const int TOTAL_HEIGHT = 5;
    private const int INNER_WIDTH = TOTAL_WIDTH - 2;
    private const int INNER_HEIGHT = TOTAL_HEIGHT - 2;
    private const float DURATION_MS = 2000f;

    private static readonly Color FrameColor = Color.Black;
    private static readonly Color HighColor = new(0, 97, 0);
    private static readonly Color MidColor = new(247, 142, 24);
    private static readonly Color LowColor = new(206, 0, 16);

    private float ElapsedMs;
    public byte HealthPercent { get; set; }

    public uint EntityId { get; }
    public bool IsExpired => ElapsedMs >= DURATION_MS;

    public HealthBar(uint entityId, byte healthPercent)
    {
        EntityId = entityId;
        HealthPercent = healthPercent;
        Width = TOTAL_WIDTH;
        Height = TOTAL_HEIGHT;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var pixel = GetPixel();
        var innerX = ScreenX + 1;
        var innerY = ScreenY + 1;
        var fillWidth = (int)(INNER_WIDTH * (HealthPercent / 100f));

        // Border only — unfilled area is transparent
        DrawBorder(
            spriteBatch,
            new Rectangle(
                ScreenX,
                ScreenY,
                TOTAL_WIDTH,
                TOTAL_HEIGHT),
            FrameColor);

        // Fill
        if (fillWidth > 0)
        {
            var fillColor = HealthPercent switch
            {
                > 52 => HighColor,
                > 24 => MidColor,
                _    => LowColor
            };

            spriteBatch.Draw(
                pixel,
                new Rectangle(
                    innerX,
                    innerY,
                    fillWidth,
                    INNER_HEIGHT),
                fillColor);
        }
    }

    public void Reset(byte healthPercent)
    {
        HealthPercent = healthPercent;
        ElapsedMs = 0;
    }

    public override void Update(GameTime gameTime, InputBuffer input) => ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
}