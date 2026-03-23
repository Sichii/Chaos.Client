#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.SelfProfile;

/// <summary>
///     Equipment tab page within the status book, loaded from _nui_eq prefab. Displays 18 equipment slots as a paper doll
///     layout with item icons. Each slot has a fixed position from the prefab and maps to an <see cref="EquipmentSlot" />.
///     Empty slots show a placeholder icon from _nui_eqi; occupied slots show the item's panel icon.
/// </summary>
public sealed class SelfProfileEquipmentTab : PrefabPanel
{
    /// <summary>
    ///     Maps control names from the _nui_eq prefab to their corresponding <see cref="EquipmentSlot" /> values. The control
    ///     names match those defined in the _nui_eq.txt control file: WEAPON=1, ARMOR=2, SHIELD=3, HEAD=Helmet(4),
    ///     EAR=Earrings(5), NECK=Necklace(6), LHAND=LeftRing(7), RHAND=RightRing(8), LARM=LeftGaunt(9), RARM=RightGaunt(10),
    ///     BELT=11, LEG=Greaves(12), FOOT=Boots(13), CAPE=Accessory1(14), ARMOR2=Overcoat(15), HEAD2=OverHelm(16),
    ///     CAPE2=Accessory2(17), CAPE3=Accessory3(18).
    /// </summary>
    private static readonly (string ControlName, EquipmentSlot Slot)[] SlotMappings =
    [
        ("WEAPON", EquipmentSlot.Weapon),
        ("ARMOR", EquipmentSlot.Armor),
        ("SHIELD", EquipmentSlot.Shield),
        ("HEAD", EquipmentSlot.Helmet),
        ("EAR", EquipmentSlot.Earrings),
        ("NECK", EquipmentSlot.Necklace),
        ("LHAND", EquipmentSlot.LeftRing),
        ("RHAND", EquipmentSlot.RightRing),
        ("LARM", EquipmentSlot.LeftGaunt),
        ("RARM", EquipmentSlot.RightGaunt),
        ("BELT", EquipmentSlot.Belt),
        ("LEG", EquipmentSlot.Greaves),
        ("FOOT", EquipmentSlot.Boots),
        ("CAPE", EquipmentSlot.Accessory1),
        ("ARMOR2", EquipmentSlot.Overcoat),
        ("HEAD2", EquipmentSlot.OverHelm),
        ("CAPE2", EquipmentSlot.Accessory2),
        ("CAPE3", EquipmentSlot.Accessory3)
    ];

    // Emoticon status icon frame index → _nemots.spf frame
    private const int EMOTICON_FRAME_COUNT = 8;

    // Idle frame for south-facing direction (walk anim frames 5-9, idle = 5)
    private const int PAPERDOLL_IDLE_FRAME = 5;
    private const float DOUBLE_CLICK_MS = 400;

    private readonly UILabel? AcLabel;
    private readonly UILabel? ClanLabel;
    private readonly UILabel? ClanTitleLabel;
    private readonly UILabel? ClassLabel;
    private readonly UILabel? ConLabel;

    private readonly UILabel? DexLabel;

    // Emoticon status
    private readonly Texture2D?[] EmoticonIcons;

    private readonly UILabel? EmoticonLabel;
    private readonly UIButton? GroupBtn;
    private readonly Texture2D? GroupClosedTexture;
    private readonly Texture2D? GroupOpenTexture;
    private readonly Rectangle HumanIconRect;
    private readonly UILabel? IntLabel;

    // Player info labels
    private readonly UILabel? NameLabel;

    // Nation icon
    private readonly Rectangle NationRect;

    // Paperdoll
    private readonly Rectangle PaperdollRect;

    // Equipment slot rendering: maps EquipmentSlot to its visual state
    private readonly Dictionary<EquipmentSlot, EquipmentSlotVisual> SlotVisuals = new();

    // Stat labels from the _nui_eq prefab (N_ prefix)
    private readonly UILabel? StrLabel;
    private readonly UILabel? TitleLabel;

    // Tooltip for hovered equipment slot
    private readonly UILabel TooltipLabel;
    private readonly UILabel? WisLabel;
    private byte EmoticonState;
    private EquipmentSlot? HoveredSlot;
    private EquipmentSlot? LastClickedSlot;
    private float LastClickTimer;
    private Texture2D? NationIconTexture;
    private byte NationId; // retained for future use (e.g. nation-specific UI logic)
    private Texture2D? PaperdollTexture;

