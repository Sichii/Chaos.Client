#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Loading/progress screen using _nload prefab. Displays a background with an animated progress bar composed of
///     _nloadb0.spf (start cap), _nloadb1.spf (fill), _nloadb2.spf (end cap).
/// </summary>
public class LoadingControl : UIPanel
{
    private readonly Texture2D? BarEndTexture;
    private readonly Texture2D? BarFillTexture;
    private readonly int BarMaxWidth;
    private readonly Texture2D? BarStartTexture;
    private readonly int BarY;
    private readonly GraphicsDevice Device;

    private float Progress;

    public LoadingControl(GraphicsDevice device)
    {
        Device = device;
        Name = "Loading";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_nload");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _nload control prefab set");

        // Anchor — panel dimensions and background
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;
        X = (640 - Width) / 2;
        Y = (480 - Height) / 2;

        if (anchor.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);

        // Load progress bar segments from SPF files
        BarStartTexture = TextureConverter.LoadSpfTexture(device, "_nloadb0.spf");
        BarFillTexture = TextureConverter.LoadSpfTexture(device, "_nloadb1.spf");
        BarEndTexture = TextureConverter.LoadSpfTexture(device, "_nloadb2.spf");

        // Position the progress bar at the bottom third of the panel
        var barStartHeight = BarStartTexture?.Height ?? 0;
        BarY = Height - barStartHeight - 20;
        BarMaxWidth = Width - 40;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if (BarStartTexture is null || BarFillTexture is null || BarEndTexture is null)
            return;

        if (Progress <= 0f)
            return;

        var sx = ScreenX;
        var sy = ScreenY;
        var barStartX = sx + 20;
        var barDrawY = sy + BarY;

        var startWidth = BarStartTexture.Width;
        var endWidth = BarEndTexture.Width;
        var fillWidth = BarFillTexture.Width;
        var totalFillableWidth = BarMaxWidth - startWidth - endWidth;
        var filledWidth = (int)(totalFillableWidth * Progress);

        // Start cap
        spriteBatch.Draw(BarStartTexture, new Vector2(barStartX, barDrawY), Color.White);

        // Fill tiles
        var fillX = barStartX + startWidth;

        for (var drawn = 0; drawn < filledWidth; drawn += fillWidth)
        {
            var remaining = filledWidth - drawn;
            var drawWidth = Math.Min(fillWidth, remaining);

            spriteBatch.Draw(
                BarFillTexture,
                new Rectangle(
                    fillX + drawn,
                    barDrawY,
                    drawWidth,
                    BarFillTexture.Height),
                new Rectangle(
                    0,
                    0,
                    drawWidth,
                    BarFillTexture.Height),
                Color.White);
        }

        // End cap (only at 100% or near-complete)
        if (Progress >= 0.99f)
            spriteBatch.Draw(BarEndTexture, new Vector2(fillX + filledWidth, barDrawY), Color.White);
    }

    public void Hide() => Visible = false;

    public void SetProgress(float progress) => Progress = Math.Clamp(progress, 0f, 1f);

    public void Show(float initialProgress = 0f)
    {
        Progress = Math.Clamp(initialProgress, 0f, 1f);
        Visible = true;
    }
}