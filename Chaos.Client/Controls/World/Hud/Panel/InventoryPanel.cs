#region
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Inventory item grid panel (A key). Thin view that subscribes to
///     <see cref="Inventory" /> change events and renders item icons with dye colors.
///     Supports expand toggle that grows the panel upward (3->5 rows in small HUD, 1->5 rows in large HUD).
/// </summary>
public sealed class InventoryPanel : PanelBase
{
    private const int MAX_SLOTS = 59;
    private const ushort GOLD_SPRITE = 136;
    private const int EXPANDED_SLOTS = 5 * COLUMNS;

    private readonly PanelSlot GoldSlot;
    private readonly Inventory InventoryState;

    public InventoryPanel(
        ControlPrefabSet hudPrefabSet,
        Inventory inventory,
        Texture2D? background = null,
        Texture2D? expandedBackground = null,
        int normalVisibleSlots = DEFAULT_VISIBLE_SLOTS)
        : base(
            hudPrefabSet,
            MAX_SLOTS,
            gridOffsetX: 7,
            gridOffsetY: 5,
            background: background,
            normalVisibleSlots: normalVisibleSlots)
    {
        Name = "Inventory";
        InventoryState = inventory;

        ConfigureExpand(expandedBackground ?? UiRenderer.Instance!.GetSpfTexture("_ninv5.spf"), EXPANDED_SLOTS);

        // Gold bag occupies the last visible grid cell, overlaying the slot at that index
        GoldSlot = new PanelSlot
        {
            Name = "GoldBag",
            Slot = 0,
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            NormalTexture = RenderIcon(GOLD_SPRITE),
            ZIndex = 1
        };

        GoldSlot.SlotName = $"Gold( {inventory.Gold} )";
        GoldSlot.OnDragStart += OnDragStarted;
        AddChild(GoldSlot);

        // Position gold at last visible cell and hide the slot underneath
        PositionGoldSlot(normalVisibleSlots - 1);

        // Subscribe to state events
        InventoryState.SlotChanged += OnSlotChanged;
        InventoryState.GoldChanged += OnGoldChanged;
        InventoryState.Cleared += OnCleared;
    }

    public override void Dispose()
    {
        InventoryState.SlotChanged -= OnSlotChanged;
        InventoryState.GoldChanged -= OnGoldChanged;
        InventoryState.Cleared -= OnCleared;

        base.Dispose();
    }

    protected override PanelSlot? FindHoveredSlot(InputBuffer input)
    {
        if (GoldSlot is { Visible: true } && GoldSlot.ContainsPoint(input.MouseX, input.MouseY))
            return GoldSlot;

        return base.FindHoveredSlot(input);
    }

    private void OnCleared()
    {
        foreach (var slot in Slots)
        {
            slot.NormalTexture?.Dispose();
            slot.NormalTexture = null;
            slot.SlotName = null;
            slot.CurrentDurability = 0;
            slot.MaxDurability = 0;
        }
    }

    private void OnGoldChanged() => GoldSlot.SlotName = $"Gold( {InventoryState.Gold} )";

    private void OnSlotChanged(byte slot)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        var data = InventoryState.GetSlot(slot);

        if (data.IsOccupied)
        {
            control.NormalTexture?.Dispose();
            control.NormalTexture = UiRenderer.Instance!.GetItemIcon(data.Sprite, data.Color);
            control.SlotName = data.Name;
            control.CurrentDurability = data.CurrentDurability;
            control.MaxDurability = data.MaxDurability;
        } else
        {
            control.NormalTexture?.Dispose();
            control.NormalTexture = null;
            control.SlotName = null;
            control.CooldownPercent = 0;
            control.CurrentDurability = 0;
            control.MaxDurability = 0;
        }
    }

    private void PositionGoldSlot(int gridIndex)
    {
        var col = gridIndex % COLUMNS;
        var row = gridIndex / COLUMNS;
        var yOffset = IsExpanded ? -ExpandYOffset : 0;

        GoldSlot.X = GridOffsetX + col * CELL_WIDTH;
        GoldSlot.Y = GridOffsetY + row * CELL_HEIGHT + yOffset;

        // Hide the real slot underneath the gold bag
        for (var i = 0; i < Slots.Length; i++)
            if (i < VisibleSlotCount)
                Slots[i].Visible = i != gridIndex;

        if (gridIndex < Slots.Length)
            Slots[gridIndex].Visible = false;
    }

    protected override Texture2D? RenderIcon(ushort spriteId) => UiRenderer.Instance!.GetItemIcon(spriteId);

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        PositionGoldSlot(VisibleSlotCount - 1);
    }
}