    public SelfProfileEquipmentTab(GraphicsDevice device, string prefabName)
        : base(device, prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        // Create all non-anchor controls via AutoPopulate, then extract the slot elements
        var elements = AutoPopulate();

        // Build slot visuals from the prefab-created elements.
        // AutoPopulate creates UIImage elements for DoesNotReturnValue controls that have images.
        // Each slot image initially shows its _nui_eqi placeholder icon.
        foreach ((var controlName, var slot) in SlotMappings)
        {
            if (!elements.TryGetValue(controlName, out var element))
                continue;

            if (element is not UIImage slotImage)
                continue;

            // The placeholder texture was already set by AutoPopulate from the _nui_eqi frame
            var visual = new EquipmentSlotVisual
            {
                Image = slotImage,
                PlaceholderTexture = slotImage.Texture
            };

            SlotVisuals[slot] = visual;
        }

        // Stat labels — right-aligned numeric values
        StrLabel = CreateLabel("N_STR", TextAlignment.Right);
        IntLabel = CreateLabel("N_INT", TextAlignment.Right);
        WisLabel = CreateLabel("N_WIS", TextAlignment.Right);
        ConLabel = CreateLabel("N_CON", TextAlignment.Right);
        DexLabel = CreateLabel("N_DEX", TextAlignment.Right);
        AcLabel = CreateLabel("N_AC", TextAlignment.Right);

        // Player info labels — left-aligned text
        NameLabel = CreateLabel("NAME");
        ClassLabel = CreateLabel("CLASSTEXT");
        ClanLabel = CreateLabel("CLANTEXT");
        ClanTitleLabel = CreateLabel("CLANTITLETEXT");
        TitleLabel = CreateLabel("TITLETEXT");

        // Group button — single button that swaps textures based on GroupOpen state.
        // GroupBtn prefab has the "open/recruiting" images, GroupBtn_Disabled has the "closed" images.
        GroupBtn = elements.GetValueOrDefault("GroupBtn") as UIButton;

        if (GroupBtn is not null)
        {
            GroupOpenTexture = GroupBtn.NormalTexture;
            GroupBtn.PressedTexture = null;
            GroupBtn.OnClick += () => OnGroupToggled?.Invoke();
        }

        // Extract the closed-state texture from GroupBtn_Disabled, then remove it
        if (elements.TryGetValue("GroupBtn_Disabled", out var disabledElement))
        {
            GroupClosedTexture = disabledElement switch
            {
                UIButton btn => btn.NormalTexture,
                UIImage img  => img.Texture,
                _            => null
            };

            Children.Remove(disabledElement);
            disabledElement.Dispose();
        }

        // Nation icon area
        NationRect = GetRect("Nation");

        // Paperdoll area (HumanImage control rect)
        PaperdollRect = GetRect("HumanImage");

        // Hide the HumanImage element created by AutoPopulate — we render the paperdoll manually
        if (elements.TryGetValue("HumanImage", out var humanImageElement))
            humanImageElement.Visible = false;

        // Emoticon status areas
        HumanIconRect = GetRect("HumanIcon");

        // Hide auto-populated elements for emoticon — we render these manually
        if (elements.TryGetValue("HumanIcon", out var humanIconElement))
            humanIconElement.Visible = false;

        if (elements.TryGetValue("HumanState", out var humanStateElement))
            humanStateElement.Visible = false;

        // Load emoticon icons from _nemots.spf (frames 0-7)
        EmoticonIcons = new Texture2D?[EMOTICON_FRAME_COUNT];

        for (var i = 0; i < EMOTICON_FRAME_COUNT; i++)
            EmoticonIcons[i] = UiRenderer.Instance!.GetSpfTexture("_nemots.spf", i);

        // Emoticon status text label — centered in HumanState rect
        EmoticonLabel = CreateLabel("HumanState", TextAlignment.Center);

        // Tooltip label — hidden by default, follows cursor when an equipment slot is hovered
        TooltipLabel = new UILabel(device)
        {
            Name = "Tooltip",
            Visible = false,
            PaddingLeft = 1,
            PaddingTop = 1,
            BackgroundColor = new Color(
                0,
                0,
                0,
                128),
            BorderColor = Color.White,
            ZIndex = 1
        };

        AddChild(TooltipLabel);
    }

