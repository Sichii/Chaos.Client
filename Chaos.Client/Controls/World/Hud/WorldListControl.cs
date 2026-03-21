#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Definitions;
using Chaos.Client.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Hud;

/// <summary>
///     Online users list panel loaded from _nusers prefab.
///     Right-aligned, slides in from the right edge of the viewport.
///     Shows a scrollable user list and 9 class filter tabs.
/// </summary>
public sealed class WorldListControl : PrefabPanel
{
    private const float SLIDE_DURATION_MS = 250f;
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
    private List<WorldListEntry> AllEntries = [];
    private List<WorldListEntry> FilteredEntries = [];
    private int OffScreenX;

    private string PlayerName = string.Empty;
    private bool RowsDirty;
    private int ScrollOffset;
    private float SlideTimer;
    private bool Sliding;
    private bool SlidingOut;

    // Slide animation
    private int TargetX;
    private ushort TotalOnline;

    public WorldListControl(GraphicsDevice device)
        : base(device, "_nusers", false)
    {
        Name = "WorldList";
        Visible = false;

        // Position: right-aligned, starts off-screen
        TargetX = 640 - Width;
        OffScreenX = 640;
        X = OffScreenX;

        var elements = AutoPopulate();

        // Hide auto-populated elements that we replace with custom controls
        if (elements.GetValueOrDefault("TotalNum") is { } totalNumElement)
            totalNumElement.Visible = false;

        if (elements.GetValueOrDefault("CountryNum") is { } countryNumElement)
            countryNumElement.Visible = false;

        if (elements.GetValueOrDefault("CountryBtn") is { } countryBtnElement)
            countryBtnElement.Visible = false;

        if (elements.GetValueOrDefault("MasterBtn") is { } masterBtnElement)
            masterBtnElement.Visible = false;

        if (elements.GetValueOrDefault("Emoticon") is { } emoticonElement)
            emoticonElement.Visible = false;

        // UsersList rect
        UsersListRect = GetRect("UsersList");
        MaxVisibleRows = UsersListRect.Height > 0 ? UsersListRect.Height / ROW_HEIGHT : 0;

        // Scrollbar
        ScrollBar = new ScrollBarControl(device)
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

        TotalNumLabel = new UILabel(device)
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

        TotalNumLabel.SetText("0");
        AddChild(TotalNumLabel);

        // Row entries
        var rowWidth = UsersListRect.Width - ScrollBarControl.DEFAULT_WIDTH - 5;
        RowEntries = new WorldListEntryControl[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowEntries[i] = new WorldListEntryControl(device, rowWidth)
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

            TabCountLabels[i] = new UILabel(device)
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

            AddChild(TabCountLabels[i]);
        }

        TabButtons[0].IsSelected = true;

        // Close button — AutoPopulate already created it as a UIButton
        if (elements.GetValueOrDefault("Close") is UIButton closeButton)
            closeButton.OnClick += SlideOut;
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

    public override void Hide()
    {
        Visible = false;
        Sliding = false;
        X = OffScreenX;
    }

    private void LoadStatusIcons()
    {
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < STATUS_ICON_COUNT; i++)
            StatusIcons[i] = cache.GetSpfTexture("_nemots.spf", i);
    }

    private static Color MapWorldListColor(WorldListColor color)
        => color switch
        {
            WorldListColor.LightBlue   => new Color(150, 200, 255),
            WorldListColor.BrightRed   => new Color(255, 80, 80),
            WorldListColor.LightYellow => new Color(255, 255, 150),
            WorldListColor.LightOrange => new Color(255, 200, 100),
            WorldListColor.Yellow      => Color.Yellow,
            WorldListColor.LightGreen  => new Color(150, 255, 150),
            WorldListColor.Blue        => new Color(100, 149, 237),
            WorldListColor.LightPurple => new Color(200, 150, 255),
            WorldListColor.DarkPurple  => new Color(150, 100, 200),
            WorldListColor.Pink        => new Color(255, 150, 200),
            WorldListColor.DarkGreen   => new Color(50, 150, 50),
            WorldListColor.Green       => new Color(100, 255, 100),
            WorldListColor.Orange      => Color.Orange,
            WorldListColor.Brown       => new Color(180, 120, 60),
            WorldListColor.Red         => Color.Red,
            WorldListColor.Black       => new Color(40, 40, 40),
            WorldListColor.Invisble    => Color.Transparent,
            _                          => Color.White
        };

    public event Action? OnClose;

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
        TargetX = viewport.X + viewport.Width - Width;
        OffScreenX = viewport.X + viewport.Width;
        Y = viewport.Y;
    }

    public void Show(List<WorldListEntry> entries, ushort totalOnline, string playerName = "")
    {
        AllEntries = entries;
        TotalOnline = totalOnline;
        PlayerName = playerName;
        ActiveTab = 0;
        ScrollOffset = 0;

        ApplyFilter();
        UpdateCountLabels();
        AutoScrollToSelf();
        RowsDirty = true;

        if (!Visible)
            SlideIn();
    }

    private void SlideIn()
    {
        X = OffScreenX;
        Visible = true;
        Sliding = true;
        SlidingOut = false;
        SlideTimer = 0;
    }

    private void SlideOut()
    {
        Sliding = true;
        SlidingOut = true;
        SlideTimer = 0;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (Sliding)
        {
            SlideTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
            var t = Math.Clamp(SlideTimer / SLIDE_DURATION_MS, 0f, 1f);
            var eased = 1f - (1f - t) * (1f - t);

            if (SlidingOut)
            {
                X = (int)MathHelper.Lerp(TargetX, OffScreenX, eased);

                if (t >= 1f)
                {
                    Hide();
                    OnClose?.Invoke();

                    return;
                }
            } else
            {
                X = (int)MathHelper.Lerp(OffScreenX, TargetX, eased);

                if (t >= 1f)
                {
                    X = TargetX;
                    Sliding = false;
                }
            }
        }

        if (input.WasKeyPressed(Keys.Escape))
        {
            SlideOut();

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
        TotalNumLabel.SetText($"{TotalOnline}");
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
            TabCountLabels[i]
                .SetText($"{counts[i]}");
    }
}