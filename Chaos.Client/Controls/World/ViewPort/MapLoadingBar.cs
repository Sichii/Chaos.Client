#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Map loading screen using _nloadm prefab. Shown during map transitions within the game world.
/// </summary>
public class MapLoadingBar : PrefabPanel
{
    public MapLoadingBar(GraphicsDevice device)
        : base(device, "_nloadm")
    {
        Name = "MapLoading";
        Visible = false;
    }
}