#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Models;
using Chaos.Client.Utilities;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.WorldList;

/// <summary>
///     Online users list panel loaded from _nusers prefab.
///     Right-aligned, slides in from the right edge of the viewport.
///     Shows a scrollable user list and 9 class filter tabs.
/// </summary>
public sealed class WorldListControl : PrefabPanel
{
    private const int ROW_HEIGHT = 12;
    private const int TAB_COUNT = 9;

    private const int STATUS_ICON_COUNT = 8;
    private readonly List<WorldListEntry> FilterBuffer = [];

    private readonly int MaxVisibleRows;
    private readonly WorldListEntryControl[] RowEntries;
    private readonly ScrollBarControl ScrollBar;
    private readonly Texture2D?[] StatusIcons = new Texture2D?[STATUS_ICON_COUNT];

    // Tab buttons
    private readonly UIButton[] TabButtons = new UIButton[TAB_COUNT];
    private readonly UILabel[] TabCountLabels = new UILabel[TAB_COUNT];
    private readonly UILabel TotalNumLabel;

    private readonly Rectangle UsersListRect;

    private int ActiveTab;

    // Player data
    private IReadOnlyList<WorldListEntry> AllEntries = [];
    private IReadOnlyList<WorldListEntry> FilteredEntries = [];
    private bool RowsDirty;
    private int ScrollOffset;

    // Slide animation
    private SlideAnimator Slide;
    private ushort TotalOnline;

    private static string PlayerName => WorldState.PlayerName;

    public WorldListControl()
        : base("_nusers", false)
    {
        Name = "WorldList";
        Visible = false;

        // Position: right-aligned, starts off-screen
        Slide.SetViewportBounds(
            new Rectangle(
                0,
                0,
                640,
                480),
            Width);
        X = Slide.OffScreenX;

        // UsersList rect
        UsersListRect = GetRect("UsersList");
        MaxVisibleRows = UsersListRect.Height > 0 ? UsersListRect.Height / ROW_HEIGHT : 0;

        // Scrollbar
        ScrollBar = new ScrollBarControl
        {
            Name = "ScrollBar",
            X = UsersListRect.X + UsersListRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = UsersListRect.Y,
            Height = UsersListRect.Height
        };

        ScrollBar.OnValueChanged += v =>
        {
            ScrollOffset = v;
            RowsDirty = true;
        };
        AddChild(ScrollBar);

        // Count labels
        var totalNumRect = GetRect("TotalNum");
        var countryNumRect = GetRect("CountryNum");

        TotalNumLabel = new UILabel
        {
            Name = "TotalNumLabel",
            X = totalNumRect.X,
            Y = totalNumRect.Y,
            Width = totalNumRect.Width,
            Height = totalNumRect.Height,
            Alignment = TextAlignment.Right,
            PaddingLeft = 0,
            PaddingTop = 0
        };

        TotalNumLabel.ForegroundColor = Color.White;
        TotalNumLabel.Text = "0";
        AddChild(TotalNumLabel);

        // Row entries
        var rowWidth = UsersListRect.Width - ScrollBarControl.DEFAULT_WIDTH - 5;
        RowEntries = new WorldListEntryControl[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowEntries[i] = new WorldListEntryControl(rowWidth)
            {
                Name = $"Row{i}",
                X = UsersListRect.X,
                Y = UsersListRect.Y + i * ROW_HEIGHT,
                Width = rowWidth,
                Visible = false
            };

            AddChild(RowEntries[i]);
        }

        // Social status icons from _nemots.spf (frame 0 of each 3-frame group)
        LoadStatusIcons();

        // Tab buttons — built from _nusersb.spf frames (9 tabs x 2 states)
        var countryBtnRect = GetRect("CountryBtn");
        var masterBtnRect = GetRect("MasterBtn");

        // Spacing derived from the Y gap between the first two prefab buttons
        var tabStride = masterBtnRect.Y - countryBtnRect.Y;

        if (tabStride <= 0)
            tabStride = 22;

        // Label stride derived from the Y gap between first two prefab labels (same stride)
        var labelStride = tabStride;

        var cache = UiRenderer.Instance!;

        for (var i = 0; i < TAB_COUNT; i++)
        {
            var tabIndex = i;
            var normalIdx = i * 2;
            var activeIdx = i * 2 + 1;

            TabButtons[i] = new UIButton
            {
                Name = $"Tab{i}",
                X = countryBtnRect.X,
                Y = countryBtnRect.Y + i * tabStride,
                Width = countryBtnRect.Width,
                Height = countryBtnRect.Height,
                NormalTexture = cache.GetSpfTexture("_nusersb.spf", normalIdx),
                SelectedTexture = cache.GetSpfTexture("_nusersb.spf", activeIdx)
            };

            TabButtons[i].OnClick += () => SelectTab(tabIndex);
            AddChild(TabButtons[i]);

            TabCountLabels[i] = new UILabel
            {
                Name = $"TabCount{i}",
                X = countryNumRect.X,
                Y = countryNumRect.Y + i * labelStride,
                Width = countryNumRect.Width,
                Height = countryNumRect.Height,
                Alignment = TextAlignment.Right,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            TabCountLabels[i].ForegroundColor = Color.White;
            AddChild(TabCountLabels[i]);
        }

        TabButtons[0].IsSelected = true;

        // Close button
        var closeButton = CreateButton("Close");

        if (closeButton is not null)
            closeButton.OnClick += () => Slide.SlideOut();

        WorldState.WorldList.Changed += OnWorldListChanged;
    }

    private void ApplyFilter()
    {
        if (ActiveTab == 0)
        {
            FilteredEntries = AllEntries;
        } else
        {
            FilterBuffer.Clear();

            foreach (var e in AllEntries)
            {
                var match = ActiveTab switch
                {
                    1 => e.IsMaster,
                    2 => e.BaseClass == BaseClass.Warrior,
                    3 => e.BaseClass == BaseClass.Rogue,
                    4 => e.BaseClass == BaseClass.Wizard,
                    5 => e.BaseClass == BaseClass.Priest,
                    6 => e.BaseClass == BaseClass.Monk,
                    7 => e.BaseClass == BaseClass.Peasant,
                    8 => e.IsGuilded,
                    _ => false
                };

                if (match)
                    FilterBuffer.Add(e);
            }

            FilteredEntries = FilterBuffer;
        }

        ScrollBar.TotalItems = FilteredEntries.Count;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, FilteredEntries.Count - MaxVisibleRows);
        ScrollBar.Value = ScrollOffset;
    }

