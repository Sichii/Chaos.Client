#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.SelfProfile;

/// <summary>
///     A generic tab page within the status book. Loaded from a tab-specific prefab (_nui_sk, _nui_dr, etc.).
/// </summary>
public sealed class StatusBookTabPage : PrefabPanel
{
    public StatusBookTabPage(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        AutoPopulate();
    }
}