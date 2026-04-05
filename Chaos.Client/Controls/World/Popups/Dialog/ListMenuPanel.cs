#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Dialog;

/// <summary>
///     Two-column scrollable icon+name list panel for ShowPlayerItems, ShowPlayerSkills, ShowPlayerSpells, ShowSkills, and
///     ShowSpells menu types. Uses the lnpcd2 prefab with 9-slice frame. Each cell shows an icon and the entity name.
///     Visually matches NPCListMenuDialog from the original client.
/// </summary>
public sealed class ListMenuPanel : FramedDialogPanel
{
    private const int ICON_SIZE = 32;
    private const int ROW_HEIGHT = 32;
    private const int ICON_TEXT_GAP = 4;
    private const int COLUMN_COUNT = 2;

    // Content area from lnpcd2 template (relative to panel)
    private const int CONTENT_X = 13;
    private const int CONTENT_Y = 6;
    private const int CONTENT_WIDTH = 400;
    private const int CONTENT_HEIGHT = 160;

    // Panel sizing
    private const int PANEL_WIDTH = 426;
    private const int MIN_PANEL_HEIGHT = 80;
    private const int MAX_VISIBLE_ROWS = CONTENT_HEIGHT / ROW_HEIGHT;

    // One extra row for the partially-visible peek effect at the bottom
    private const int DISPLAY_ROWS = MAX_VISIBLE_ROWS + 1;

    private static readonly RasterizerState ScissorRasterizer = new()
    {
        ScissorTestEnable = true
    };

    // Border metrics (from FramedDialogPanel)
    private const int BORDER_BOTTOM = 30;
    private const int BTN_WIDTH = 61;
    private const int BTN_HEIGHT = 22;

    // Bottom of panel aligns with top of the dialog bottom bar
    private const int BOTTOM_ANCHOR_Y = 372;

    private static readonly Color SELECTED_TEXT_COLOR = new(206, 0, 16);
    private readonly List<ListEntryData> Entries = [];

    private readonly List<ListEntryControl> EntryControls = [];
    private readonly ScrollBarControl ScrollBar;

    private int ColumnWidth;
    private int HoveredIndex = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;

    private int TotalRows => (Entries.Count + COLUMN_COUNT - 1) / COLUMN_COUNT;

    public ListMenuPanel()
        : base("lnpcd2", false)
    {
        Name = "ListMenu";
        Visible = false;

        OkButton = CreateButton("Btn1");

        if (OkButton is not null)
        {
            OkButton.DisabledTexture = UiRenderer.Instance!.GetSpfTexture("_nbtn.spf", 5);
            OkButton.Enabled = false;

            OkButton.OnClick += () =>
            {
                if (SelectedIndex >= 0)
                    OnItemSelected?.Invoke(SelectedIndex);
            };
        }

        ScrollBar = new ScrollBarControl
        {
            Name = "ListScrollBar",
            X = CONTENT_X + CONTENT_WIDTH - ScrollBarControl.DEFAULT_WIDTH,
            Y = CONTENT_Y,
            Height = CONTENT_HEIGHT,
            Visible = false
        };

        ScrollBar.OnValueChanged += value =>
        {
            ScrollOffset = value;
            SelectedIndex = -1;
            LayoutEntries();
        };

        AddChild(ScrollBar);
    }

    private void AddEntry(string name, byte slot, Texture2D? icon)
    {
        var displayName = name.Length > 23 ? name[..20] + "..." : name;

        Entries.Add(
            new ListEntryData(
                name,
                displayName,
                slot,
                icon));
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // Hide entry rows so base.Draw() only renders frame + scrollbar + ok button
        SetEntryVisibility(false);
        base.Draw(spriteBatch);
        SetEntryVisibility(true);

        if (EntryControls.Count == 0)
            return;

        // Draw entry controls with scissor clipping so the peek row is partially visible
        var device = spriteBatch.GraphicsDevice;

        var clipRect = new Rectangle(
            ScreenX + CONTENT_X,
            ScreenY + CONTENT_Y,
            CONTENT_WIDTH,
            CONTENT_HEIGHT);

        spriteBatch.End();

        var prevScissor = device.ScissorRectangle;
        device.ScissorRectangle = clipRect;

        spriteBatch.Begin(samplerState: GlobalSettings.Sampler, rasterizerState: ScissorRasterizer);

        foreach (var entry in EntryControls)
            if (entry.Visible)
                entry.Draw(spriteBatch);

        spriteBatch.End();

        device.ScissorRectangle = prevScissor;
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
    }

    public string? GetEntryName(int index)
    {
        if ((index < 0) || (index >= Entries.Count))
            return null;

        return Entries[index].OriginalName;
    }

    public byte? GetEntrySlot(int index)
    {
        if ((index < 0) || (index >= Entries.Count))
            return null;

        return Entries[index].Slot;
    }