    private void AutoScrollToSelf()
    {
        if ((PlayerName.Length == 0) || (MaxVisibleRows <= 0))
            return;

        for (var i = 0; i < FilteredEntries.Count; i++)
            if (FilteredEntries[i]
                .Name
                .EqualsI(PlayerName))
            {
                ScrollOffset = Math.Max(0, i - MaxVisibleRows / 2);

                if (FilteredEntries.Count > MaxVisibleRows)
                    ScrollOffset = Math.Min(ScrollOffset, FilteredEntries.Count - MaxVisibleRows);

                return;
            }
    }

    public override void Dispose()
    {
        WorldState.WorldList.Changed -= OnWorldListChanged;

        foreach (var icon in StatusIcons)
            icon?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    public override void Hide() => Slide.Hide(this);

    private void LoadStatusIcons()
    {
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < STATUS_ICON_COUNT; i++)
            StatusIcons[i] = cache.GetSpfTexture("_nemots.spf", i);
    }

    private static Color MapWorldListColor(WorldListColor color)
    {
        if (color == WorldListColor.Invisble)
            return Color.Transparent;

        if (color == WorldListColor.White)
            return LegendColors.White;

        return LegendColors.Get((int)color);
    }

    public event Action? OnClose;

    private void OnWorldListChanged() => Show(WorldState.WorldList.Entries, WorldState.WorldList.TotalOnline);

    private void RefreshRowEntries()
    {
        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (entryIndex < FilteredEntries.Count)
            {
                var entry = FilteredEntries[entryIndex];
                var nameColor = MapWorldListColor(entry.Color);
                var statusIdx = (int)entry.SocialStatus;
                var statusIcon = (statusIdx >= 0) && (statusIdx < StatusIcons.Length) ? StatusIcons[statusIdx] : null;

                RowEntries[i]
                    .SetEntry(entry, statusIcon, nameColor);
            } else
                RowEntries[i]
                    .Clear();
        }
    }

    private void SelectTab(int tab)
    {
        TabButtons[ActiveTab].IsSelected = false;
        ActiveTab = tab;
        TabButtons[ActiveTab].IsSelected = true;
        ScrollOffset = 0;
        ApplyFilter();
        UpdateCountLabels();
        RowsDirty = true;
    }

    public void SetViewportBounds(Rectangle viewport)
    {
        Slide.SetViewportBounds(viewport, Width);
        Y = viewport.Y;
    }

    public void Show(IReadOnlyList<WorldListEntry> entries, ushort totalOnline)
    {
        AllEntries = entries;
        TotalOnline = totalOnline;
        ActiveTab = 0;
        TabButtons[0].IsSelected = true;
        ScrollOffset = 0;

        ApplyFilter();
        UpdateCountLabels();
        AutoScrollToSelf();
        ScrollBar.Value = ScrollOffset;
        RowsDirty = true;

        if (!Visible)
            Slide.SlideIn(this);
    }

    public void SlideClose() => Slide.SlideOut();

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        if (input.WasKeyPressed(Keys.Escape))
        {
            Slide.SlideOut();

            return;
        }

        if (RowsDirty)
        {
            RefreshRowEntries();
            RowsDirty = false;
        }

        base.Update(gameTime, input);

        if ((input.ScrollDelta != 0) && (FilteredEntries.Count > MaxVisibleRows))
        {
            var scrollLines = Math.Sign(input.ScrollDelta) * Math.Max(1, Math.Abs(input.ScrollDelta) / 120);
            ScrollOffset = Math.Clamp(ScrollOffset - scrollLines, 0, FilteredEntries.Count - MaxVisibleRows);
            ScrollBar.Value = ScrollOffset;
            RowsDirty = true;
        }
    }

    private void UpdateCountLabels()
    {
        TotalNumLabel.Text = $"{TotalOnline}";
        var counts = new int[TAB_COUNT];
        counts[0] = AllEntries.Count;

        foreach (var entry in AllEntries)
        {
            if (entry.IsMaster)
                counts[1]++;

            if (entry.IsGuilded)
                counts[8]++;

            switch (entry.BaseClass)
            {
                case BaseClass.Warrior:
                    counts[2]++;

                    break;
                case BaseClass.Rogue:
                    counts[3]++;

                    break;
                case BaseClass.Wizard:
                    counts[4]++;

                    break;
                case BaseClass.Priest:
                    counts[5]++;

                    break;
                case BaseClass.Monk:
                    counts[6]++;

                    break;
                case BaseClass.Peasant:
                    counts[7]++;

                    break;
                default:
                    continue;
            }
        }

        for (var i = 0; i < TAB_COUNT; i++)
            TabCountLabels[i].Text = $"{counts[i]}";
    }
}