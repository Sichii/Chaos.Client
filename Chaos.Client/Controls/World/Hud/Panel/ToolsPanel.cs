#region
using Chaos.Client.Data.Models;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Tools/utility panel (H key). Uses _ninvs3.spf (3-line variant) as background.
///     Same grid layout as InventoryPanel (PanelBaseControl).
/// </summary>
public sealed class ToolsPanel : PanelBase
{
    private const int TOOL_SLOTS = 36;

    public ToolsPanel(ControlPrefabSet hudPrefabSet, Texture2D? background = null, int normalVisibleSlots = DEFAULT_VISIBLE_SLOTS)
        : base(
            hudPrefabSet,
            TOOL_SLOTS,
            background: background,
            normalVisibleSlots: normalVisibleSlots)
        => Name = "Tools";

    protected override Texture2D RenderIcon(ushort spriteId) => UiRenderer.Instance!.GetItemIcon(spriteId);
}