#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Online users list panel loaded from _nusers prefab. Right-aligned, slides in from the right edge of the viewport.
///     Shows a scrollable user list (left side) and 9 class filter tabs (right side). UsersList rect: scrollable list of
///     player entries, 12px per row. Tab buttons: All, Masters, Warrior, Rogue, Wizard, Priest, Monk, Peasant, Guilded.
/// </summary>
public class WorldListControl : UIPanel
{
    private const float SLIDE_DURATION_MS = 250f;
    private const int ROW_HEIGHT = 12;
    private const int CHAR_WIDTH = 6;
    private const int NAME_MAX_CHARS = 21;
    private const int TITLE_OFFSET_X = 134;
    private const int TITLE_MAX_CHARS = 15;
    private const int CLASS_ICON_OFFSET_X = 227;
    private const int SCROLLBAR_WIDTH = 16;
    private const int TAB_COUNT = 9;

    // Class emoticon icons from emot001.epf (indexed by BaseClass)
    private readonly Texture2D?[] ClassIcons = new Texture2D?[8];
    private readonly Rectangle CountryNumRect;
    private readonly CachedText CountryNumText;

    private readonly GraphicsDevice Device;

    // Scroll state
    private readonly ScrollBar ScrollBar;

    // Tab buttons on right side (All, Masters, Warrior..Peasant, Guilded)
    private readonly UIButton[] TabButtons = new UIButton[TAB_COUNT];
    private readonly CachedText[] TabCountCaches = new CachedText[TAB_COUNT];
    private readonly Rectangle TotalNumRect;
    private readonly CachedText TotalNumText;
    private readonly Rectangle UsersListRect;
    private int ActiveTab;

    // Player data
    private List<WorldListEntry> AllEntries = [];
    private List<WorldListEntry> FilteredEntries = [];
    private readonly int MaxVisibleRows;
    private int OffScreenX;

    // Player name for auto-scroll to self
    private string PlayerName = string.Empty;

    // Row text caches (reused per frame)
    private readonly CachedText[] RowNameCaches = [];
    private readonly CachedText[] RowTitleCaches = [];
    private int ScrollOffset;
    private float SlideTimer;
    private bool Sliding;
    private bool SlidingOut;

    // Slide animation
    private int TargetX;
    private ushort TotalOnline;

    public WorldListControl(GraphicsDevice device)
    {
        Device = device;
        Name = "WorldList";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_nusers");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _nusers control prefab set");

        // Anchor — panel dimensions and background
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;
        TargetX = 640 - Width;
        OffScreenX = 640;
        X = OffScreenX;
        Y = (int)anchorRect.Top;

        if (anchor.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);

        // UsersList — scrollable player list area
        UsersListRect = PrefabPanel.GetRect(prefabSet, "UsersList");
        MaxVisibleRows = UsersListRect.Height > 0 ? UsersListRect.Height / ROW_HEIGHT : 0;

        // Scrollbar on the right edge of UsersList
        ScrollBar = new ScrollBar(device)
        {
            Name = "ScrollBar",
            X = UsersListRect.X + UsersListRect.Width - SCROLLBAR_WIDTH,
            Y = UsersListRect.Y,
            Width = SCROLLBAR_WIDTH,
            Height = UsersListRect.Height
        };

        ScrollBar.OnValueChanged += v => ScrollOffset = v;
        AddChild(ScrollBar);

        // Allocate row text caches
        RowNameCaches = new CachedText[MaxVisibleRows];
        RowTitleCaches = new CachedText[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowNameCaches[i] = new CachedText(device);
            RowTitleCaches[i] = new CachedText(device);
        }

        // TotalNum and CountryNum — count displays
        TotalNumRect = PrefabPanel.GetRect(prefabSet, "TotalNum");
        CountryNumRect = PrefabPanel.GetRect(prefabSet, "CountryNum");

        TotalNumText = new CachedText(device)
        {
            Alignment = TextAlignment.Right
        };

        CountryNumText = new CachedText(device)
        {
            Alignment = TextAlignment.Right
        };
        TotalNumText.Update("0", 0, Color.White);
        CountryNumText.Update("0", 0, Color.White);

        // Tab buttons — 9 rows starting at CountryBtn, spaced by (MasterBtn.Top - CountryBtn.Top)
        // _nusersb.spf frames: each tab has 2 frames (normal, active) — tab i uses frames i*2 and i*2+1
        // CountryBtn prefab has frames 0-1, MasterBtn has frames 2-3, etc.
        // We collect all frames from both prefabs into one list to index by tab
        var countryPrefab = prefabSet.Contains("CountryBtn") ? prefabSet["CountryBtn"] : null;
        var masterPrefab = prefabSet.Contains("MasterBtn") ? prefabSet["MasterBtn"] : null;
        var countryBtnRect = PrefabPanel.GetRect(prefabSet, "CountryBtn");
        var masterBtnRect = PrefabPanel.GetRect(prefabSet, "MasterBtn");
        var tabSpacing = masterBtnRect.Y - countryBtnRect.Y;

