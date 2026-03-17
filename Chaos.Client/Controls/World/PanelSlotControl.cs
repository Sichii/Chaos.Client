#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     A single slot in an icon grid panel (inventory, skill book, spell book). Extends UIButton with cooldown overlay
///     rendering, double-click detection, and drag-and-drop support. The parent panel creates one PanelSlotControl per
///     visible grid cell and manages layout, slot assignment, and drag state.
/// </summary>
public sealed class PanelSlotControl : UIButton
{
    private const float DOUBLE_CLICK_MS = 300f;
    private bool DoubleClickFired;

    // Double-click tracking
    private float LastClickTime;

    /// <summary>
    ///     Cooldown progress from 0 (fully cooled down) to 1 (just started, fully on cooldown).
    /// </summary>
    public float CooldownPercent { get; set; }

    /// <summary>
    ///     How the cooldown overlay is rendered.
    /// </summary>
    public CooldownStyle CooldownStyle { get; set; }

    /// <summary>
    ///     Alternate texture shown during cooldown. For skills this is the blue variant; for spells the blue swap icon.
    /// </summary>
    public Texture2D? CooldownTexture { get; set; }

    /// <summary>
    ///     The 1-based slot number this control represents.
    /// </summary>
    public byte Slot { get; init; }

    /// <summary>
    ///     Display name of the item/skill/spell in this slot. Used for hover tooltips by the parent.
    /// </summary>
    public string? SlotName { get; set; }

    public override void Dispose()
    {
        CooldownTexture?.Dispose();
        CooldownTexture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        // Icon rendering with cooldown overlay
        var icon = NormalTexture;

        if (icon is null)
            return;

        var pos = new Vector2(ScreenX, ScreenY);

        if ((CooldownPercent > 0) && CooldownTexture is not null)
            switch (CooldownStyle)
            {
                case CooldownStyle.Swap:
                    spriteBatch.Draw(CooldownTexture, pos, Color.White);

                    break;

                case CooldownStyle.Progressive:
                    // Base normal icon
                    spriteBatch.Draw(icon, pos, Color.White);

                    // Blue overlay at 33% opacity
                    spriteBatch.Draw(CooldownTexture, pos, Color.White * 0.33f);

                    // Blue progressively revealed top-to-bottom as cooldown elapses
                    var elapsed = 1f - CooldownPercent;
                    var revealHeight = (int)(CooldownTexture.Height * elapsed);

                    if (revealHeight > 0)
                    {
                        var srcRect = new Rectangle(
                            0,
                            0,
                            CooldownTexture.Width,
                            revealHeight);

                        spriteBatch.Draw(
                            CooldownTexture,
                            pos,
                            srcRect,
                            Color.White);
                    }

                    break;

                default:
                    spriteBatch.Draw(icon, pos, Color.White);

                    break;
            }
        else
            spriteBatch.Draw(icon, pos, Color.White);
    }

    /// <summary>
    ///     Fired on double-click. Parameter is the 1-based slot number.
    /// </summary>
    public event Action<byte>? OnDoubleClick;

    /// <summary>
    ///     Fired when a drag begins on this slot. Parameter is the 1-based slot number.
    /// </summary>
    public event Action<PanelSlotControl>? OnDragStart;

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        var hovering = ContainsPoint(input.MouseX, input.MouseY);

        // Double-click detection — must happen before base.Update consumes the click
        if (input.WasLeftButtonPressed && hovering && NormalTexture is not null)
        {
            var now = (float)gameTime.TotalGameTime.TotalMilliseconds;

            if ((now - LastClickTime) < DOUBLE_CLICK_MS)
            {
                OnDoubleClick?.Invoke(Slot);
                DoubleClickFired = true;
                LastClickTime = 0;
            } else
            {
                LastClickTime = now;
                DoubleClickFired = false;
            }
        }

        // Drag detection — mouse held and moved away from origin
        if (input.IsLeftButtonHeld && hovering && !DoubleClickFired && NormalTexture is not null)

            // Drag starts when the mouse moves while pressed on this slot
            if (input.WasLeftButtonPressed)
                OnDragStart?.Invoke(this);

        base.Update(gameTime, input);
    }
}