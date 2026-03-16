#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Spell book panel (D key, Shift+D for secondary). 89 slots total. Spell icons rendered from Setoa.dat spell EPFs
///     with gui06 palette. Cooldown: full blue icon for the entire duration.
/// </summary>
public class SpellBookPanel : PanelBaseControl
{
    private const int MAX_SLOTS = 89;
    private readonly Texture2D?[] BlueIconCache = new Texture2D?[MAX_SLOTS];

    private readonly float[] CooldownRemaining = new float[MAX_SLOTS];
    private readonly ushort[] SpriteIds = new ushort[MAX_SLOTS];

    public SpellBookPanel(GraphicsDevice device, ControlPrefabSet hudPrefabSet, bool secondary = false)
        : base(
            device,
            hudPrefabSet,
            MAX_SLOTS,
            secondary)
        => Name = secondary ? "SpellBookAlt" : "SpellBook";

    public override void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if ((index >= 0) && (index < MAX_SLOTS))
        {
            SpriteIds[index] = 0;
            CooldownRemaining[index] = 0;

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
        if ((CooldownRemaining[slotIndex] > 0) && BlueIconCache[slotIndex] is { } blueIcon)
        {
            spriteBatch.Draw(blueIcon, new Vector2(x, y), Color.White);

            return;
        }

        spriteBatch.Draw(icon, new Vector2(x, y), Color.White);
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

        // Lazy-load blue variant
        var spriteId = SpriteIds[index];

        if (spriteId > 0)
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

            if (CooldownRemaining[i] < 0)
                CooldownRemaining[i] = 0;
        }
    }
}