#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Map loading screen using _nloadm prefab. Shown during map transitions within the game world.
/// </summary>
public class MapLoadingControl : PrefabPanel
{
    public MapLoadingControl(GraphicsDevice device)
        : base(device, "_nloadm")
    {
        Name = "MapLoading";
        Visible = false;
    }
}