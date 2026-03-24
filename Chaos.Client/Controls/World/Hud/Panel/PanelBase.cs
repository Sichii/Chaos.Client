#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Base class for icon grid panels (inventory, skill book, spell book). Creates a grid of
///     <see cref="PanelSlot" /> children and manages slot assignment, drag-and-drop, and
///     the dragged icon ghost. Subclasses provide icon rendering.
/// </summary>
public abstract class PanelBase : ExpandablePanel
{
    protected const int ICON_SIZE = 32;
    protected const int CELL_WIDTH = 36;
    protected const int CELL_HEIGHT = 33;
    protected const int COLUMNS = 12;
    protected const int DEFAULT_VISIBLE_SLOTS = 36;

    private static Texture2D? SlotNumberOverlay;

    // Compact padding (large HUD 1-row backgrounds need extra Y before the grid)
    private readonly int CompactGridPadding;

    protected readonly int GridOffsetX;
    protected readonly int GridOffsetY;
    protected readonly int MaxSlots;
    protected readonly int NormalVisibleSlots;
    protected readonly int SlotOffset;
    protected readonly PanelSlot[] Slots;
    private int DragMouseX;

    // Drag state
    private PanelSlot? DragSource;

    // Expand state (slot-specific)
    private int ExpandedVisibleSlots;

    // Hover tracking
    private PanelSlot? LastHoveredSlot;

    /// <summary>
    ///     Current mouse Y during drag.
    /// </summary>
    public int DragY { get; private set; }

    /// <summary>
    ///     True when the user is actively dragging a slot icon.
    /// </summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    ///     The number of currently visible grid slots.
    /// </summary>
    public int VisibleSlotCount { get; protected set; } = DEFAULT_VISIBLE_SLOTS;

    /// <summary>
    ///     The 1-based slot number being dragged, or 0 if not dragging.
    /// </summary>
    public byte DragSlot => DragSource?.Slot ?? 0;

    /// <summary>
    ///     The texture of the currently dragged icon, or null.
    /// </summary>
    public Texture2D? DragTexture => IsDragging ? DragSource?.NormalTexture : null;

    /// <summary>
    ///     Current mouse X during drag.
    /// </summary>
    public int DragX => DragMouseX;

    protected PanelBase(
        ControlPrefabSet hudPrefabSet,
        int maxSlots,
        CooldownStyle cooldownStyle = CooldownStyle.None,
        bool secondary = false,
        int gridOffsetX = 8,
        int gridOffsetY = 6,
        Texture2D? background = null,
        int normalVisibleSlots = DEFAULT_VISIBLE_SLOTS)
    {
        MaxSlots = maxSlots;
        GridOffsetX = gridOffsetX;
        NormalVisibleSlots = normalVisibleSlots;
        VisibleSlotCount = normalVisibleSlots;
        SlotOffset = secondary ? normalVisibleSlots : 0;

        // Large HUD compact backgrounds (1-row) have extra top padding before the grid
        CompactGridPadding = normalVisibleSlots < DEFAULT_VISIBLE_SLOTS ? 4 : 0;
        GridOffsetY = gridOffsetY + CompactGridPadding;

        if (background is not null)
            Background = background;
        else if (hudPrefabSet.Contains("InventoryBackground"))
        {
            var prefab = hudPrefabSet["InventoryBackground"];

            if (prefab.Images.Count > 0)
                Background = UiRenderer.Instance!.GetPrefabTexture(hudPrefabSet.Name, "InventoryBackground", 0);
        }

        SlotNumberOverlay ??= UiRenderer.Instance!.GetSpfTexture("_ninvn.spf");

        // Create slot controls for all possible grid cells (visibility controlled per slot)
        var totalSlots = Math.Min(maxSlots - SlotOffset, maxSlots);
        Slots = new PanelSlot[totalSlots];

        for (var i = 0; i < totalSlots; i++)
        {
            var slotIndex = SlotOffset + i;

            if (slotIndex >= maxSlots)
                break;

            var col = i % COLUMNS;
            var row = i / COLUMNS;

            // ReSharper disable once VirtualMemberCallInConstructor
            var slot = CreateSlot((byte)(slotIndex + 1), $"Slot{slotIndex}", cooldownStyle);
            slot.X = GridOffsetX + col * CELL_WIDTH;
            slot.Y = GridOffsetY + row * CELL_HEIGHT;
            slot.Width = ICON_SIZE;
            slot.Height = ICON_SIZE;
            slot.Visible = i < normalVisibleSlots;

            slot.OnDoubleClick += s => OnSlotClicked?.Invoke(s);
            slot.OnDragStart += OnDragStarted;

            Slots[i] = slot;
            AddChild(slot);
        }
    }

