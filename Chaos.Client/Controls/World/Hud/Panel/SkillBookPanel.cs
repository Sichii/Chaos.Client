#region
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Data.Models;
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Skill book panel (S key, Shift+S for secondary). 89 slots total. Skill icons rendered from Setoa.dat skill EPFs
///     with gui06 palette. Cooldown: grey icon with blue progressively covering from bottom to top.
/// </summary>
public sealed class SkillBookPanel : PanelBase
{
    private const int MAX_SLOTS = 89;

    private readonly float[] CooldownDuration = new float[MAX_SLOTS];
    private readonly float[] CooldownRemaining = new float[MAX_SLOTS];
    private readonly ushort[] SpriteIds = new ushort[MAX_SLOTS];

    public SkillBookPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet, bool secondary = false)
        : base(
            device,
            hudPrefabSet,
            MAX_SLOTS,
            CooldownStyle.Progressive,
            secondary)
        => Name = secondary ? "SkillBookAlt" : "SkillBook";

    public override void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if (index is >= 0 and < MAX_SLOTS)
        {
            SpriteIds[index] = 0;
            CooldownRemaining[index] = 0;
            CooldownDuration[index] = 0;
        }

        var control = FindSlot(slot);

        if (control is not null)
        {
            control.CooldownTexture?.Dispose();
            control.CooldownTexture = null;
            control.GreyTexture?.Dispose();
            control.GreyTexture = null;
            control.CooldownPercent = 0;
        }

        base.ClearSlot(slot);
    }

    protected override PanelSlot CreateSlot(byte slotNumber, string name, CooldownStyle cooldownStyle)
        => new SkillSlot
        {
            Name = name,
            Slot = slotNumber,
            CooldownStyle = cooldownStyle
        };

    public override void Dispose()
    {
        foreach (var slot in Slots)
        {
            slot.CooldownTexture?.Dispose();
            slot.CooldownTexture = null;
            slot.GreyTexture?.Dispose();
            slot.GreyTexture = null;
        }

        base.Dispose();
    }

    /// <summary>
    ///     Returns the SkillSlotControl for a 1-based slot number, or null.
    /// </summary>
    public SkillSlot? GetSkillSlot(byte slot) => FindSlot(slot) as SkillSlot;

    private Texture2D? RenderGreyIcon(ushort spriteId) => UiRenderer.Instance!.GetSkillGreyIcon(spriteId);

    protected override Texture2D? RenderIcon(ushort spriteId) => UiRenderer.Instance!.GetSkillIcon(spriteId);

    private Texture2D RenderTintedIcon(ushort spriteId)
    {
        var cache = UiRenderer.Instance!;

        return cache.GetTintedTexture($"skill:{spriteId}", cache.GetSkillIcon(spriteId));
    }

    public void SetCooldown(byte slot, uint durationSecs)
    {
        var index = slot - 1;

        if ((index < 0) || (index >= MAX_SLOTS))
            return;

        var duration = durationSecs * 1000f;
        CooldownRemaining[index] = duration;
        CooldownDuration[index] = duration;

        var control = FindSlot(slot);

        if (control is null)
            return;

        if (durationSecs == 0)
        {
            control.CooldownPercent = 0;

            return;
        }

        // Lazy-load grey base and tinted overlay
        var spriteId = SpriteIds[index];

        if (spriteId > 0)
        {
            control.GreyTexture ??= RenderGreyIcon(spriteId);
            control.CooldownTexture ??= RenderTintedIcon(spriteId);
        }

        control.CooldownPercent = 1f;
    }

    public override void SetSlot(byte slot, ushort sprite)
    {
        var index = slot - 1;

        if (index is >= 0 and < MAX_SLOTS)
        {
            SpriteIds[index] = sprite;

            var control = FindSlot(slot);

            if (control is not null)
            {
                control.CooldownTexture?.Dispose();
                control.CooldownTexture = null;
                control.GreyTexture?.Dispose();
                control.GreyTexture = null;
            }
        }

        base.SetSlot(slot, sprite);
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        base.Update(gameTime, input);

        var elapsedMs = (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        for (var i = 0; i < MAX_SLOTS; i++)
        {
            if (CooldownRemaining[i] <= 0)
                continue;

            CooldownRemaining[i] -= elapsedMs;

            if (CooldownRemaining[i] <= 0)
            {
                CooldownRemaining[i] = 0;
                CooldownDuration[i] = 0;
            }

            // Update the slot control's cooldown percent
            var control = FindSlot((byte)(i + 1));

            control?.CooldownPercent = CooldownDuration[i] > 0 ? CooldownRemaining[i] / CooldownDuration[i] : 0;
        }
    }
}