    public override void Hide()
    {
        foreach (var control in EntryControls)
        {
            Children.Remove(control);
            control.Dispose();
        }

        EntryControls.Clear();
        Entries.Clear();
        ScrollOffset = 0;
        SelectedIndex = -1;
        HoveredIndex = -1;
        Visible = false;
    }

    private void LayoutEntries()
    {
        var firstEntry = ScrollOffset * COLUMN_COUNT;
        var controlIndex = 0;

        for (var row = 0; row < DISPLAY_ROWS; row++)
        {
            for (var col = 0; col < COLUMN_COUNT; col++)
            {
                if (controlIndex >= EntryControls.Count)
                    return;

                var control = EntryControls[controlIndex];
                var entryIndex = firstEntry + row * COLUMN_COUNT + col;

                if (entryIndex < Entries.Count)
                {
                    var entry = Entries[entryIndex];
                    var isSelected = entryIndex == SelectedIndex;

                    control.X = CONTENT_X + col * ColumnWidth;
                    control.Y = CONTENT_Y + row * ROW_HEIGHT;
                    control.SetEntry(entry.Icon, entry.DisplayName, isSelected);
                    control.Visible = true;
                } else
                {
                    control.ClearEntry();
                    control.Visible = false;
                }

                controlIndex++;
            }
        }

        // Hide remaining controls
        for (; controlIndex < EntryControls.Count; controlIndex++)
        {
            EntryControls[controlIndex]
                .ClearEntry();
            EntryControls[controlIndex].Visible = false;
        }
    }

    public event Action? OnClose;
    public event Action<int>? OnItemSelected;

    private void PopulatePlayerItems(DisplayMenuArgs args)
    {
        if (args.Slots is null)
            return;

        var renderer = UiRenderer.Instance!;

        foreach (var slot in args.Slots)
        {
            ref readonly var slotData = ref WorldState.Inventory.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = renderer.GetItemIcon(slotData.Sprite);
            AddEntry(slotData.Name ?? string.Empty, slot, icon);
        }
    }

    private void PopulatePlayerSkills()
    {
        var renderer = UiRenderer.Instance!;

        for (byte slot = 1; slot <= SkillBook.MAX_SLOTS; slot++)
        {
            ref readonly var slotData = ref WorldState.SkillBook.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = renderer.GetSkillIcon(slotData.Sprite);
            AddEntry(slotData.Name ?? string.Empty, slot, icon);
        }
    }

    private void PopulatePlayerSpells()
    {
        var renderer = UiRenderer.Instance!;

        for (byte slot = 1; slot <= SpellBook.MAX_SLOTS; slot++)
        {
            ref readonly var slotData = ref WorldState.SpellBook.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = renderer.GetSpellIcon(slotData.Sprite);
            AddEntry(slotData.Name ?? string.Empty, slot, icon);
        }
    }

    private void PopulateSkills(DisplayMenuArgs args)
    {
        if (args.Items is null)
            return;

        var renderer = UiRenderer.Instance!;

        foreach (var item in args.Items)
        {
            var icon = renderer.GetSkillIcon(item.Sprite);
            AddEntry(item.Name, item.Slot, icon);
        }
    }

    private void PopulateSpells(DisplayMenuArgs args)
    {
        if (args.Items is null)
            return;

        var renderer = UiRenderer.Instance!;

        foreach (var item in args.Items)
        {
            var icon = renderer.GetSpellIcon(item.Sprite);
            AddEntry(item.Name, item.Slot, icon);
        }
    }

    private void SetEntryVisibility(bool visible)
    {
        foreach (var entry in EntryControls)
            if (entry.HasData)
                entry.Visible = visible;
    }

