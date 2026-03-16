#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Base class for icon grid panels (inventory, skill book, spell book). Handles background loading, grid layout, icon
///     caching, click detection, and draw. Subclasses provide the icon rendering method.
/// </summary>
public abstract class PanelBaseControl : UIPanel
{
    // Double-click detection
    private const float DOUBLE_CLICK_MS = 300f;

    protected readonly GraphicsDevice Device;
    protected readonly Texture2D?[] IconCache;
    protected readonly int MaxSlots;
    protected readonly string?[] SlotNames;
    protected readonly int SlotOffset;
    private int DragMouseX;
    private int DragMouseY;

    // Drag state
    private int DragSourceSlotIndex = -1;
    private int HoveredSlotIndex = -1;
    private int LastClickedSlotIndex = -1;
    private float LastClickTime;
    private CachedText? TooltipText;
    protected virtual int CellHeight => 33;
    protected virtual int CellWidth => 36;
    protected virtual int Columns => 12;
    protected virtual int GridOffsetX => 8;
    protected virtual int GridOffsetY => 6;
    protected virtual int VisibleSlots => 36;

    protected PanelBaseControl(
        GraphicsDevice device,
        ControlPrefabSet hudPrefabSet,
        int maxSlots,
        bool secondary = false)
    {
        Device = device;
        MaxSlots = maxSlots;
        IconCache = new Texture2D?[maxSlots];
        SlotNames = new string?[maxSlots];

        if (!hudPrefabSet.Contains("InventoryBackground"))
            return;

        var prefab = hudPrefabSet["InventoryBackground"];

        if (prefab.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, prefab.Images[0]);

        SlotOffset = secondary ? 36 : 0;
    }

    public virtual void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if ((index < 0) || (index >= MaxSlots))
            return;

        IconCache[index]
            ?.Dispose();
        IconCache[index] = null;
        SlotNames[index] = null;
    }

    public override void Dispose()
    {
        foreach (var icon in IconCache)
            icon?.Dispose();

        Array.Clear(IconCache);
        TooltipText?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if (Columns == 0)
            return;

        var sx = ScreenX;
        var sy = ScreenY;

        for (var i = 0; i < VisibleSlots; i++)
        {
            var slotIndex = SlotOffset + i;

            if (slotIndex >= MaxSlots)
                break;

            if (IconCache[slotIndex] is not { } icon)
                continue;

            var col = i % Columns;
            var row = i / Columns;
            var x = sx + GridOffsetX + col * CellWidth;
            var y = sy + GridOffsetY + row * CellHeight;

            DrawSlotIcon(
                spriteBatch,
                slotIndex,
                x,
                y,
                icon);
        }

        // Dragged icon follows mouse (semi-transparent)
        if ((DragSourceSlotIndex >= 0) && IconCache[DragSourceSlotIndex] is { } dragIcon)
            spriteBatch.Draw(dragIcon, new Vector2(DragMouseX - dragIcon.Width / 2, DragMouseY - dragIcon.Height / 2), Color.White * 0.7f);

        // Tooltip for hovered slot (not while dragging)
        if ((DragSourceSlotIndex < 0) && (HoveredSlotIndex >= 0) && SlotNames[HoveredSlotIndex] is { Length: > 0 } name)
        {
            TooltipText ??= new CachedText(Device);
            TooltipText.Update(name, 0, Color.White);

            if (TooltipText.Texture is not null)
            {
                var tipX = sx + GridOffsetX;
                var tipY = sy - TooltipText.Texture.Height - 2;
                spriteBatch.Draw(TooltipText.Texture, new Vector2(tipX, tipY), Color.White);
            }
        }
    }

    /// <summary>
    ///     Draws a single slot's icon. Override in subclasses for custom rendering (e.g. cooldown overlays).
    /// </summary>
    protected virtual void DrawSlotIcon(
        SpriteBatch spriteBatch,
        int slotIndex,
        int x,
        int y,
        Texture2D icon)
        => spriteBatch.Draw(icon, new Vector2(x, y), Color.White);

    private int HitTestSlot(int mouseX, int mouseY)
    {
        var gridX = mouseX - ScreenX - GridOffsetX;
        var gridY = mouseY - ScreenY - GridOffsetY;

        if ((gridX < 0) || (gridY < 0))
            return -1;

        var col = gridX / CellWidth;
        var row = gridY / CellHeight;

        if (col >= Columns)
            return -1;

        var gridIndex = row * Columns + col;

        if ((gridIndex < 0) || (gridIndex >= VisibleSlots))
            return -1;

        var slotIndex = SlotOffset + gridIndex;

        if (slotIndex >= MaxSlots)
            return -1;

        return slotIndex;
    }

    /// <summary>
    ///     Fired when the user clicks an occupied slot. Parameter is the 1-based slot number.
    /// </summary>
    public event Action<byte>? OnSlotClicked;

    /// <summary>
    ///     Fired when the user drags a slot icon and releases outside the panel. Parameter is the 1-based slot number.
    /// </summary>
    public event Action<byte>? OnSlotDroppedOutside;

    /// <summary>
    ///     Fired when the user drags a slot icon onto another slot. Parameters are 1-based slot numbers (source, target).
    /// </summary>
    public event Action<byte, byte>? OnSlotSwapped;

    protected abstract Texture2D? RenderIcon(ushort spriteId);

    public virtual void SetSlot(byte slot, ushort sprite)
    {
        var index = slot - 1;

        if ((index < 0) || (index >= MaxSlots))
            return;

        IconCache[index]
            ?.Dispose();
        IconCache[index] = RenderIcon(sprite);
    }

    public void SetSlotName(byte slot, string? name)
    {
        var index = slot - 1;

        if ((index >= 0) && (index < MaxSlots))
            SlotNames[index] = name;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        base.Update(gameTime, input);

        if (!Visible || !Enabled || (Columns == 0))
        {
            HoveredSlotIndex = -1;
            DragSourceSlotIndex = -1;

            return;
        }

        var mouseX = input.MouseX;
        var mouseY = input.MouseY;

        // Hit test — find which slot the mouse is over
        var hitSlotIndex = HitTestSlot(mouseX, mouseY);
        HoveredSlotIndex = hitSlotIndex;

        // Drag tracking
        if (DragSourceSlotIndex >= 0)
        {
            DragMouseX = mouseX;
            DragMouseY = mouseY;

            if (!input.IsLeftButtonHeld)
            {
                var sourceSlot = (byte)(DragSourceSlotIndex + 1);

                if ((hitSlotIndex >= 0) && (hitSlotIndex != DragSourceSlotIndex))
                    OnSlotSwapped?.Invoke(sourceSlot, (byte)(hitSlotIndex + 1));
                else if (hitSlotIndex < 0)
                    OnSlotDroppedOutside?.Invoke(sourceSlot);

                DragSourceSlotIndex = -1;
            }

            return;
        }

        // Click/drag start + double-click detection
        if (input.WasLeftButtonPressed && (hitSlotIndex >= 0))
        {
            DragSourceSlotIndex = hitSlotIndex;
            DragMouseX = mouseX;
            DragMouseY = mouseY;

            var now = (float)gameTime.TotalGameTime.TotalMilliseconds;

            if ((hitSlotIndex == LastClickedSlotIndex) && ((now - LastClickTime) < DOUBLE_CLICK_MS))
            {
                OnSlotClicked?.Invoke((byte)(hitSlotIndex + 1));
                LastClickedSlotIndex = -1;
            } else
            {
                LastClickedSlotIndex = hitSlotIndex;
                LastClickTime = now;
            }
        }
    }
}