        if (tabSpacing <= 0)
            tabSpacing = 22;

        // Load all 18 tab button frames from _nusersb.spf directly (9 tabs x 2 states)
        // Tab i: normal = frame i*2, active = frame i*2+1
        var tabFrames = TextureConverter.LoadSpfTextures(device, "_nusersb.spf");

        for (var i = 0; i < TAB_COUNT; i++)
        {
            var tabIndex = i;
            var normalIdx = i * 2;
            var activeIdx = i * 2 + 1;

            TabButtons[i] = new UIButton
            {
                Name = $"Tab{i}",
                X = countryBtnRect.X,
                Y = countryBtnRect.Y + i * tabSpacing,
                Width = countryBtnRect.Width,
                Height = tabSpacing,
                NormalTexture = normalIdx < tabFrames.Length ? tabFrames[normalIdx] : null,
                SelectedTexture = activeIdx < tabFrames.Length ? tabFrames[activeIdx] : null
            };

            TabButtons[i].OnClick += () => SelectTab(tabIndex);
            AddChild(TabButtons[i]);

            TabCountCaches[i] = new CachedText(device)
            {
                Alignment = TextAlignment.Right
            };
        }

        // First tab selected by default
        TabButtons[0].IsSelected = true;

        // Close button
        if (prefabSet.Contains("Close"))
        {
            var closePrefab = prefabSet["Close"];
            var closeRect = closePrefab.Control.Rect!.Value;

            var closeButton = new UIButton
            {
                Name = "Close",
                X = (int)closeRect.Left,
                Y = (int)closeRect.Top,
                Width = (int)closeRect.Width,
                Height = (int)closeRect.Height,
                NormalTexture = closePrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, closePrefab.Images[0]) : null,
                PressedTexture = closePrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, closePrefab.Images[1]) : null
            };

            closeButton.OnClick += SlideOut;
            AddChild(closeButton);
        }

        // Load class emoticon icons from emot001.epf
        LoadClassIcons(device, prefabSet);
    }

    private void ApplyFilter()
    {
        FilteredEntries = ActiveTab switch
        {
            0 => AllEntries,
            1 => AllEntries.Where(e => e.IsMaster)
                           .ToList(),
            2 => AllEntries.Where(e => e.BaseClass == BaseClass.Warrior)
                           .ToList(),
            3 => AllEntries.Where(e => e.BaseClass == BaseClass.Rogue)
                           .ToList(),
            4 => AllEntries.Where(e => e.BaseClass == BaseClass.Wizard)
                           .ToList(),
            5 => AllEntries.Where(e => e.BaseClass == BaseClass.Priest)
                           .ToList(),
            6 => AllEntries.Where(e => e.BaseClass == BaseClass.Monk)
                           .ToList(),
            7 => AllEntries.Where(e => e.BaseClass == BaseClass.Peasant)
                           .ToList(),
            8 => AllEntries.Where(e => e.IsGuilded)
                           .ToList(),
            _ => AllEntries
        };

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
        foreach (var cache in RowNameCaches)
            cache.Dispose();

        foreach (var cache in RowTitleCaches)
            cache.Dispose();

        foreach (var cache in TabCountCaches)
            cache.Dispose();

        TotalNumText.Dispose();
        CountryNumText.Dispose();

        foreach (var icon in ClassIcons)
            icon?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        // Draw total and filtered counts
        TotalNumText.Draw(
            spriteBatch,
            new Rectangle(
                sx + TotalNumRect.X,
                sy + TotalNumRect.Y,
                TotalNumRect.Width,
                TotalNumRect.Height));

        CountryNumText.Draw(
            spriteBatch,
            new Rectangle(
                sx + CountryNumRect.X,
                sy + CountryNumRect.Y,
                CountryNumRect.Width,
                CountryNumRect.Height));

        // Draw per-tab counts next to tab buttons
        for (var i = 0; i < TAB_COUNT; i++)
        {
            var btn = TabButtons[i];

            TabCountCaches[i]
                .Draw(
                    spriteBatch,
                    new Rectangle(
                        sx + CountryNumRect.X,
                        sy + btn.Y,
                        CountryNumRect.Width,
                        btn.Height));
        }

        // Draw player rows in UsersList area
        var listX = ScreenX + UsersListRect.X;
        var listY = ScreenY + UsersListRect.Y;

        RefreshRowCaches();

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (entryIndex >= FilteredEntries.Count)
                break;

            var rowY = listY + i * ROW_HEIGHT;

            // Title — left column, right-justified within first 126px
            RowTitleCaches[i]
                .Draw(
                    spriteBatch,
                    new Rectangle(
                        listX,
                        rowY,
                        NAME_MAX_CHARS * CHAR_WIDTH,
                        ROW_HEIGHT));

            // Name — right column at +134px, right-justified within space before class icon
            var nameWidth = CLASS_ICON_OFFSET_X - TITLE_OFFSET_X - 2;

            RowNameCaches[i]
                .Draw(
                    spriteBatch,
                    new Rectangle(
                        listX + TITLE_OFFSET_X,
                        rowY,
                        nameWidth,
                        ROW_HEIGHT));

            // Class icon at +227px
            var entry = FilteredEntries[entryIndex];
            var iconIndex = GetClassIconIndex(entry.BaseClass);

            if ((iconIndex >= 0) && (iconIndex < ClassIcons.Length) && ClassIcons[iconIndex] is { } classIcon)
                spriteBatch.Draw(classIcon, new Vector2(listX + CLASS_ICON_OFFSET_X, rowY), Color.White);
        }
    }

    private static int GetClassIconIndex(BaseClass baseClass) => (int)baseClass;

    private int[] GetTabCounts()
    {
        var counts = new int[TAB_COUNT];
        counts[0] = AllEntries.Count;
        counts[1] = AllEntries.Count(e => e.IsMaster);
        counts[2] = AllEntries.Count(e => e.BaseClass == BaseClass.Warrior);
        counts[3] = AllEntries.Count(e => e.BaseClass == BaseClass.Rogue);
        counts[4] = AllEntries.Count(e => e.BaseClass == BaseClass.Wizard);
        counts[5] = AllEntries.Count(e => e.BaseClass == BaseClass.Priest);
        counts[6] = AllEntries.Count(e => e.BaseClass == BaseClass.Monk);
        counts[7] = AllEntries.Count(e => e.BaseClass == BaseClass.Peasant);
        counts[8] = AllEntries.Count(e => e.IsGuilded);

        return counts;
    }

    public void Hide()
    {
        Visible = false;
        Sliding = false;
        X = OffScreenX;
    }

    private void LoadClassIcons(GraphicsDevice device, ControlPrefabSet prefabSet)
    {
        if (!prefabSet.Contains("Emoticon"))
            return;

        var emotPrefab = prefabSet["Emoticon"];

        // The Emoticon control has emot001.epf frames
        // RE mapping: Peasant(0)=7, Warrior(1)=2, Rogue(2)=3, Wizard(3)=4, Priest(4)=5, Monk(5)=6
        int[] classToFrame =
        [
            7,
            2,
            3,
            4,
            5,
            6,
            0,
            1
        ];

        for (var i = 0; (i < classToFrame.Length) && (i < ClassIcons.Length); i++)
        {
            var frameIdx = classToFrame[i];

            if (frameIdx < emotPrefab.Images.Count)
                ClassIcons[i] = TextureConverter.ToTexture2D(device, emotPrefab.Images[frameIdx]);
        }
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

    private void RefreshRowCaches()
    {
        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (entryIndex < FilteredEntries.Count)
            {
                var entry = FilteredEntries[entryIndex];
                var nameColor = MapWorldListColor(entry.Color);
                RowNameCaches[i].Alignment = TextAlignment.Right;

                RowNameCaches[i]
                    .Update(entry.Name, 0, nameColor);
                RowTitleCaches[i].Alignment = TextAlignment.Right;

                RowTitleCaches[i]
                    .Update(entry.Title ?? string.Empty, 0, Color.White);
            } else
            {
                RowNameCaches[i]
                    .Update(string.Empty, 0, Color.White);

                RowTitleCaches[i]
                    .Update(string.Empty, 0, Color.White);
            }
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
    }

    /// <summary>
    ///     Sets the right-aligned target position based on the HUD viewport.
    /// </summary>
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

        // Slide animation
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

        base.Update(gameTime, input);

        // Scroll wheel
        if ((input.ScrollDelta != 0) && (FilteredEntries.Count > MaxVisibleRows))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, FilteredEntries.Count - MaxVisibleRows);

            ScrollBar.Value = ScrollOffset;
        }
    }

    private void UpdateCountLabels()
    {
        TotalNumText.Update($"{TotalOnline}", 0, Color.White);
        CountryNumText.Update($"{FilteredEntries.Count}", 0, Color.White);

        // Per-tab counts
        var tabFilters = GetTabCounts();

        for (var i = 0; i < TAB_COUNT; i++)
            TabCountCaches[i]
                .Update($"{tabFilters[i]}", 0, Color.White);
    }
}

/// <summary>
///     A single entry in the online users list.
/// </summary>
public record WorldListEntry(
    string Name,
    string? Title,
    BaseClass BaseClass,
    bool IsMaster,
    bool IsGuilded,
    WorldListColor Color);