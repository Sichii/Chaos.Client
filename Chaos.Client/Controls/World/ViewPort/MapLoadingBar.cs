#region
using Chaos.Client.Controls.Components;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Map loading screen using _nloadm prefab. Shown during map transitions within the game world.
/// </summary>
public class MapLoadingBar : PrefabPanel
{
    public MapLoadingBar()
        : base("_nloadm")
    {
        Name = "MapLoading";
        Visible = false;
    }
}