    public virtual void ClearSlot(byte slot)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        control.NormalTexture?.Dispose();
        control.NormalTexture = null;
        control.SlotName = null;
        control.CooldownPercent = 0;
        control.CurrentDurability = 0;
        control.MaxDurability = 0;
    }

    /// <summary>
    ///     Configures expand support for this panel with a specific expanded slot count.
    /// </summary>
    public void ConfigureExpand(Texture2D? expandedBackground, int expandedVisibleSlots)
    {
        ExpandedVisibleSlots = expandedVisibleSlots;

        ConfigureExpand(expandedBackground);
    }

    protected virtual PanelSlot CreateSlot(byte slotNumber, string name, CooldownStyle cooldownStyle)
        => new()
        {
            Name = name,
            Slot = slotNumber,
            CooldownStyle = cooldownStyle
        };

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // ExpandablePanel.Draw handles expanded background + children, or normal background + children
        base.Draw(spriteBatch);

        // Slot number overlay — positioned at expanded or normal Y
        if (SlotNumberOverlay is not null)
        {
            var overlayY = IsExpanded ? ScreenY - ExpandYOffset + 3 : ScreenY + 3 + CompactGridPadding;

            AtlasHelper.Draw(
                spriteBatch,
                SlotNumberOverlay,
                new Vector2(ScreenX - 17, overlayY),
                Color.White);
        }
    }

    protected virtual PanelSlot? FindHoveredSlot(InputBuffer input)
    {
        for (var i = 0; (i < VisibleSlotCount) && (i < Slots.Length); i++)
            if (Slots[i]
                .ContainsPoint(input.MouseX, input.MouseY))
                return Slots[i];

        return null;
    }

    /// <summary>
    ///     Finds the PanelSlotControl for a 1-based slot number, or null if out of range or not visible.
    /// </summary>
    protected PanelSlot? FindSlot(byte slot)
    {
        var index = slot - 1;

        if ((index < SlotOffset) || (index >= (SlotOffset + VisibleSlotCount)))
            return null;

        var gridIndex = index - SlotOffset;

        return gridIndex < Slots.Length ? Slots[gridIndex] : null;
    }

    /// <summary>
    ///     Forces a hover exit event and clears the tracked hover state. Call when the panel is hidden while a slot is still
    ///     hovered.
    /// </summary>
    public void ForceHoverExit()
    {
        if (LastHoveredSlot is null)
            return;

        LastHoveredSlot = null;
        OnSlotHoverExit?.Invoke();
    }

    /// <summary>
    ///     Returns the 1-based slot number at the given screen coordinates, or null if no slot is at that position.
    /// </summary>
    public byte? GetSlotAtPosition(int mouseX, int mouseY)
    {
        for (var i = 0; (i < VisibleSlotCount) && (i < Slots.Length); i++)
            if (Slots[i]
                    .ContainsPoint(mouseX, mouseY)
                && Slots[i].NormalTexture is not null)
                return Slots[i].Slot;

        return null;
    }

    /// <summary>
    ///     Returns the PanelSlotControl for a 1-based slot number, or null if out of range or not visible.
    /// </summary>
    public PanelSlot? GetSlotControl(byte slot) => FindSlot(slot);

    protected void OnDragStarted(PanelSlot source)
    {
        DragSource = source;
        IsDragging = true;
    }

    /// <summary>
    ///     Fired when the user double-clicks an occupied slot. Parameter is the 1-based slot number.
    /// </summary>
    public event Action<byte>? OnSlotClicked;

    /// <summary>
    ///     Fired when the user drags a slot icon and releases outside the panel.
    ///     Parameters: (slot, mouseX, mouseY).
    /// </summary>
    public event Action<byte, int, int>? OnSlotDroppedOutside;

    /// <summary>
    ///     Fired when the hovered slot changes. Parameter is the slot name (or null when unhovered).
    /// </summary>
    public event Action<PanelSlot>? OnSlotHoverEnter;

    public event Action? OnSlotHoverExit;

    /// <summary>
    ///     Fired when the user drags a slot icon onto another slot. Parameters are 1-based slot numbers (source, target).
    /// </summary>
    public event Action<byte, byte>? OnSlotSwapped;

    protected abstract Texture2D? RenderIcon(ushort spriteId);

    /// <summary>
    ///     Sets the expand state. Adjusts slot visibility and Y positions for the expanded grid.
    /// </summary>
    public override void SetExpanded(bool expanded)
    {
        if ((expanded == IsExpanded) || (ExpandedVisibleSlots == 0))
            return;

        var yShift = expanded ? -ExpandYOffset - CompactGridPadding : ExpandYOffset + CompactGridPadding;

        base.SetExpanded(expanded);

        var targetSlots = expanded ? ExpandedVisibleSlots : NormalVisibleSlots;

        for (var i = 0; i < Slots.Length; i++)
        {
            Slots[i].Y += yShift;
            Slots[i].Visible = i < targetSlots;
        }

        VisibleSlotCount = targetSlots;
    }

    public virtual void SetSlot(byte slot, ushort sprite)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        control.NormalTexture?.Dispose();
        control.NormalTexture = RenderIcon(sprite);
    }

    public void SetSlotName(byte slot, string? name)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        if (control is AbilitySlotControl ability)
            ability.SetAbilityName(name);
        else
            control.SlotName = name;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        base.Update(gameTime, input);

        if (!Visible || !Enabled)
        {
            IsDragging = false;
            DragSource = null;

            return;
        }

        // Hover event — find which slot the mouse is over
        var hoveredSlot = FindHoveredSlot(input);

        if (hoveredSlot != LastHoveredSlot)
        {
            LastHoveredSlot = hoveredSlot;

            if (hoveredSlot?.NormalTexture is not null)
                OnSlotHoverEnter?.Invoke(hoveredSlot);
            else
                OnSlotHoverExit?.Invoke();
        }

        // Drag tracking
        if (IsDragging)
        {
            DragMouseX = input.MouseX;
            DragY = input.MouseY;

            if (!input.IsLeftButtonHeld)
            {
                if (DragSource is not null)
                {
                    if (hoveredSlot is not null && (hoveredSlot != DragSource))
                        OnSlotSwapped?.Invoke(DragSource.Slot, hoveredSlot.Slot);
                    else if (hoveredSlot is null)
                        OnSlotDroppedOutside?.Invoke(DragSource.Slot, input.MouseX, input.MouseY);
                }

                IsDragging = false;
                DragSource = null;
            }
        }
    }
}