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
    private const int CHAR_WIDTH = 6;
    private const int NAME_MAX_CHARS = 21;
    private const int TITLE_OFFSET_X = 134;
    private const int CLASS_ICON_OFFSET_X = 227;
    private const int SCROLLBAR_WIDTH = 16;
    private const int TAB_COUNT = 9;

    // Class emoticon icons (indexed by BaseClass)
    private readonly Texture2D?[] ClassIcons = new Texture2D?[8];
    private readonly Rectangle CountryNumRect;
    private readonly CachedText CountryNumText;
    private readonly int MaxVisibleRows;

    // Row text caches
    private readonly CachedText[] RowNameCaches;
    private readonly CachedText[] RowTitleCaches;

    // Scroll state
    private readonly ScrollBarControl ScrollBar;

    // Tab buttons
    private readonly UIButton[] TabButtons = new UIButton[TAB_COUNT];
    private readonly CachedText[] TabCountCaches = new CachedText[TAB_COUNT];
    private readonly Rectangle TotalNumRect;
    private readonly CachedText TotalNumText;

    private readonly Rectangle UsersListRect;
    private int ActiveTab;

    // Player data
    private List<WorldListEntry> AllEntries = [];
    private List<WorldListEntry> FilteredEntries = [];
    private int OffScreenX;

    private string PlayerName = string.Empty;
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

        // UsersList rect
        UsersListRect = GetRect("UsersList");
        MaxVisibleRows = UsersListRect.Height > 0 ? UsersListRect.Height / ROW_HEIGHT : 0;

        // Scrollbar
        ScrollBar = new ScrollBarControl(device)
        {
            Name = "ScrollBar",
            X = UsersListRect.X + UsersListRect.Width - SCROLLBAR_WIDTH,
            Y = UsersListRect.Y,
            Width = SCROLLBAR_WIDTH,
            Height = UsersListRect.Height
        };

        ScrollBar.OnValueChanged += v => ScrollOffset = v;
        AddChild(ScrollBar);

        // Row text caches
        RowNameCaches = new CachedText[MaxVisibleRows];
        RowTitleCaches = new CachedText[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowNameCaches[i] = new CachedText(device);
            RowTitleCaches[i] = new CachedText(device);
        }

        // Count displays
        TotalNumRect = GetRect("TotalNum");
        CountryNumRect = GetRect("CountryNum");

        TotalNumText = new CachedText(device)
        {
            Alignment = TextAlignment.Right
        };

        CountryNumText = new CachedText(device)
        {
            Alignment = TextAlignment.Right
        };
        TotalNumText.Update("0", Color.White);
        CountryNumText.Update("0", Color.White);

        // Tab buttons — built from _nusersb.spf frames (9 tabs x 2 states)
        var countryBtnRect = GetRect("CountryBtn");
        var masterBtnRect = GetRect("MasterBtn");
        var tabSpacing = masterBtnRect.Y - countryBtnRect.Y;

        if (tabSpacing <= 0)
            tabSpacing = 22;

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

        TabButtons[0].IsSelected = true;

        // Close button — AutoPopulate already created it as a UIButton
        if (elements.GetValueOrDefault("Close") is UIButton closeButton)
            closeButton.OnClick += SlideOut;

        // Class emoticon icons
        LoadClassIcons(device);
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

        var listX = ScreenX + UsersListRect.X;
        var listY = ScreenY + UsersListRect.Y;

        RefreshRowCaches();

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (entryIndex >= FilteredEntries.Count)
                break;

            var rowY = listY + i * ROW_HEIGHT;

            RowTitleCaches[i]
                .Draw(
                    spriteBatch,
                    new Rectangle(
                        listX,
                        rowY,
                        NAME_MAX_CHARS * CHAR_WIDTH,
                        ROW_HEIGHT));

            var nameWidth = CLASS_ICON_OFFSET_X - TITLE_OFFSET_X - 2;

            RowNameCaches[i]
                .Draw(
                    spriteBatch,
                    new Rectangle(
                        listX + TITLE_OFFSET_X,
                        rowY,
                        nameWidth,
                        ROW_HEIGHT));

            var entry = FilteredEntries[entryIndex];
            var iconIndex = (int)entry.BaseClass;

            if ((iconIndex >= 0) && (iconIndex < ClassIcons.Length) && ClassIcons[iconIndex] is { } classIcon)
                spriteBatch.Draw(classIcon, new Vector2(listX + CLASS_ICON_OFFSET_X, rowY), Color.White);
        }
    }

    public override void Hide()
    {
        Visible = false;
        Sliding = false;
        X = OffScreenX;
    }

    private void LoadClassIcons(GraphicsDevice device)
    {
        if (!PrefabSet.Contains("Emoticon"))
            return;

        var emotPrefab = PrefabSet["Emoticon"];

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
                    .Update(entry.Name, nameColor);
                RowTitleCaches[i].Alignment = TextAlignment.Right;

                RowTitleCaches[i]
                    .Update(entry.Title ?? string.Empty, Color.White);
            } else
            {
                RowNameCaches[i]
                    .Update(string.Empty, Color.White);

                RowTitleCaches[i]
                    .Update(string.Empty, Color.White);
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

        if ((input.ScrollDelta != 0) && (FilteredEntries.Count > MaxVisibleRows))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, FilteredEntries.Count - MaxVisibleRows);
            ScrollBar.Value = ScrollOffset;
        }
    }

    private void UpdateCountLabels()
    {
        TotalNumText.Update($"{TotalOnline}", Color.White);
        CountryNumText.Update($"{FilteredEntries.Count}", Color.White);

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
            TabCountCaches[i]
                .Update($"{counts[i]}", Color.White);
    }
}