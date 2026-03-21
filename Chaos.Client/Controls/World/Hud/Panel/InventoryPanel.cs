#region
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
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

    public void SetSlot(byte slot, ushort sprite, DisplayColor color)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        control.NormalTexture?.Dispose();
        control.NormalTexture = UiRenderer.Instance!.GetItemIcon(sprite, color);
    }
}