    /// <summary>
    ///     Clears all equipment slot icons, restoring placeholders.
    /// </summary>
    public void ClearAllSlots()
    {
        foreach ((_, var visual) in SlotVisuals)
        {
            if (visual.ItemTexture is not null)
            {
                visual.ItemTexture.Dispose();
                visual.ItemTexture = null;
            }

            visual.Image.Texture = visual.PlaceholderTexture;
        }
    }

    /// <summary>
    ///     Clears the item icon for a specific equipment slot, restoring the placeholder.
    /// </summary>
    public void ClearSlot(EquipmentSlot slot)
    {
        if (!SlotVisuals.TryGetValue(slot, out var visual))
            return;

        if (visual.ItemTexture is not null)
        {
            visual.ItemTexture.Dispose();
            visual.ItemTexture = null;
        }

        visual.Image.Texture = visual.PlaceholderTexture;
    }

    /// <summary>
    ///     Returns true if the given screen point is within any equipment slot image.
    /// </summary>
    public bool ContainsEquipmentSlotPoint(int screenX, int screenY)
    {
        foreach ((_, var visual) in SlotVisuals)
            if (visual.Image.ContainsPoint(screenX, screenY))
                return true;

        return false;
    }

    public override void Dispose()
    {
        foreach ((_, var visual) in SlotVisuals)
            if (visual.ItemTexture is not null)
            {
                if (visual.Image.Texture == visual.ItemTexture)
                    visual.Image.Texture = null;

                visual.ItemTexture.Dispose();
            }

        SlotVisuals.Clear();
        NationIconTexture?.Dispose();
        PaperdollTexture?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        // Nation icon
        if (NationIconTexture is not null && (NationRect != Rectangle.Empty))
            AtlasHelper.Draw(
                spriteBatch,
                NationIconTexture,
                new Vector2(sx + NationRect.X, sy + NationRect.Y),
                Color.White);

        // Paperdoll — horizontally centered, bottom-aligned within the HumanImage rect
        if (PaperdollTexture is not null && (PaperdollRect != Rectangle.Empty))
        {
            var drawX = sx + PaperdollRect.X + (PaperdollRect.Width - PaperdollTexture.Width) / 2;
            var drawY = sy + PaperdollRect.Y + PaperdollRect.Height - PaperdollTexture.Height;

            spriteBatch.Draw(PaperdollTexture, new Vector2(drawX, drawY), Color.White);
        }

        // Emoticon icon — draw at HumanIcon rect origin
        if ((HumanIconRect != Rectangle.Empty)
            && (EmoticonState < EmoticonIcons.Length)
            && EmoticonIcons[EmoticonState] is { } emoticonIcon)
            AtlasHelper.Draw(
                spriteBatch,
                emoticonIcon,
                new Vector2(sx + HumanIconRect.X, sy + HumanIconRect.Y),
                Color.White);
    }

    public event Action? OnGroupToggled;
    public event Action<EquipmentSlot>? OnUnequip;

    /// <summary>
    ///     Renders an item icon from the panel item sprite sheet using the same pipeline as inventory icons.
    /// </summary>
    private Texture2D RenderItemIcon(ushort spriteId) => UiRenderer.Instance!.GetItemIcon(spriteId);

    /// <summary>
    ///     Sets the emoticon/social status icon and text. State 0-7 maps to _nemots.spf frames.
    /// </summary>
    public void SetEmoticonState(byte state, string statusText)
    {
        EmoticonState = state;
        EmoticonLabel?.SetText(statusText);
    }

    /// <summary>
    ///     Swaps the group button texture between recruiting (open) and closed states.
    /// </summary>
    public void SetGroupOpen(bool groupOpen)
    {
        if (GroupBtn is null)
            return;

        GroupBtn.NormalTexture = groupOpen ? GroupOpenTexture : GroupClosedTexture;
    }

    /// <summary>
    ///     Sets the nation icon (from _nui_nat.spf, frame = nationId - 1).
    /// </summary>
    public void SetNation(byte nationId)
    {
        NationId = nationId;
        NationIconTexture?.Dispose();
        NationIconTexture = null;

        if (nationId > 0)
            NationIconTexture = UiRenderer.Instance!.GetSpfTexture("_nui_nat.spf", nationId - 1);
    }

