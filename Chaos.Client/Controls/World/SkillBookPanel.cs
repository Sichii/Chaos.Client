#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Skill book panel (S key, Shift+S for secondary). 89 slots total. Skill icons rendered from Setoa.dat skill EPFs
///     with gui06 palette. Cooldown: grey icon with blue progressively covering from bottom to top.
/// </summary>
public class SkillBookPanel : PanelBaseControl
{
    private const int MAX_SLOTS = 89;
    private readonly Texture2D?[] BlueIconCache = new Texture2D?[MAX_SLOTS];
    private readonly float[] CooldownDuration = new float[MAX_SLOTS];

    private readonly float[] CooldownRemaining = new float[MAX_SLOTS];
    private readonly ushort[] SpriteIds = new ushort[MAX_SLOTS];

    public SkillBookPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet, bool secondary = false)
        : base(
            device,
            hudPrefabSet,
            MAX_SLOTS,
            secondary)
        => Name = secondary ? "SkillBookAlt" : "SkillBook";

    public override void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if ((index >= 0) && (index < MAX_SLOTS))
        {
            SpriteIds[index] = 0;
            CooldownRemaining[index] = 0;
            CooldownDuration[index] = 0;

            BlueIconCache[index]
                ?.Dispose();
            BlueIconCache[index] = null;
        }

        base.ClearSlot(slot);
    }

    public override void Dispose()
    {
        foreach (var icon in BlueIconCache)
            icon?.Dispose();

        base.Dispose();
    }

    protected override void DrawSlotIcon(
        SpriteBatch spriteBatch,
        int slotIndex,
        int x,
        int y,
        Texture2D icon)
    {
        // Normal icon always drawn as base
        spriteBatch.Draw(icon, new Vector2(x, y), Color.White);

        if ((CooldownRemaining[slotIndex] <= 0) || (CooldownDuration[slotIndex] <= 0))
            return;

        if (BlueIconCache[slotIndex] is not { } blueIcon)
            return;

        // Blue icon at 80% opacity over the entire normal icon
        spriteBatch.Draw(blueIcon, new Vector2(x, y), Color.White * 0.33f);

        // Full opaque blue icon progressively covering top to bottom as cooldown elapses
        var elapsed = 1f - CooldownRemaining[slotIndex] / CooldownDuration[slotIndex];
        var revealHeight = (int)(blueIcon.Height * elapsed);

        if (revealHeight > 0)
        {
            var srcRect = new Rectangle(
                0,
                0,
                blueIcon.Width,
                revealHeight);

            spriteBatch.Draw(
                blueIcon,
                new Vector2(x, y),
                srcRect,
                Color.White);
        }
    }

    private Texture2D? RenderBlueIcon(ushort spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelIcons.GetSkillBlueIcon(spriteId));

    protected override Texture2D? RenderIcon(ushort spriteId)
        => TextureConverter.RenderSprite(Device, DataContext.PanelIcons.GetSkillIcon(spriteId));

    public void SetCooldown(byte slot, uint durationSecs)
    {
        var index = slot - 1;

        if ((index < 0) || (index >= MAX_SLOTS))
            return;

        var duration = durationSecs * 1000f;
        CooldownRemaining[index] = duration;
        CooldownDuration[index] = duration;

        // Lazy-load blue/grey variants
        var spriteId = SpriteIds[index];

        if (spriteId == 0)
            return;

        BlueIconCache[index] ??= RenderBlueIcon(spriteId);
    }

    public override void SetSlot(byte slot, ushort sprite)
    {
        var index = slot - 1;

        if ((index >= 0) && (index < MAX_SLOTS))
        {
            SpriteIds[index] = sprite;

            BlueIconCache[index]
                ?.Dispose();
            BlueIconCache[index] = null;
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
        }
    }
}