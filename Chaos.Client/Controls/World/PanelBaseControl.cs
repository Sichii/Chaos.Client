#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Base class for icon grid panels (inventory, skill book, spell book). Creates a grid of
///     <see cref="PanelSlotControl" /> children and manages slot assignment, drag-and-drop, and
///     the dragged icon ghost. Subclasses provide icon rendering.
/// </summary>
public abstract class PanelBaseControl : UIPanel
{
    private const int ICON_SIZE = 32;
    private const int CELL_WIDTH = 36;
    private const int CELL_HEIGHT = 33;
    private const int COLUMNS = 12;
    private const int VISIBLE_SLOTS = 36;

    protected readonly GraphicsDevice Device;
    protected readonly int SlotOffset;
    protected readonly PanelSlotControl[] Slots;
    private int DragMouseX;

    // Drag state
    private PanelSlotControl? DragSource;

    // Hover tracking
    private PanelSlotControl? LastHoveredSlot;

    /// <summary>
    ///     True when the user is actively dragging a slot icon.
    /// </summary>
    public bool IsDragging { get; private set; }

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

    /// <summary>
    ///     Current mouse Y during drag.
    /// </summary>
    public int DragY { get; private set; }

    protected PanelBaseControl(
        GraphicsDevice device,
        ControlPrefabSet hudPrefabSet,
        int maxSlots,
        CooldownStyle cooldownStyle = CooldownStyle.None,
        bool secondary = false,
        int gridOffsetX = 8,
        int gridOffsetY = 6)
    {
        Device = device;
        SlotOffset = secondary ? VISIBLE_SLOTS : 0;

        if (hudPrefabSet.Contains("InventoryBackground"))
        {
            var prefab = hudPrefabSet["InventoryBackground"];

            if (prefab.Images.Count > 0)
                Background = TextureConverter.ToTexture2D(device, prefab.Images[0]);
        }

        // Create slot controls for the visible grid cells
        Slots = new PanelSlotControl[VISIBLE_SLOTS];

        for (var i = 0; i < VISIBLE_SLOTS; i++)
        {
            var slotIndex = SlotOffset + i;

            if (slotIndex >= maxSlots)
                break;

            var col = i % COLUMNS;
            var row = i / COLUMNS;

            var slot = new PanelSlotControl
            {
                Name = $"Slot{slotIndex}",
                Slot = (byte)(slotIndex + 1),
                X = gridOffsetX + col * CELL_WIDTH,
                Y = gridOffsetY + row * CELL_HEIGHT,
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                CooldownStyle = cooldownStyle
            };

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
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Finds the PanelSlotControl for a 1-based slot number, or null if out of range or not visible.
    /// </summary>
    protected PanelSlotControl? FindSlot(byte slot)
    {
        var index = slot - 1;

        if ((index < SlotOffset) || (index >= (SlotOffset + VISIBLE_SLOTS)))
            return null;

        var gridIndex = index - SlotOffset;

        return gridIndex < Slots.Length ? Slots[gridIndex] : null;
    }

    private void OnDragStarted(PanelSlotControl source)
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
    public event Action<string?>? OnSlotHovered;

    /// <summary>
    ///     Fired when the user drags a slot icon onto another slot. Parameters are 1-based slot numbers (source, target).
    /// </summary>
    public event Action<byte, byte>? OnSlotSwapped;

    protected abstract Texture2D? RenderIcon(ushort spriteId);

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

        control?.SlotName = name;
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
        PanelSlotControl? hoveredSlot = null;

        foreach (var slot in Slots)
            if (slot.ContainsPoint(input.MouseX, input.MouseY))
            {
                hoveredSlot = slot;

                break;
            }

        if (hoveredSlot != LastHoveredSlot)
        {
            LastHoveredSlot = hoveredSlot;
            OnSlotHovered?.Invoke(hoveredSlot?.NormalTexture is not null ? hoveredSlot.SlotName : null);
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