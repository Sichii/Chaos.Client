#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Inventory item grid panel (A key). 59 slots, no secondary page. Items rendered from Legend.dat item EPFs with
///     itempal palette lookup.
/// </summary>
public class InventoryPanel : PanelBaseControl
{
    private const int MAX_SLOTS = 59;
    protected override int CellHeight => 33;
    protected override int CellWidth => 36;
    protected override int GridOffsetX => 7;
    protected override int GridOffsetY => 5;

    public InventoryPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet)
        : base(device, hudPrefabSet, MAX_SLOTS)
        => Name = "Inventory";

    protected override Texture2D? RenderIcon(ushort spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelItems.GetPanelItemSprite(spriteId));
}