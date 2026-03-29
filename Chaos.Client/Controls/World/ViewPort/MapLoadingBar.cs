#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Map loading screen using _nloadm prefab. Shown during map transitions within the game world.
///     Uses _nloadb0.spf (start cap), _nloadb1.spf (fill), _nloadb2.spf (end cap) for the progress bar.
/// </summary>
public class MapLoadingBar : PrefabPanel
{
    private readonly UIImage? EndCap;
    private readonly UIProgressBar? FillBar;

    public MapLoadingBar()
        : base("_nloadm")
    {
        Name = "MapLoading";
        Visible = false;

        var cache = UiRenderer.Instance!;
        var startTexture = cache.GetSpfTexture("_nloadb0.spf");
        var fillTexture = cache.GetSpfTexture("_nloadb1.spf");
        var endTexture = cache.GetSpfTexture("_nloadb2.spf");

        var barY = Height - (startTexture?.Height ?? 0) - 20;
        var barX = 20;

        if (startTexture is not null)
            AddChild(
                new UIImage
                {
                    Texture = startTexture,
                    X = barX,
                    Y = barY
                });

        if (fillTexture is not null)
        {
            FillBar = new UIProgressBar
            {
                X = barX + (startTexture?.Width ?? 0),
                Y = barY,
                Width = fillTexture.Width,
                Height = fillTexture.Height,
                FillTexture = fillTexture
            };

            AddChild(FillBar);
        }

        if (endTexture is not null)
        {
            EndCap = new UIImage
            {
                Texture = endTexture,
                X = barX + (startTexture?.Width ?? 0) + (fillTexture?.Width ?? 0),
                Y = barY,
                Visible = false
            };

            AddChild(EndCap);
        }
    }

    public void SetProgress(float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        if (FillBar is not null)
            FillBar.Percent = progress;

        if (EndCap is not null)
            EndCap.Visible = progress >= 1f;
    }

    public void Show(float initialProgress = 0f)
    {
        SetProgress(initialProgress);
        Visible = true;
    }
}