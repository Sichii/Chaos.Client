#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Merchant/shop/trainer browser panel using the lnpcd3 prefab. Handles the 6 merchant menu types: ShowItems (buy),
///     ShowPlayerItems (sell), ShowSkills, ShowSpells, ShowPlayerSkills, ShowPlayerSpells. Displays an icon+name list in
///     the Content area, item details on the right side, and page navigation. Owned by NpcSessionControl which handles
///     Escape, close, and response dispatch.
/// </summary>
public sealed class MerchantBrowserPanel : PrefabPanel
{
    private const int ICON_SIZE = 32;
    private const int ROW_HEIGHT = 40;
    private const int ICON_TEXT_GAP = 8;

    private readonly Rectangle ContentRect;
    private readonly UILabel? DescClassLabel;
    private readonly UILabel? DescLevelLabel;
    private readonly UILabel? DescTextLabel;
    private readonly UILabel? DescWeightLabel;
    private readonly List<MerchantEntry> Entries = [];
    private readonly int ItemsPerPage;

    private readonly MerchantListingPanel[] Listings;
    private readonly UILabel? MoneyLabel;
    private readonly UILabel? PageLabel;

    private int CurrentPage;
    private int SelectedIndex = -1;
    private int TotalPages;

    public MenuType CurrentMenuType { get; private set; }

    public UIButton? CloseButton { get; }
    public UIButton? PageNextButton { get; }
    public UIButton? PagePrevButton { get; }
    public UIButton? TabNextButton { get; }
    public UIButton? TabPrevButton { get; }

