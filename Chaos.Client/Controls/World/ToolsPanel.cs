#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Tools/utility panel (H key). Secondary inventory-style panel for additional items or quick-access tools. Background
///     loaded from _ninvs1.spf (1-line variant). Uses the same grid layout as InventoryPanel (PanelBaseControl).
/// </summary>
public class ToolsPanel : PanelBaseControl
{
    private const int TOOL_SLOTS = 36;

    public ToolsPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet)
        : base(device, hudPrefabSet, TOOL_SLOTS)
        => Name = "Tools";

    protected override Texture2D? RenderIcon(ushort spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelItems.GetPanelItemSprite(spriteId));
}