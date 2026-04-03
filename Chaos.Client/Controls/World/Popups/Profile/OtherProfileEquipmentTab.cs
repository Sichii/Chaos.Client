#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Equipment tab page for viewing another player's profile, loaded from _nui_eqa prefab. Same layout as
///     <see cref="SelfProfileEquipmentTab" /> but without stat labels and without unequip interaction. Equipment is
///     populated from <see cref="OtherProfileArgs" /> packet data rather than WorldState.
/// </summary>
public sealed class OtherProfileEquipmentTab : PrefabPanel
{
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

    private const int EMOTICON_FRAME_COUNT = 8;
    private const int PAPERDOLL_IDLE_FRAME = 5;

    private readonly UILabel? ClanLabel;
    private readonly UILabel? ClanTitleLabel;
    private readonly UILabel? ClassLabel;
    private readonly Texture2D?[] EmoticonIcons;
    private readonly UILabel? EmoticonLabel;
    private readonly UIButton? GroupBtn;
    private readonly Texture2D? GroupClosedTexture;
    private readonly Texture2D? GroupOpenTexture;
    private readonly Rectangle HumanIconRect;
    private readonly UILabel? NameLabel;
    private readonly UIImage? NationImage;
    private readonly UILabel? NationTextLabel;
    private readonly UIImage? PaperdollImage;
    private readonly UIImage? PortraitImage;
    private readonly UILabel? PortraitTextLabel;
    private readonly Dictionary<EquipmentSlot, EquipmentSlotVisual> SlotVisuals = new();
    private readonly UILabel? TitleLabel;
    private readonly UILabel TooltipLabel;
    private byte EmoticonState;
    private EquipmentSlot? HoveredSlot;
    private Texture2D? NationIconTexture;
    private Texture2D? PaperdollTexture;

    public OtherProfileEquipmentTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        foreach ((var controlName, var slot) in SlotMappings)
        {
            if (CreateImage(controlName) is not { } slotImage)
                continue;

            var visual = new EquipmentSlotVisual
            {
                Image = slotImage,
                PlaceholderTexture = slotImage.Texture
            };

            SlotVisuals[slot] = visual;
        }

        // No stat labels — _nui_eqa does not have N_STR/INT/WIS/CON/DEX/AC

        // Player info labels
        NameLabel = CreateLabel("NAME");
        ClassLabel = CreateLabel("CLASSTEXT");
        ClanLabel = CreateLabel("CLANTEXT");
        ClanTitleLabel = CreateLabel("CLANTITLETEXT");
        TitleLabel = CreateLabel("TITLETEXT");

        // Group button — sends group invite for the displayed player
        GroupBtn = CreateButton("GroupBtn");

        if (GroupBtn is not null)
        {
            GroupOpenTexture = GroupBtn.NormalTexture;
            GroupBtn.PressedTexture = null;

            GroupBtn.OnClick += () =>
            {
                var name = NameLabel?.Text;

                if (!string.IsNullOrEmpty(name))
                    OnGroupInviteRequested?.Invoke(name);
            };
        }

        if (CreateImage("GroupBtn_Disabled") is { } disabledImage)
        {
            GroupClosedTexture = disabledImage.Texture;
            Children.Remove(disabledImage);
            disabledImage.Dispose();
        }

        // Nation icon and text
        NationImage = CreateImage("Nation");
        NationTextLabel = CreateLabel("NationText");

        if (NationTextLabel is not null)
            NationTextLabel.TopAligned = true;

        // Paperdoll area
        PaperdollImage = CreateImage("HumanImage");

        // Portrait and profile text
        PortraitImage = CreateImage("Portrait");
        PortraitTextLabel = CreateLabel("PortraitText");

        // Emoticon status
        HumanIconRect = GetRect("HumanIcon");

        EmoticonIcons = new Texture2D?[EMOTICON_FRAME_COUNT];

        for (var i = 0; i < EMOTICON_FRAME_COUNT; i++)
            EmoticonIcons[i] = UiRenderer.Instance!.GetSpfTexture("_nemots.spf", i);

        EmoticonLabel = CreateLabel("HumanState", TextAlignment.Center);

        // Tooltip label for equipment slot hover
        TooltipLabel = new UILabel
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
    ///     Clears all equipment slots and resets to placeholders.
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

