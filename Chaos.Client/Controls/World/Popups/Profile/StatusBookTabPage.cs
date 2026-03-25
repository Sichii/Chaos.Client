#region
using Chaos.Client.Controls.Components;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     A generic tab page within the status book. Loaded from a tab-specific prefab (_nui_sk, _nui_dr, etc.).
/// </summary>
public sealed class StatusBookTabPage : PrefabPanel
{
    public StatusBookTabPage(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;
    }
}