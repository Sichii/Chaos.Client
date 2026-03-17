#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Tools/utility panel (H key). Uses _ninvs3.spf (3-line variant) as background.
///     Same grid layout as InventoryPanel (PanelBaseControl).
/// </summary>
public sealed class ToolsPanel : PanelBaseControl
{
    private const int TOOL_SLOTS = 36;

    public ToolsPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet)
        : base(device, hudPrefabSet, TOOL_SLOTS)
    {
        Name = "Tools";

        // Override the default InventoryBackground with the tools-specific background
        Background?.Dispose();
        Background = TextureConverter.LoadSpfTexture(device, "_ninvs3.spf");
    }

    protected override Texture2D? RenderIcon(ushort spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelItems.GetPanelItemSprite(spriteId));
}