            visual.ItemName = string.Empty;
            visual.Image.Texture = visual.PlaceholderTexture;
        }
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

        // Emoticon icon — draw at HumanIcon rect origin (not a UIImage since frame changes per state)
        if ((HumanIconRect != Rectangle.Empty)
            && (EmoticonState < EmoticonIcons.Length)
            && EmoticonIcons[EmoticonState] is { } emoticonIcon)
            AtlasHelper.Draw(
                spriteBatch,
                emoticonIcon,
                new Vector2(ScreenX + HumanIconRect.X, ScreenY + HumanIconRect.Y),
                Color.White);
    }

    public event Action<string>? OnGroupInviteRequested;

    /// <summary>
    ///     Sets the emoticon/social status icon and text.
    /// </summary>
    public void SetEmoticonState(byte state, string statusText)
    {
        EmoticonState = state;
        EmoticonLabel?.Text = statusText;
    }

    /// <summary>
    ///     Populates all equipment slots from the packet data.
    /// </summary>
    public void SetEquipment(IDictionary<EquipmentSlot, ItemInfo?> equipment)
    {
        ClearAllSlots();

        foreach ((var slot, var item) in equipment)
        {
            if (item is null || (item.Sprite == 0))
                continue;

            if (!SlotVisuals.TryGetValue(slot, out var visual))
                continue;

            var texture = UiRenderer.Instance!.GetItemIcon(item.Sprite);
            visual.ItemTexture = texture;
            visual.Image.Texture = texture;
        }
    }

    /// <summary>
    ///     Sets the group button to open or closed state (display only).
    /// </summary>
    public void SetGroupOpen(bool groupOpen)
    {
        if (GroupBtn is null)
            return;

        GroupBtn.NormalTexture = groupOpen ? GroupOpenTexture : GroupClosedTexture;
    }

    /// <summary>
    ///     Sets the nation icon.
    /// </summary>
    public void SetNation(byte nationId)
    {
        NationIconTexture?.Dispose();
        NationIconTexture = null;

        if (nationId > 0)
            NationIconTexture = UiRenderer.Instance!.GetSpfTexture("_nui_nat.spf", nationId - 1);

        if (NationImage is not null)
            NationImage.Texture = NationIconTexture;

        if (NationTextLabel is not null)
        {
            var nationMeta = DataContext.MetaFiles.GetNationMetadata();
            NationTextLabel.Text = nationMeta?.Nations.TryGetValue(nationId, out var name) == true ? name : string.Empty;
        }
    }

    /// <summary>
    ///     Renders the paperdoll using the entity's current appearance.
    /// </summary>
    public void SetPaperdoll(AislingRenderer renderer, in AislingAppearance appearance)
    {
        PaperdollTexture?.Dispose();
        PaperdollTexture = renderer.Render(in appearance, PAPERDOLL_IDLE_FRAME, flipHorizontal: true);

        if (PaperdollImage is not null)
            PaperdollImage.Texture = PaperdollTexture;
    }

    /// <summary>
    ///     Updates the player identity labels.
    /// </summary>
    public void SetPlayerInfo(
        string name,
        string className,
        string clanName,
        string clanTitle,
        string title)
    {
        NameLabel?.ForegroundColor = LegendColors.White;
        NameLabel?.Text = name;
        ClassLabel?.ForegroundColor = LegendColors.White;
        ClassLabel?.Text = className;
        ClanLabel?.ForegroundColor = LegendColors.White;
        ClanLabel?.Text = clanName;
        ClanTitleLabel?.ForegroundColor = LegendColors.White;
        ClanTitleLabel?.Text = clanTitle;
        TitleLabel?.ForegroundColor = LegendColors.White;
        TitleLabel?.Text = title;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);

        // Hover detection for equipment slot tooltips (display only — no unequip)
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

        // Position tooltip label at cursor
        if (HoveredSlot.HasValue && SlotVisuals.TryGetValue(HoveredSlot.Value, out var hovered) && (hovered.ItemName.Length > 0))
        {
            TooltipLabel.Text = hovered.ItemName;

            var textWidth = TextRenderer.MeasureWidth(hovered.ItemName);
            var tipW = textWidth + TooltipLabel.PaddingLeft + TooltipLabel.PaddingRight;
            var tipH = 12 + TooltipLabel.PaddingTop + TooltipLabel.PaddingBottom;
            var tipX = mx - tipW / 2;
            var tipY = my + 20;

            if ((tipX + tipW) > 640)
                tipX = 640 - tipW;

            if ((tipY + tipH) > 480)
                tipY = 480 - tipH;

            TooltipLabel.X = tipX - ScreenX;
            TooltipLabel.Y = tipY - ScreenY;
            TooltipLabel.Width = tipW;
            TooltipLabel.Height = tipH;
            TooltipLabel.Visible = true;
        } else
            TooltipLabel.Visible = false;
    }

    private sealed class EquipmentSlotVisual
    {
        public required UIImage Image { get; init; }
        public string ItemName { get; set; } = string.Empty;
        public Texture2D? ItemTexture { get; set; }
        public Texture2D? PlaceholderTexture { get; init; }
    }
}