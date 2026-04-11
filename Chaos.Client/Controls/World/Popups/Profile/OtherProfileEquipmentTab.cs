#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Definitions;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
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

        //no stat labels — _nui_eqa does not have n_str/int/wis/con/dex/ac

        //player info labels
        NameLabel = CreateLabel("NAME");
        ClassLabel = CreateLabel("CLASSTEXT");
        ClanLabel = CreateLabel("CLANTEXT");
        ClanTitleLabel = CreateLabel("CLANTITLETEXT");
        TitleLabel = CreateLabel("TITLETEXT");

        //group button — sends group invite for the displayed player
        GroupBtn = CreateButton("GroupBtn");

        if (GroupBtn is not null)
        {
            GroupOpenTexture = GroupBtn.NormalTexture;
            GroupBtn.PressedTexture = null;

            GroupBtn.Clicked += () =>
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

        //nation icon and text
        NationImage = CreateImage("Nation");
        NationTextLabel = CreateLabel("NationText");

        NationTextLabel?.VerticalAlignment = VerticalAlignment.Top;

        //paperdoll area
        PaperdollImage = CreateImage("HumanImage");

        //portrait and profile text
        PortraitImage = CreateImage("Portrait");
        PortraitTextLabel = CreateLabel("PortraitText");

        if (PortraitTextLabel is not null)
        {
            PortraitTextLabel.WordWrap = true;
            PortraitTextLabel.HorizontalAlignment = HorizontalAlignment.Left;
            PortraitTextLabel.ForegroundColor = Color.White;
            PortraitTextLabel.VerticalAlignment = VerticalAlignment.Top;
        }

        //emoticon status
        HumanIconRect = GetRect("HumanIcon");

        EmoticonIcons = new Texture2D?[EMOTICON_FRAME_COUNT];

        for (var i = 0; i < EMOTICON_FRAME_COUNT; i++)
            EmoticonIcons[i] = UiRenderer.Instance!.GetSpfTexture("_nemots.spf", i);

        EmoticonLabel = CreateLabel("HumanState", HorizontalAlignment.Center);

        //tooltip label for equipment slot hover
        TooltipLabel = new UILabel
        {
            Name = "Tooltip",
            Visible = false,
            IsHitTestVisible = false,
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

        //emoticon icon — draw at humanicon rect origin (not a uiimage since frame changes per state)
        if ((HumanIconRect != Rectangle.Empty)
            && (EmoticonState < EmoticonIcons.Length)
            && EmoticonIcons[EmoticonState] is { } emoticonIcon)
            AtlasHelper.Draw(
                spriteBatch,
                emoticonIcon,
                new Vector2(ScreenX + HumanIconRect.X, ScreenY + HumanIconRect.Y),
                Color.White);
    }

    public event GroupInviteRequestedHandler? OnGroupInviteRequested;

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

            var texture = UiRenderer.Instance!.GetItemIcon(item.Sprite, item.Color);
            visual.ItemTexture = texture;
            visual.Image.Texture = texture;
        }
    }

    /// <summary>
    ///     Sets the group button to open or closed state (display only).
    /// </summary>
    public void SetGroupOpen(bool groupOpen)
    {
        GroupBtn?.NormalTexture = groupOpen ? GroupOpenTexture : GroupClosedTexture;
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

        NationImage?.Texture = NationIconTexture;

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

        PaperdollImage?.Texture = PaperdollTexture;
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

    public void SetPortrait(byte[]? portraitData)
    {
        if (PortraitImage is null)
            return;

        PortraitImage.Texture?.Dispose();
        PortraitImage.Texture = null;

        if (portraitData is { Length: > 0 })
        {
            using var skImage = SKImage.FromEncodedData(portraitData);

            if (skImage is not null)
                PortraitImage.Texture = TextureConverter.ToTexture2D(skImage);
        }
    }

    public void SetProfileText(string text)
    {
        PortraitTextLabel?.Text = text;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        string? foundName = null;

        foreach ((_, var visual) in SlotVisuals)
        {
            if (visual.Image.ContainsPoint(e.ScreenX, e.ScreenY) && (visual.ItemTexture is not null))
            {
                foundName = visual.ItemName;

                break;
            }
        }

        if (!string.IsNullOrEmpty(foundName))
        {
            TooltipLabel.Text = foundName;
            TooltipLabel.Width = TextRenderer.MeasureWidth(foundName) + 4;
            TooltipLabel.Height = TextRenderer.CHAR_HEIGHT + 4;
            TooltipLabel.X = e.ScreenX - ScreenX + 12;
            TooltipLabel.Y = e.ScreenY - ScreenY + 12;
            TooltipLabel.Visible = true;
        } else
            TooltipLabel.Visible = false;
    }

    public override void OnMouseLeave()
    {
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