    public MerchantBrowserPanel()
        : base("lnpcd3")
    {
        Name = "MerchantBrowser";
        Visible = false;

        CloseButton = CreateButton("Btn1");
        PagePrevButton = CreateButton("PagePrev");
        PageNextButton = CreateButton("PageNext");
        TabPrevButton = CreateButton("TabPrev");
        TabNextButton = CreateButton("TabNext");

        if (CloseButton is not null)
            CloseButton.OnClick += () => OnClose?.Invoke();

        if (PagePrevButton is not null)
            PagePrevButton.OnClick += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    UpdatePageDisplay();
                }
            };

        if (PageNextButton is not null)
            PageNextButton.OnClick += () =>
            {
                if (CurrentPage < (TotalPages - 1))
                {
                    CurrentPage++;
                    UpdatePageDisplay();
                }
            };

        ContentRect = GetRect("Content");
        ItemsPerPage = ContentRect.Height > 0 ? ContentRect.Height / ROW_HEIGHT : 4;

        // Create listing panels as children for each visible row slot
        Listings = new MerchantListingPanel[ItemsPerPage];

        for (var i = 0; i < ItemsPerPage; i++)
        {
            var listing = new MerchantListingPanel
            {
                X = ContentRect.X,
                Y = ContentRect.Y + i * ROW_HEIGHT,
                Width = ContentRect.Width,
                Height = ROW_HEIGHT,
                Visible = false
            };

            var rowIndex = i;
            listing.OnClick += () => HandleListingClick(rowIndex);
            Listings[i] = listing;
            AddChild(listing);
        }

        DescClassLabel = CreateLabel("DescClass");
        DescLevelLabel = CreateLabel("DescLevel");
        DescWeightLabel = CreateLabel("DescWeight");
        DescTextLabel = CreateLabel("DescText");
        MoneyLabel = CreateLabel("Money", TextAlignment.Right);
        PageLabel = CreateLabel("Page", TextAlignment.Center);
    }

    private void ClearDetails()
    {
        MoneyLabel?.Text = string.Empty;
        DescClassLabel?.Text = string.Empty;
        DescLevelLabel?.Text = string.Empty;
        DescWeightLabel?.Text = string.Empty;
        DescTextLabel?.Text = string.Empty;
    }

    private void ClearEntries()
    {
        Entries.Clear();
        SelectedIndex = -1;

        foreach (var listing in Listings)
        {
            listing.ClearEntry();
            listing.IsSelected = false;
            listing.Visible = false;
        }
    }

    /// <summary>
    ///     Returns the name of the entry at the given absolute index, or null if out of range.
    /// </summary>
    public string? GetEntryName(int index)
    {
        if ((index < 0) || (index >= Entries.Count))
            return null;

        return Entries[index].Name;
    }

    /// <summary>
    ///     Returns the slot byte for the entry at the given absolute index, or null if out of range.
    /// </summary>
    public byte? GetEntrySlot(int index)
    {
        if ((index < 0) || (index >= Entries.Count))
            return null;

        return Entries[index].Slot;
    }

    private void HandleListingClick(int rowIndex)
    {
        var absoluteIndex = CurrentPage * ItemsPerPage + rowIndex;

        if (absoluteIndex >= Entries.Count)
            return;

        if (absoluteIndex == SelectedIndex)
            OnItemSelected?.Invoke(SelectedIndex);
        else
        {
            SelectedIndex = absoluteIndex;
            ShowDetails(SelectedIndex);
            UpdateListingStates();
        }
    }

    public override void Hide()
    {
        Visible = false;
        ClearEntries();
        ClearDetails();
    }

    public event Action? OnClose;
    public event Action<int>? OnItemSelected;

    private void PopulateItems(DisplayMenuArgs args)
    {
        if (args.Items is null)
            return;

        foreach (var item in args.Items)
        {
            var icon = UiRenderer.Instance!.GetItemIcon(item.Sprite, item.Color);

            Entries.Add(
                new MerchantEntry(
                    item.Name,
                    icon,
                    item.Cost ?? 0,
                    item.Slot));
        }
    }

    private void PopulatePlayerItems(DisplayMenuArgs args)
    {
        if (args.Slots is null)
            return;

        foreach (var slot in args.Slots)
        {
            ref readonly var slotData = ref WorldState.Inventory.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = UiRenderer.Instance!.GetItemIcon(slotData.Sprite);

            Entries.Add(
                new MerchantEntry(
                    slotData.Name ?? string.Empty,
                    icon,
                    0,
                    slot));
        }
    }

    private void PopulatePlayerSkills()
    {
        for (byte slot = 1; slot <= SkillBook.MAX_SLOTS; slot++)
        {
            ref readonly var slotData = ref WorldState.SkillBook.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = UiRenderer.Instance!.GetSkillIcon(slotData.Sprite);

            Entries.Add(
                new MerchantEntry(
                    slotData.Name ?? string.Empty,
                    icon,
                    0,
                    slot));
        }
    }

    private void PopulatePlayerSpells()
    {
        for (byte slot = 1; slot <= SpellBook.MAX_SLOTS; slot++)
        {
            ref readonly var slotData = ref WorldState.SpellBook.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = UiRenderer.Instance!.GetSpellIcon(slotData.Sprite);

            Entries.Add(
                new MerchantEntry(
                    slotData.Name ?? string.Empty,
                    icon,
                    0,
                    slot));
        }
    }

    private void PopulateSkills(DisplayMenuArgs args)
    {
        if (args.Skills is null)
            return;

        foreach (var skill in args.Skills)
        {
            var icon = UiRenderer.Instance!.GetSkillIcon(skill.Sprite);

            Entries.Add(
                new MerchantEntry(
                    skill.Name,
                    icon,
                    0,
                    skill.Slot));
        }
    }

    private void PopulateSpells(DisplayMenuArgs args)
    {
        if (args.Spells is null)
            return;

        foreach (var spell in args.Spells)
        {
            var icon = UiRenderer.Instance!.GetSpellIcon(spell.Sprite);

            Entries.Add(
                new MerchantEntry(
                    spell.Name,
                    icon,
                    0,
                    spell.Slot));
        }
    }

    private void ShowDetails(int absoluteIndex)
    {
        if ((absoluteIndex < 0) || (absoluteIndex >= Entries.Count))
        {
            ClearDetails();

            return;
        }

        var entry = Entries[absoluteIndex];

        // For buy shops, show cost
        if (CurrentMenuType == MenuType.ShowItems)
            MoneyLabel?.Text = entry.Cost > 0 ? entry.Cost.ToString("N0") : string.Empty;
        else
            MoneyLabel?.Text = string.Empty;

        // Clear description fields — detail population would require metadata queries
        // that aren't available at this level. The name in the list is sufficient for v1.
        DescClassLabel?.Text = string.Empty;
        DescLevelLabel?.Text = string.Empty;
        DescWeightLabel?.Text = string.Empty;
        DescTextLabel?.Text = string.Empty;
    }

    /// <summary>
    ///     Shows the merchant panel for a DisplayMenuArgs with one of the 6 merchant menu types.
    /// </summary>
    public void ShowMerchant(DisplayMenuArgs args)
    {
        CurrentMenuType = args.MenuType;

        ClearEntries();
        CurrentPage = 0;
        SelectedIndex = -1;

        switch (args.MenuType)
        {
            case MenuType.ShowItems:
                PopulateItems(args);

                break;

            case MenuType.ShowPlayerItems:
                PopulatePlayerItems(args);

                break;

            case MenuType.ShowSkills:
                PopulateSkills(args);

                break;

            case MenuType.ShowSpells:
                PopulateSpells(args);

                break;

            case MenuType.ShowPlayerSkills:
                PopulatePlayerSkills();

                break;

            case MenuType.ShowPlayerSpells:
                PopulatePlayerSpells();

                break;
        }

        TotalPages = Entries.Count > 0 ? (Entries.Count + ItemsPerPage - 1) / ItemsPerPage : 1;

        UpdatePageDisplay();
        ClearDetails();
        Show();
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime, input);
    }

    private void UpdateListingStates()
    {
        var pageStart = CurrentPage * ItemsPerPage;

        for (var i = 0; i < Listings.Length; i++)
        {
            var absoluteIndex = pageStart + i;
            var listing = Listings[i];

            if (absoluteIndex < Entries.Count)
            {
                var entry = Entries[absoluteIndex];
                listing.SetEntry(entry.Icon, entry.Name);
                listing.IsSelected = absoluteIndex == SelectedIndex;
                listing.Visible = true;
            } else
            {
                listing.ClearEntry();
                listing.IsSelected = false;
                listing.Visible = false;
            }
        }
    }

    private void UpdatePageDisplay()
    {
        PageLabel?.Text = $"{CurrentPage + 1}/{TotalPages}";

        if (PagePrevButton is not null)
            PagePrevButton.Visible = CurrentPage > 0;

        if (PageNextButton is not null)
            PageNextButton.Visible = CurrentPage < (TotalPages - 1);

        UpdateListingStates();
    }

    private sealed record MerchantEntry(
        string Name,
        Texture2D? Icon,
        int Cost,
        byte Slot);

    /// <summary>
    ///     A single row in the merchant listing. Renders a centered icon and name text, with highlight states for hover and
    ///     selection.
    /// </summary>
    private sealed class MerchantListingPanel : UIPanel
    {
        private static readonly Color SELECTED_COLOR = new(
            80,
            80,
            120,
            100);

        private static readonly Color HOVERED_COLOR = new(
            60,
            60,
            90,
            60);

        private readonly CenteredIcon IconImage;
        private readonly UILabel NameLabel;
        private bool IsHovered;
        public bool IsSelected { get; set; }

        public MerchantListingPanel()
        {
            IconImage = new CenteredIcon
            {
                X = 4,
                Y = 0,
                Width = ICON_SIZE,
                Height = ROW_HEIGHT
            };

            NameLabel = new UILabel
            {
                X = 4 + ICON_SIZE + ICON_TEXT_GAP,
                Y = (ROW_HEIGHT - TextRenderer.CHAR_HEIGHT) / 2,
                Width = 200,
                Height = TextRenderer.CHAR_HEIGHT,
                ForegroundColor = Color.White
            };

            AddChild(IconImage);
            AddChild(NameLabel);
        }

        public void ClearEntry()
        {
            IconImage.Texture = null;
            NameLabel.Text = string.Empty;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible)
                return;

            // Highlight background
            if (IsSelected)
                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        ScreenX,
                        ScreenY,
                        Width,
                        Height),
                    SELECTED_COLOR);
            else if (IsHovered)
                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        ScreenX,
                        ScreenY,
                        Width,
                        Height),
                    HOVERED_COLOR);

            NameLabel.ForegroundColor = IsSelected
                ? Color.Yellow
                : IsHovered
                    ? Color.LightGoldenrodYellow
                    : Color.White;

            base.Draw(spriteBatch);
        }

        public event Action? OnClick;

        public void SetEntry(Texture2D? icon, string name)
        {
            IconImage.Texture = icon;
            NameLabel.Text = name;
        }

        public override void Update(GameTime gameTime, InputBuffer input)
        {
            if (!Visible || !Enabled)
                return;

            IsHovered = ContainsPoint(input.MouseX, input.MouseY);

            if (IsHovered && input.WasLeftButtonPressed)
                OnClick?.Invoke();

            base.Update(gameTime, input);
        }
    }
}