    /// <summary>
    ///     Renders the paperdoll using the player's current appearance. Uses the full AislingRenderer at the south-facing idle
    ///     frame (same composition as the world aisling, just frozen).
    /// </summary>
    public void SetPaperdoll(AislingRenderer renderer, in AislingAppearance appearance)
    {
        PaperdollTexture?.Dispose();

        // South-facing (direction=2) = Right idle frame (5) + horizontal flip
        PaperdollTexture = renderer.Render(
            Device,
            in appearance,
            PAPERDOLL_IDLE_FRAME,
            flipHorizontal: true);
    }

    /// <summary>
    ///     Updates the player identity labels (name, class, clan, title).
    /// </summary>
    public void SetPlayerInfo(
        string name,
        string className,
        string clanName,
        string clanTitle,
        string title)
    {
        NameLabel?.SetText(name, Color.White);
        ClassLabel?.SetText(className, Color.White);
        ClanLabel?.SetText(clanName, Color.White);
        ClanTitleLabel?.SetText(clanTitle, Color.White);
        TitleLabel?.SetText(title, Color.White);
    }

    /// <summary>
    ///     Sets the item icon for a specific equipment slot.
    /// </summary>
    public void SetSlot(EquipmentSlot slot, ushort sprite, string? itemName = null)
    {
        if (!SlotVisuals.TryGetValue(slot, out var visual))
            return;

        // Dispose previous item texture (not the placeholder — that's shared/owned by the prefab)
        if (visual.ItemTexture is not null)
        {
            visual.ItemTexture.Dispose();
            visual.ItemTexture = null;
        }

        visual.ItemName = itemName ?? string.Empty;

        var texture = RenderItemIcon(sprite);
        visual.ItemTexture = texture;
        visual.Image.Texture = texture;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        LastClickTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        // Hover detection for equipment slot tooltips
        HoveredSlot = null;
        var mx = input.MouseX;
        var my = input.MouseY;

        foreach ((var slot, var visual) in SlotVisuals)
        {
            if (visual.ItemTexture is null)
                continue;

            if (visual.Image.ContainsPoint(mx, my))
            {
                HoveredSlot = slot;

                break;
            }
        }

        // Double-click detection for unequip
        if (input.WasLeftButtonPressed && HoveredSlot.HasValue)
        {
            if ((HoveredSlot == LastClickedSlot) && (LastClickTimer < DOUBLE_CLICK_MS))
            {
                OnUnequip?.Invoke(HoveredSlot.Value);
                LastClickedSlot = null;
            } else
            {
                LastClickedSlot = HoveredSlot;
                LastClickTimer = 0;
            }
        }

        // Position tooltip label at cursor
        if (HoveredSlot.HasValue && SlotVisuals.TryGetValue(HoveredSlot.Value, out var hovered) && (hovered.ItemName.Length > 0))
        {
            TooltipLabel.SetText(hovered.ItemName);

            var textWidth = TextRenderer.MeasureWidth(hovered.ItemName);
            var tipW = textWidth + TooltipLabel.PaddingLeft * 2;
            var tipH = 12 + TooltipLabel.PaddingTop * 2;
            var tipX = mx - tipW / 2;
            var tipY = my + 20;

            if ((tipX + tipW) > 640)
                tipX = 640 - tipW;

            if ((tipY + tipH) > 480)
                tipY = 480 - tipH;

            // Convert screen-space position to parent-relative
            TooltipLabel.X = tipX - ScreenX;
            TooltipLabel.Y = tipY - ScreenY;
            TooltipLabel.Width = tipW;
            TooltipLabel.Height = tipH;
            TooltipLabel.Visible = true;
        } else
            TooltipLabel.Visible = false;
    }

    /// <summary>
    ///     Updates the stat display labels on the equipment page.
    /// </summary>
    public void UpdateStats(
        int str,
        int intel,
        int wis,
        int con,
        int dex,
        int ac)
    {
        StrLabel?.SetText($"{str}");
        IntLabel?.SetText($"{intel}");
        WisLabel?.SetText($"{wis}");
        ConLabel?.SetText($"{con}");
        DexLabel?.SetText($"{dex}");
        AcLabel?.SetText($"{ac}");
    }

    private sealed class EquipmentSlotVisual
    {
        public required UIImage Image { get; init; }
        public string ItemName { get; set; } = string.Empty;
        public Texture2D? ItemTexture { get; set; }
        public Texture2D? PlaceholderTexture { get; init; }
    }
}