    public void ShowList(DisplayMenuArgs args)
    {
        Hide();

        switch (args.MenuType)
        {
            case MenuType.ShowPlayerItems:
                PopulatePlayerItems(args);

                break;

            case MenuType.ShowPlayerSkills:
                PopulatePlayerSkills();

                break;

            case MenuType.ShowPlayerSpells:
                PopulatePlayerSpells();

                break;

            case MenuType.ShowSkills:
                PopulateSkills(args);

                break;

            case MenuType.ShowSpells:
                PopulateSpells(args);

                break;
        }

        // Column width based on scrollbar presence
        var needsScroll = TotalRows > MAX_VISIBLE_ROWS;
        var availableWidth = needsScroll ? CONTENT_WIDTH - ScrollBarControl.DEFAULT_WIDTH : CONTENT_WIDTH;
        ColumnWidth = availableWidth / COLUMN_COUNT;

        // Fixed panel size
        var contentHeight = MAX_VISIBLE_ROWS * ROW_HEIGHT;
        Height = CONTENT_Y + contentHeight + BORDER_BOTTOM;
        Width = PANEL_WIDTH;

        // Right-aligned, bottom-anchored above dialog bar
        X = ChaosGame.VIRTUAL_WIDTH - Width;
        Y = BOTTOM_ANCHOR_Y - Height;

        // OK button positioning
        if (OkButton is not null)
        {
            OkButton.X = Width - BTN_WIDTH - 20;
            OkButton.Y = Height - BTN_HEIGHT - 3;
            OkButton.Enabled = false;
        }

        // Scrollbar
        ScrollBar.Visible = needsScroll;
        ScrollBar.Enabled = needsScroll;
        ScrollBar.Y = CONTENT_Y;
        ScrollBar.Height = contentHeight + 2;

        if (needsScroll)
        {
            ScrollBar.TotalItems = TotalRows;
            ScrollBar.VisibleItems = MAX_VISIBLE_ROWS;
            ScrollBar.MaxValue = TotalRows - MAX_VISIBLE_ROWS;
            ScrollBar.Value = 0;
        }

        // Create entry controls for visible slots (including peek row)
        var visibleCount = Math.Min(DISPLAY_ROWS * COLUMN_COUNT, Entries.Count);

        for (var i = 0; i < visibleCount; i++)
        {
            var control = new ListEntryControl(ColumnWidth)
            {
                Name = $"Entry_{i}"
            };

            EntryControls.Add(control);
            AddChild(control);
        }

        LayoutEntries();
        Visible = true;
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

        base.Update(gameTime, input);

        // Hit-test cells
        var sx = ScreenX + CONTENT_X;
        var sy = ScreenY + CONTENT_Y;
        var mx = input.MouseX;
        var my = input.MouseY;

        HoveredIndex = -1;

        if ((mx >= sx) && (mx < (sx + ColumnWidth * COLUMN_COUNT)) && (my >= sy) && (my < (sy + MAX_VISIBLE_ROWS * ROW_HEIGHT)))
        {
            var row = (my - sy) / ROW_HEIGHT;
            var col = (mx - sx) / ColumnWidth;

            if (col is >= 0 and < COLUMN_COUNT)
            {
                var entryIndex = (ScrollOffset + row) * COLUMN_COUNT + col;

                if ((entryIndex >= 0) && (entryIndex < Entries.Count))
                    HoveredIndex = entryIndex;
            }
        }

        if (input.WasLeftButtonPressed && (HoveredIndex >= 0))
        {
            if (HoveredIndex == SelectedIndex)
                OnItemSelected?.Invoke(SelectedIndex);
            else
            {
                SelectedIndex = HoveredIndex;
                UpdateSelectionState();

                if (OkButton is not null)
                    OkButton.Enabled = true;
            }
        }

        // Mouse wheel scrolling
        if (ScrollBar.Visible && (input.ScrollDelta != 0))
        {
            var delta = input.ScrollDelta > 0 ? -1 : 1;
            var newValue = Math.Clamp(ScrollBar.Value + delta, 0, ScrollBar.MaxValue);

            if (newValue != ScrollBar.Value)
            {
                ScrollBar.Value = newValue;
                ScrollOffset = newValue;
                LayoutEntries();
            }
        }
    }

    private void UpdateSelectionState()
    {
        var firstEntry = ScrollOffset * COLUMN_COUNT;

        for (var i = 0; i < EntryControls.Count; i++)
        {
            var entryIndex = firstEntry + i;

            if (entryIndex < Entries.Count)
                EntryControls[i]
                    .SetSelected(entryIndex == SelectedIndex);
        }
    }

    private sealed class ListEntryControl : UIPanel
    {
        private readonly UIImage IconImage;
        private readonly UILabel NameLabel;

        public bool HasData { get; private set; }

        public ListEntryControl(int columnWidth)
        {
            Width = columnWidth;
            Height = ROW_HEIGHT;

            IconImage = new UIImage
            {
                X = 0,
                Y = (ROW_HEIGHT - ICON_SIZE) / 2,
                Width = ICON_SIZE,
                Height = ICON_SIZE
            };

            NameLabel = new UILabel
            {
                X = ICON_SIZE + ICON_TEXT_GAP,
                Y = (ROW_HEIGHT - TextRenderer.CHAR_HEIGHT) / 2,
                Width = columnWidth - ICON_SIZE - ICON_TEXT_GAP,
                Height = TextRenderer.CHAR_HEIGHT,
                ForegroundColor = TextColors.Default
            };

            AddChild(IconImage);
            AddChild(NameLabel);
        }

        public void ClearEntry()
        {
            HasData = false;
            IconImage.Visible = false;
            NameLabel.Text = string.Empty;
        }

        public void SetEntry(Texture2D? icon, string name, bool selected)
        {
            HasData = true;
            IconImage.Texture = icon;
            IconImage.Visible = icon is not null;
            NameLabel.Text = name;
            NameLabel.ForegroundColor = selected ? SELECTED_TEXT_COLOR : TextColors.Default;
        }

        public void SetSelected(bool selected) => NameLabel.ForegroundColor = selected ? SELECTED_TEXT_COLOR : TextColors.Default;

        public override void Update(GameTime gameTime, InputBuffer input) { }
    }

    private sealed record ListEntryData(
        string OriginalName,
        string DisplayName,
        byte Slot,
        Texture2D? Icon);
}