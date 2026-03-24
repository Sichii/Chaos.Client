#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Merchant/shop/trainer dialog using the lnpcd3 prefab. Handles the 6 merchant menu types: ShowItems (buy),
///     ShowPlayerItems (sell), ShowSkills, ShowSpells, ShowPlayerSkills, ShowPlayerSpells. Displays an icon+name list in
///     the Content area, item details on the right side, page navigation, and a close button.
/// </summary>
public sealed class MerchantDialogControl : PrefabPanel
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
    private readonly UILabel? MoneyLabel;
    private readonly UILabel? PageLabel;

    private int CurrentPage;
    private int HoveredIndex = -1;
    private int SelectedIndex = -1;
    private int TotalPages;
    public MenuType CurrentMenuType { get; private set; }
    public ushort PursuitId { get; private set; }

    public EntityType SourceEntityType { get; private set; }
    public uint? SourceId { get; private set; }

    public UIButton? CloseButton { get; }
    public UIButton? PageNextButton { get; }
    public UIButton? PagePrevButton { get; }
    public UIButton? TabNextButton { get; }
    public UIButton? TabPrevButton { get; }

    public MerchantDialogControl()
        : base("lnpcd3")
    {
        Name = "MerchantDialog";
        Visible = false;

        CloseButton = CreateButton("Btn1");
        PagePrevButton = CreateButton("PagePrev");
        PageNextButton = CreateButton("PageNext");
        TabPrevButton = CreateButton("TabPrev");
        TabNextButton = CreateButton("TabNext");

        if (CloseButton is not null)
            CloseButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

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

        DescClassLabel = CreateLabel("DescClass");
        DescLevelLabel = CreateLabel("DescLevel");
        DescWeightLabel = CreateLabel("DescWeight");
        DescTextLabel = CreateLabel("DescText");
        MoneyLabel = CreateLabel("Money", TextAlignment.Right);
        PageLabel = CreateLabel("Page", TextAlignment.Center);
    }

    private void ClearDetails()
    {
        MoneyLabel?.SetText(string.Empty);
        DescClassLabel?.SetText(string.Empty);
        DescLevelLabel?.SetText(string.Empty);
        DescWeightLabel?.SetText(string.Empty);
        DescTextLabel?.SetText(string.Empty);
    }

    private void ClearEntries()
    {
        foreach (var entry in Entries)
            entry.CachedText?.Dispose();

        Entries.Clear();
        SelectedIndex = -1;
        HoveredIndex = -1;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        // Draw visible entries for the current page
        var pageStart = CurrentPage * ItemsPerPage;
        var pageEnd = Math.Min(pageStart + ItemsPerPage, Entries.Count);

        for (var i = pageStart; i < pageEnd; i++)
        {
            var entry = Entries[i];
            var rowIndex = i - pageStart;
            var rowY = ContentRect.Y + rowIndex * ROW_HEIGHT;

            // Highlight hovered/selected row
            if (i == SelectedIndex)
                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        sx + ContentRect.X,
                        sy + rowY,
                        ContentRect.Width,
                        ROW_HEIGHT),
                    new Color(
                        80,
                        80,
                        120,
                        100));
            else if (i == HoveredIndex)
                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        sx + ContentRect.X,
                        sy + rowY,
                        ContentRect.Width,
                        ROW_HEIGHT),
                    new Color(
                        60,
                        60,
                        90,
                        60));

            // Draw icon
            if (entry.Icon is not null)
            {
                var iconY = rowY + (ROW_HEIGHT - ICON_SIZE) / 2;

                spriteBatch.Draw(
                    entry.Icon,
                    new Rectangle(
                        sx + ContentRect.X + 4,
                        sy + iconY,
                        ICON_SIZE,
                        ICON_SIZE),
                    Color.White);
            }

            // Draw name text vertically centered with the icon
            var textColor = i == SelectedIndex
                ? Color.Yellow
                : i == HoveredIndex
                    ? Color.LightGoldenrodYellow
                    : Color.White;

            entry.CachedText ??= new CachedText();
            entry.CachedText.Update(entry.Name, textColor);

            var textX = sx + ContentRect.X + 4 + ICON_SIZE + ICON_TEXT_GAP;
            var textY = sy + rowY + (ROW_HEIGHT - TextRenderer.CHAR_HEIGHT) / 2;
            entry.CachedText.Draw(spriteBatch, new Vector2(textX, textY));
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

    private void PopulatePlayerItems(DisplayMenuArgs args, ConnectionManager connection)
    {
        if (args.Slots is null)
            return;

        foreach (var slot in args.Slots)
        {
            if (!connection.InventorySlots.TryGetValue(slot, out var slotInfo))
                continue;

            var icon = UiRenderer.Instance!.GetItemIcon(slotInfo.Sprite);

            Entries.Add(
                new MerchantEntry(
                    slotInfo.Name,
                    icon,
                    0,
                    slot));
        }
    }

    private void PopulatePlayerSkills(ConnectionManager connection)
    {
        foreach ((var slot, (var sprite, var name)) in connection.SkillSlots)
        {
            var icon = UiRenderer.Instance!.GetSkillIcon(sprite);

            Entries.Add(
                new MerchantEntry(
                    name,
                    icon,
                    0,
                    slot));
        }
    }

    private void PopulatePlayerSpells(ConnectionManager connection)
    {
        foreach ((var slot, var spellInfo) in connection.SpellSlots)
        {
            var icon = UiRenderer.Instance!.GetSpellIcon(spellInfo.Sprite);

            Entries.Add(
                new MerchantEntry(
                    spellInfo.PanelName,
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
            MoneyLabel?.SetText(entry.Cost > 0 ? entry.Cost.ToString("N0") : string.Empty);
        else
            MoneyLabel?.SetText(string.Empty);

        // Clear description fields — detail population would require metadata queries
        // that aren't available at this level. The name in the list is sufficient for v1.
        DescClassLabel?.SetText(string.Empty);
        DescLevelLabel?.SetText(string.Empty);
        DescWeightLabel?.SetText(string.Empty);
        DescTextLabel?.SetText(string.Empty);
    }

    /// <summary>
    ///     Shows the merchant panel for a DisplayMenuArgs with one of the 6 merchant menu types.
    /// </summary>
    public void ShowMerchant(DisplayMenuArgs args, ConnectionManager connection)
    {
        CurrentMenuType = args.MenuType;
        SourceEntityType = args.EntityType;
        SourceId = args.SourceId;
        PursuitId = args.PursuitId;

        ClearEntries();
        CurrentPage = 0;
        SelectedIndex = -1;
        HoveredIndex = -1;

        switch (args.MenuType)
        {
            case MenuType.ShowItems:
                PopulateItems(args);

                break;

            case MenuType.ShowPlayerItems:
                PopulatePlayerItems(args, connection);

                break;

            case MenuType.ShowSkills:
                PopulateSkills(args);

                break;

            case MenuType.ShowSpells:
                PopulateSpells(args);

                break;

            case MenuType.ShowPlayerSkills:
                PopulatePlayerSkills(connection);

                break;

            case MenuType.ShowPlayerSpells:
                PopulatePlayerSpells(connection);

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

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        // Hover tracking over visible rows
        HoveredIndex = -1;
        var pageStart = CurrentPage * ItemsPerPage;
        var pageEnd = Math.Min(pageStart + ItemsPerPage, Entries.Count);
        var visibleCount = pageEnd - pageStart;

        for (var i = 0; i < visibleCount; i++)
        {
            var rowY = ContentRect.Y + i * ROW_HEIGHT;
            var sx = ScreenX + ContentRect.X;
            var sy = ScreenY + rowY;

            if ((input.MouseX >= sx)
                && (input.MouseX < (sx + ContentRect.Width))
                && (input.MouseY >= sy)
                && (input.MouseY < (sy + ROW_HEIGHT)))
            {
                HoveredIndex = pageStart + i;

                break;
            }
        }

        // Click handling: first click selects and shows details, second click on same entry confirms
        if (input.WasLeftButtonPressed && (HoveredIndex >= 0))
        {
            if (HoveredIndex == SelectedIndex)

                // Second click on already-selected entry — confirm selection
                OnItemSelected?.Invoke(SelectedIndex);
            else
            {
                // First click — select and show details
                SelectedIndex = HoveredIndex;
                ShowDetails(SelectedIndex);
            }
        }

        base.Update(gameTime, input);
    }

    private void UpdatePageDisplay()
    {
        PageLabel?.SetText($"{CurrentPage + 1}/{TotalPages}");

        if (PagePrevButton is not null)
            PagePrevButton.Visible = CurrentPage > 0;

        if (PageNextButton is not null)
            PageNextButton.Visible = CurrentPage < (TotalPages - 1);
    }

    private sealed class MerchantEntry(
        string name,
        Texture2D? icon,
        int cost,
        byte slot)
    {
        public CachedText? CachedText { get; set; }
        public int Cost { get; } = cost;
        public Texture2D? Icon { get; } = icon;
        public string Name { get; } = name;
        public byte Slot { get; } = slot;
    }
}