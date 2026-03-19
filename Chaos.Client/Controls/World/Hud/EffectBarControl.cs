#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Controls.World.Hud;

/// <summary>
///     Vertical status effect bar on the right side of the HUD. Shows active persistent effects as half-size spell icons
///     with 2px-high horizontal color bars underneath each icon. Uses spelled.epf as the background strip (22x212). Only
///     visible when at least one effect is active. 10 slots, keyed by EffectIcon.
/// </summary>
public sealed class EffectBarControl : UIPanel
{
    private const int MAX_EFFECTS = 10;
    private const int SLOT_SIZE = 24;
    private const int SLOT_WIDTH = 22;
    private const int ICON_SIZE = 15;
    private const int ICON_OFFSET_X = 3;
    private const int ICON_OFFSET_Y = 3;
    private const int BAR_HEIGHT = 2;
    private const int BAR_OFFSET_X = 3;
    private readonly Texture2D? BackgroundTexture;

    private readonly GraphicsDevice Device;
    private readonly ActiveEffectEntry[] Effects = new ActiveEffectEntry[MAX_EFFECTS];
    private int ActiveCount;

    public EffectBarControl(GraphicsDevice device)
    {
        Device = device;
        Name = "EffectBar";
        Width = SLOT_WIDTH;
        Height = MAX_EFFECTS * SLOT_SIZE;
        Visible = false;

        // Load background strip from spelled.epf (22x212)
        var images = DataContext.UserControls.GetEpfImages("spelled.epf");

        if (images.Length > 0)
        {
            BackgroundTexture = TextureConverter.ToTexture2D(device, images[0]);

            foreach (var img in images)
                img?.Dispose();
        }

        for (var i = 0; i < MAX_EFFECTS; i++)
            Effects[i] = new ActiveEffectEntry();
    }

    public void ClearEffects()
    {
        for (var i = 0; i < MAX_EFFECTS; i++)
        {
            Effects[i]
                .IconTexture
                ?.Dispose();
            Effects[i].IconTexture = null;
            Effects[i].Active = false;
        }

        ActiveCount = 0;
        UpdateVisibility();
    }

    public override void Dispose()
    {
        BackgroundTexture?.Dispose();

        for (var i = 0; i < MAX_EFFECTS; i++)
            Effects[i]
                .IconTexture
                ?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || (ActiveCount == 0))
            return;

        var sx = ScreenX;
        var sy = ScreenY;

        if (BackgroundTexture is not null)
            spriteBatch.Draw(BackgroundTexture, new Vector2(sx, sy), Color.White);

        var pixel = GetPixel(Device);

        for (var i = 0; i < MAX_EFFECTS; i++)
        {
            if (!Effects[i].Active)
                continue;

            var slotY = sy + i * SLOT_SIZE;

            // Draw half-size spell icon
            if (Effects[i].IconTexture is { } iconTex)
                spriteBatch.Draw(iconTex, new Vector2(sx + ICON_OFFSET_X, slotY + ICON_OFFSET_Y), Color.White);

            // Draw 2px-high horizontal color bar under the icon — width is a percentage of ICON_SIZE based on color
            var barPercent = GetBarPercent(Effects[i].Color);
            var barWidth = (int)(ICON_SIZE * barPercent);

            if (barWidth > 0)
            {
                var barColor = GetBarColor(Effects[i].Color);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(
                        sx + BAR_OFFSET_X,
                        slotY + ICON_OFFSET_Y + ICON_SIZE + 1,
                        barWidth,
                        BAR_HEIGHT),
                    barColor);
            }
        }
    }

    private static Color GetBarColor(EffectColor effectColor)
        => effectColor switch
        {
            EffectColor.Blue   => new Color(59, 82, 120),
            EffectColor.Green  => new Color(0, 180, 0),
            EffectColor.Yellow => Color.Yellow,
            EffectColor.Orange => new Color(255, 165, 0),
            EffectColor.Red    => Color.Red,
            EffectColor.White  => Color.White,
            _                  => Color.Transparent
        };

    private static float GetBarPercent(EffectColor effectColor)
        => effectColor switch
        {
            EffectColor.White  => 1f,
            EffectColor.Red    => 5f / 6f,
            EffectColor.Orange => 4f / 6f,
            EffectColor.Yellow => 3f / 6f,
            EffectColor.Green  => 2f / 6f,
            EffectColor.Blue   => 1f / 6f,
            _                  => 0f
        };

    private void RemoveEffect(byte effectIcon)
    {
        for (var i = 0; i < MAX_EFFECTS; i++)
            if (Effects[i].Active && (Effects[i].Icon == effectIcon))
            {
                Effects[i]
                    .IconTexture
                    ?.Dispose();
                Effects[i].IconTexture = null;
                Effects[i].Active = false;
                ActiveCount--;
                UpdateVisibility();

                return;
            }
    }

    /// <summary>
    ///     Renders a spell icon at half size (15x15) via SkiaSharp downscale.
    /// </summary>
    private Texture2D? RenderHalfSizeIcon(byte iconId)
    {
        var palettized = DataContext.PanelIcons.GetSpellIcon(iconId);

        if (palettized is null)
            return null;

        using var fullImage = Graphics.RenderImage(palettized.Entity, palettized.Palette);

        if (fullImage is null)
            return null;

        // Downsample to 15x15
        var info = new SKImageInfo(
            ICON_SIZE,
            ICON_SIZE,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        surface.Canvas.DrawImage(
            fullImage,
            new SKRect(
                0,
                0,
                fullImage.Width,
                fullImage.Height),
            new SKRect(
                0,
                0,
                ICON_SIZE,
                ICON_SIZE),
            new SKPaint
            {
                FilterQuality = SKFilterQuality.Medium
            });

        using var halfImage = surface.Snapshot();

        return TextureConverter.ToTexture2D(Device, halfImage);
    }

    /// <summary>
    ///     Updates or adds an effect. EffectIcon acts as a key — only one effect per icon value. EffectColor None removes the
    ///     effect.
    /// </summary>
    public void SetEffect(byte effectIcon, EffectColor effectColor)
    {
        if (effectColor == EffectColor.None)
        {
            RemoveEffect(effectIcon);

            return;
        }

        // Check if this icon already exists — update color
        for (var i = 0; i < MAX_EFFECTS; i++)
            if (Effects[i].Active && (Effects[i].Icon == effectIcon))
            {
                Effects[i].Color = effectColor;

                return;
            }

        // Find first empty slot
        for (var i = 0; i < MAX_EFFECTS; i++)
            if (!Effects[i].Active)
            {
                Effects[i].Icon = effectIcon;
                Effects[i].Color = effectColor;
                Effects[i].Active = true;

                Effects[i]
                    .IconTexture
                    ?.Dispose();
                Effects[i].IconTexture = RenderHalfSizeIcon(effectIcon);
                ActiveCount++;
                UpdateVisibility();

                return;
            }
    }

    private void UpdateVisibility() => Visible = ActiveCount > 0;

    private sealed class ActiveEffectEntry
    {
        public bool Active { get; set; }
        public EffectColor Color { get; set; }
        public byte Icon { get; set; }
        public Texture2D? IconTexture { get; set; }
    }
}