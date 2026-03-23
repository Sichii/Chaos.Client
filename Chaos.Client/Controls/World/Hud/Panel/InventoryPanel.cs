#region
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Inventory item grid panel (A key). 59 slots, no secondary page. Items rendered from Legend.dat item EPFs with
///     itempal palette lookup. The last visible grid cell displays a permanent gold bag icon. Supports expand toggle
///     (Shift+A) that grows the panel upward from 3 rows to 5 rows, overlaying the game viewport.
/// </summary>
public sealed class InventoryPanel : PanelBase
{
    private const int MAX_SLOTS = 59;
    private const ushort GOLD_SPRITE = 136;
    private const int NORMAL_ROWS = 3;
    private const int EXPANDED_ROWS = 5;
    private const int NORMAL_SLOTS = NORMAL_ROWS * COLUMNS;
    private const int EXPANDED_SLOTS = EXPANDED_ROWS * COLUMNS;

    private readonly Texture2D? ExpandedBackground;
    private readonly int ExpandYOffset;
    private readonly PanelSlot GoldSlot;
    private readonly Texture2D? NormalBackground;

    /// <summary>
    ///     Whether the inventory is currently in expanded (5-row) mode.
    /// </summary>
    public bool IsExpanded { get; private set; }

    public InventoryPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet)
        : base(
            device,
            hudPrefabSet,
            MAX_SLOTS,
            gridOffsetX: 7,
            gridOffsetY: 5)
    {
        Name = "Inventory";

        // Cache both background textures for toggling
        NormalBackground = Background;
        ExpandedBackground = UiRenderer.Instance!.GetSpfTexture("_ninv5.spf");

        // The expanded background is taller — compute the upward offset for the extra rows
        var normalHeight = NormalBackground?.Height ?? Height;
        var expandedHeight = ExpandedBackground?.Height ?? normalHeight;
        ExpandYOffset = expandedHeight - normalHeight;

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

        GoldSlot.SlotName = "Gold( 0 )";
        GoldSlot.OnDragStart += OnDragStarted;
        AddChild(GoldSlot);

        // Position gold at last visible cell and hide the slot underneath
        PositionGoldSlot(NORMAL_SLOTS - 1);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // When expanded, draw the taller background shifted upward so the bottom stays anchored
        if (IsExpanded && ExpandedBackground is not null)
        {
            AtlasHelper.Draw(
                spriteBatch,
                ExpandedBackground,
                new Vector2(ScreenX, ScreenY - ExpandYOffset),
                Color.White);

            // Draw children (slots) — base.Draw would draw NormalBackground, so we skip it
            foreach (var child in Children)
                if (child.Visible)
                    child.Draw(spriteBatch);

            // Slot number overlay
            var slotOverlay = UiRenderer.Instance!.GetSpfTexture("_ninvn.spf");

            if (slotOverlay is not null)
                AtlasHelper.Draw(
                    spriteBatch,
                    slotOverlay,
                    new Vector2(ScreenX - 17, ScreenY - ExpandYOffset + 3),
                    Color.White);

            return;
        }

        base.Draw(spriteBatch);
    }

    protected override PanelSlot? FindHoveredSlot(InputBuffer input)
    {
        if (GoldSlot is { Visible: true } && GoldSlot.ContainsPoint(input.MouseX, input.MouseY))
            return GoldSlot;

        return base.FindHoveredSlot(input);
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

    public void SetSlot(byte slot, ushort sprite, DisplayColor color)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        control.NormalTexture?.Dispose();
        control.NormalTexture = UiRenderer.Instance!.GetItemIcon(sprite, color);
    }

    /// <summary>
    ///     Toggles between normal (3-row) and expanded (5-row) inventory. The extra rows extend upward above the panel,
    ///     overlaying the game viewport. The bottom edge stays anchored.
    /// </summary>
    public void ToggleExpand()
    {
        IsExpanded = !IsExpanded;

        var targetSlots = IsExpanded ? EXPANDED_SLOTS : NORMAL_SLOTS;
        var goldIndex = targetSlots - 1;
        var yShift = IsExpanded ? -ExpandYOffset : ExpandYOffset;

        // Render above other HUD elements when expanded
        ZIndex = IsExpanded ? 10 : 0;

        // Shift all slot Y positions so the grid grows upward from the bottom
        for (var i = 0; i < Slots.Length; i++)
        {
            Slots[i].Y += yShift;
            Slots[i].Visible = (i < targetSlots) && (i != goldIndex);
        }

        // Update visible slot count
        VisibleSlotCount = targetSlots;

        // Reposition gold slot to new last visible cell
        PositionGoldSlot(goldIndex);
    }

    /// <summary>
    ///     Updates the gold bag tooltip text to reflect the current gold amount.
    /// </summary>
    public void UpdateGold(uint gold) => GoldSlot.SlotName = $"Gold( {gold} )";
}