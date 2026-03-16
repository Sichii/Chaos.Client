#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Map loading screen using _nloadm prefab. Shown during map transitions within the game world.
/// </summary>
public class MapLoadingControl : UIPanel
{
    public MapLoadingControl(GraphicsDevice device)
    {
        Name = "MapLoading";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_nloadm");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _nloadm control prefab set");

        // Anchor — panel dimensions and background
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;
        X = (640 - Width) / 2;
        Y = (480 - Height) / 2;

        if (anchor.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);
    }

    public void Hide() => Visible = false;

    public void Show() => Visible = true;
}