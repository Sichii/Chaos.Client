#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Skill book panel (S key, Shift+S for secondary). Thin view that subscribes to
///     <see cref="ViewModel.SkillBook" /> change events and renders skill icons with Progressive-style cooldowns.
/// </summary>
public sealed class SkillBookPanel : PanelBase
{
    private const int MAX_SLOTS = 89;

    public SkillBookPanel(
        ControlPrefabSet hudPrefabSet,
        bool secondary = false,
        Texture2D? background = null,
        int normalVisibleSlots = DEFAULT_VISIBLE_SLOTS)
        : base(
            hudPrefabSet,
            MAX_SLOTS,
            CooldownStyle.Progressive,
            secondary,
            background: background,
            normalVisibleSlots: normalVisibleSlots)
    {
        Name = secondary ? "SkillBookAlt" : "SkillBook";
        WorldState.SkillBook.SlotChanged += OnSlotChanged;
        WorldState.SkillBook.Cleared += OnCleared;
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
        WorldState.SkillBook.SlotChanged -= OnSlotChanged;
        WorldState.SkillBook.Cleared -= OnCleared;

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
    ///     Returns the SkillSlot for a 1-based slot number, or null.
    /// </summary>
    public SkillSlot? GetSkillSlot(byte slot) => FindSlot(slot) as SkillSlot;

    private void OnCleared()
    {
        foreach (var slot in Slots)
        {
            slot.NormalTexture?.Dispose();
            slot.NormalTexture = null;
            slot.CooldownTexture?.Dispose();
            slot.CooldownTexture = null;
            slot.GreyTexture?.Dispose();
            slot.GreyTexture = null;
            slot.CooldownPercent = 0;
            slot.SlotName = null;
        }
    }

    private void OnSlotChanged(byte slot)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        var data = WorldState.SkillBook.GetSlot(slot);

        // Dispose old cooldown textures — sprite may have changed
        control.CooldownTexture?.Dispose();
        control.CooldownTexture = null;
        control.GreyTexture?.Dispose();
        control.GreyTexture = null;

        if (data.IsOccupied)
        {
            control.NormalTexture?.Dispose();
            control.NormalTexture = RenderIcon(data.Sprite);

            if (control is SkillSlot skillSlot)
                skillSlot.Chant = data.Chant ?? string.Empty;

            SetSlotName(slot, data.Name);
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

    private Texture2D? RenderGreyIcon(ushort spriteId) => UiRenderer.Instance!.GetSkillGreyIcon(spriteId);

    protected override Texture2D? RenderIcon(ushort spriteId) => UiRenderer.Instance!.GetSkillIcon(spriteId);

    private Texture2D RenderTintedIcon(ushort spriteId)
    {
        var cache = UiRenderer.Instance!;

        return cache.GetTintedTexture($"skill:{spriteId}", cache.GetSkillIcon(spriteId));
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        base.Update(gameTime, input);

        // Read cooldown state each frame — progressive style: grey base with blue overlay
        for (var i = 0; (i < VisibleSlotCount) && (i < Slots.Length); i++)
        {
            var slot = (byte)(i + SlotOffset + 1);
            var control = Slots[i];
            var cooldownPercent = WorldState.SkillBook.GetCooldownPercent(slot);

            if ((cooldownPercent > 0) && control.NormalTexture is not null)
            {
                var data = WorldState.SkillBook.GetSlot(slot);

                if (data.IsOccupied)
                {
                    control.GreyTexture ??= RenderGreyIcon(data.Sprite);
                    control.CooldownTexture ??= RenderTintedIcon(data.Sprite);
                }
            }

            control.CooldownPercent = cooldownPercent;
        }
    }
}