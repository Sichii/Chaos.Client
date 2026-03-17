#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Spell book panel (D key, Shift+D for secondary). 89 slots total. Spell icons rendered from Setoa.dat spell EPFs
///     with gui06 palette. Cooldown: full blue icon for the entire duration.
/// </summary>
public sealed class SpellBookPanel : PanelBaseControl
{
    private const int MAX_SLOTS = 89;

    private readonly float[] CooldownRemaining = new float[MAX_SLOTS];
    private readonly ushort[] SpriteIds = new ushort[MAX_SLOTS];

    public SpellBookPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet, bool secondary = false)
        : base(
            device,
            hudPrefabSet,
            MAX_SLOTS,
            CooldownStyle.Swap,
            secondary)
        => Name = secondary ? "SpellBookAlt" : "SpellBook";

    public override void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if (index is >= 0 and < MAX_SLOTS)
        {
            SpriteIds[index] = 0;
            CooldownRemaining[index] = 0;
        }

        var control = FindSlot(slot);

        if (control is not null)
        {
            control.CooldownTexture?.Dispose();
            control.CooldownTexture = null;
            control.CooldownPercent = 0;
        }

        base.ClearSlot(slot);
    }

    public override void Dispose()
    {
        foreach (var slot in Slots)
        {
            slot.CooldownTexture?.Dispose();
            slot.CooldownTexture = null;
        }

        base.Dispose();
    }

    private Texture2D? RenderBlueIcon(ushort spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelIcons.GetSpellBlueIcon(spriteId));

    protected override Texture2D? RenderIcon(ushort spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelIcons.GetSpellIcon(spriteId));

    public void SetCooldown(byte slot, uint durationSecs)
    {
        var index = slot - 1;

        if ((index < 0) || (index >= MAX_SLOTS))
            return;

        CooldownRemaining[index] = durationSecs * 1000f;

        var control = FindSlot(slot);

        if (control is null)
            return;

        // Lazy-load blue variant
        var spriteId = SpriteIds[index];

        if (spriteId > 0)
            control.CooldownTexture ??= RenderBlueIcon(spriteId);

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

            if (CooldownRemaining[i] < 0)
                CooldownRemaining[i] = 0;

            var control = FindSlot((byte)(i + 1));

            control?.CooldownPercent = CooldownRemaining[i] > 0 ? 1f : 0;
        }
    }
}