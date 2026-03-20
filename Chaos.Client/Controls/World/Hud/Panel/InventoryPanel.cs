#region
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Inventory item grid panel (A key). 59 slots, no secondary page. Items rendered from Legend.dat item EPFs with
///     itempal palette lookup.
/// </summary>
public sealed class InventoryPanel : PanelBase
{
    private const int MAX_SLOTS = 59;

    public InventoryPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet)
        : base(
            device,
            hudPrefabSet,
            MAX_SLOTS,
            gridOffsetX: 7,
            gridOffsetY: 5)
        => Name = "Inventory";

    protected override Texture2D? RenderIcon(ushort spriteId) => UiRenderer.Instance!.GetItemIcon(spriteId);
}