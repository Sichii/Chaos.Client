#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Data.Models;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Events/quest tab page (_nui_ev). Three display pages, each with two columns (EV1 left, EV2 right).
///     Each column corresponds to one SEvent file (circle level). NEXT/PREV buttons cycle through pages.
///     Each row uses the _nui_ski prefab with a leicon.epf state icon (completed/available/unavailable).
///     Layout: Page 0 = SEvent1|SEvent2, Page 1 = SEvent3|SEvent4, Page 2 = SEvent5|SEvent6+7.
/// </summary>
public sealed class SelfProfileEventsTab : PrefabPanel
{
    private const int ROW_HEIGHT = 45;
    private const int MAX_VISIBLE_ROWS = 5;
    private const int DISPLAY_ROWS = MAX_VISIBLE_ROWS + 1;
    private const int MAX_DISPLAY_PAGES = 3;
    private const int COLUMNS_PER_PAGE = 2;
    private const int MAX_DISPLAY_SLOTS = MAX_DISPLAY_PAGES * COLUMNS_PER_PAGE;

    private static readonly RasterizerState ScissorRasterizer = new()
    {
        ScissorTestEnable = true
    };

    // 6 display slots (3 pages x 2 columns), each holding events for that slot
    private readonly List<EventMetadataEntry>[] DisplaySlots = new List<EventMetadataEntry>[MAX_DISPLAY_SLOTS];
    private readonly Rectangle LeftRect;

    private readonly EventEntryControl[] LeftRows;
    private readonly ScrollBarControl LeftScrollBar;
    private readonly UIButton? NextButton;
    private readonly UIButton? PrevButton;
    private readonly Rectangle RightRect;
    private readonly EventEntryControl[] RightRows;
    private readonly ScrollBarControl RightScrollBar;

    private BaseClass BaseClass;
    private HashSet<string> CompletedEventIds = new(StringComparer.OrdinalIgnoreCase);
    private int CurrentPage;
    private bool Dirty = true;
    private bool EnableMasterQuests;
    private int LeftScrollOffset;
    private int RightScrollOffset;

    public SelfProfileEventsTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        VisibilityChanged += visible =>
        {
            if (visible)
                Dirty = true;
        };

        for (var i = 0; i < MAX_DISPLAY_SLOTS; i++)
            DisplaySlots[i] = [];

        LeftRect = GetRect("EV1");
        RightRect = GetRect("EV2");

        if (LeftRect == Rectangle.Empty)
            LeftRect = new Rectangle(
                32,
                33,
                233,
                239);

        if (RightRect == Rectangle.Empty)
            RightRect = new Rectangle(
                331,
                33,
                233,
                239);

        LeftRows = CreateColumn(LeftRect);
        RightRows = CreateColumn(RightRect);

        LeftScrollBar = CreateScrollBar(
            LeftRect,
            v =>
            {
                LeftScrollOffset = v;
                Dirty = true;
            });

        RightScrollBar = CreateScrollBar(
            RightRect,
            v =>
            {
                RightScrollOffset = v;
                Dirty = true;
            });

        NextButton = CreateButton("NEXT");
        PrevButton = CreateButton("PREV");

        if (NextButton is not null)
            NextButton.Clicked += () =>
            {
                if (CurrentPage < (MAX_DISPLAY_PAGES - 1))
                {
                    CurrentPage++;
                    LeftScrollOffset = 0;
                    RightScrollOffset = 0;
                    Dirty = true;
                }
            };

        if (PrevButton is not null)
            PrevButton.Clicked += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    LeftScrollOffset = 0;
                    RightScrollOffset = 0;
                    Dirty = true;
                }
            };
    }

    /// <summary>
    ///     Clears all event entries.
    /// </summary>
    public void ClearAll()
    {
        for (var i = 0; i < MAX_DISPLAY_SLOTS; i++)
            DisplaySlots[i] = [];

        CompletedEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CurrentPage = 0;
        LeftScrollOffset = 0;
        RightScrollOffset = 0;
        LeftScrollBar.TotalItems = 0;
        LeftScrollBar.MaxValue = 0;
        LeftScrollBar.Value = 0;
        RightScrollBar.TotalItems = 0;
        RightScrollBar.MaxValue = 0;
        RightScrollBar.Value = 0;
        Dirty = true;
    }

    private EventEntryControl[] CreateColumn(Rectangle columnRect)
    {
        var rows = new EventEntryControl[DISPLAY_ROWS];

        for (var i = 0; i < DISPLAY_ROWS; i++)
        {
            var row = new EventEntryControl
            {
                X = columnRect.X,
                Y = columnRect.Y + i * ROW_HEIGHT,
                Visible = false
            };

            row.OnClicked += (entry, state) => OnEntryClicked?.Invoke(entry, state);
            AddChild(row);
            rows[i] = row;
        }

        return rows;
    }

    private ScrollBarControl CreateScrollBar(Rectangle columnRect, Action<int> onValueChanged)
    {
        var scrollBar = new ScrollBarControl
        {
            X = columnRect.X + columnRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = columnRect.Y,
            Height = columnRect.Height,
            VisibleItems = MAX_VISIBLE_ROWS
        };

        scrollBar.OnValueChanged += onValueChanged;
        AddChild(scrollBar);

        return scrollBar;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // Hide entry rows so base.Draw() only renders background + scrollbars + buttons
        SetRowVisibilityForBaseDraw(false);
        base.Draw(spriteBatch);
        SetRowVisibilityForBaseDraw(true);

        // Draw each column's rows clipped to the column rect
        var device = spriteBatch.GraphicsDevice;
        var sx = ScreenX;
        var sy = ScreenY;

        DrawClippedColumn(
            spriteBatch,
            device,
            LeftRows,
            new Rectangle(
                sx + LeftRect.X,
                sy + LeftRect.Y,
                LeftRect.Width,
                LeftRect.Height));

        DrawClippedColumn(
            spriteBatch,
            device,
            RightRows,
            new Rectangle(
                sx + RightRect.X,
                sy + RightRect.Y,
                RightRect.Width,
                RightRect.Height));
    }

    private static void DrawClippedColumn(
        SpriteBatch spriteBatch,
        GraphicsDevice device,
        EventEntryControl[] rows,
        Rectangle clipRect)
    {
        spriteBatch.End();

        var prevScissor = device.ScissorRectangle;
        device.ScissorRectangle = clipRect;

        spriteBatch.Begin(samplerState: GlobalSettings.Sampler, rasterizerState: ScissorRasterizer);

        foreach (var row in rows)
            if (row.Visible)
                row.Draw(spriteBatch);

        spriteBatch.End();

        device.ScissorRectangle = prevScissor;
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler);
    }

    /// <summary>
    ///     Fired when any entry row is clicked. Passes the entry and its resolved state.
    /// </summary>
    public event Action<EventMetadataEntry, EventState>? OnEntryClicked;

    private void RefreshColumn(EventEntryControl[] rows, IReadOnlyList<EventMetadataEntry> entries, int scrollOffset)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var entryIndex = scrollOffset + i;

            if (entryIndex < entries.Count)
            {
                var entry = entries[entryIndex];
                var state = ResolveEventState(entry);

                rows[i]
                    .SetEntry(entry, state);
            } else
                rows[i]
                    .Clear();
        }
    }

    private void RefreshRows()
    {
        if (!Dirty)
            return;

        Dirty = false;
        UpdateScrollBars();

        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;

        var leftEntries = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot] : [];
        var rightEntries = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot] : [];

        RefreshColumn(LeftRows, leftEntries, LeftScrollOffset);

        RefreshColumn(RightRows, rightEntries, RightScrollOffset);
    }

    private EventState ResolveEventState(EventMetadataEntry entry)
    {
        // Completed: player has a legend mark with key matching this event's ID
        if (!string.IsNullOrEmpty(entry.Id) && CompletedEventIds.Contains(entry.Id))
            return EventState.Completed;

        var attrs = WorldState.Attributes.Current;
        var playerLevel = attrs?.Level ?? 1;

        // Derive player's circle from level; master flag overrides to circle 6
        var playerCircle = EnableMasterQuests
            ? 6
            : playerLevel switch
            {
                >= 99 => 5,
                >= 71 => 4,
                >= 41 => 3,
                >= 11 => 2,
                _     => 1
            };

        // Check qualifying circles — player's circle must be in the list
        if (!string.IsNullOrEmpty(entry.QualifyingCircles))
        {
            var circleChar = (char)('0' + playerCircle);

            if (!entry.QualifyingCircles.Contains(circleChar))
                return EventState.Unavailable;
        }

        // Check qualifying classes — player's class must be in the list
        if (!string.IsNullOrEmpty(entry.QualifyingClasses))
        {
            var classChar = (char)('0' + (int)BaseClass);

            if (!entry.QualifyingClasses.Contains(classChar))
                return EventState.Unavailable;
        }

        // Check prerequisite event — must be completed
        if (!string.IsNullOrEmpty(entry.PreRequisiteId) && !CompletedEventIds.Contains(entry.PreRequisiteId))
            return EventState.Unavailable;

        return EventState.Available;
    }

    /// <summary>
    ///     Sets the event entries from parsed metadata, distributed into display slots.
    /// </summary>
    public void SetEvents(
        IReadOnlyList<EventMetadataEntry> events,
        HashSet<string> completedEventIds,
        BaseClass baseClass,
        bool enableMasterQuests)
    {
        CompletedEventIds = completedEventIds;
        BaseClass = baseClass;
        EnableMasterQuests = enableMasterQuests;

        for (var i = 0; i < MAX_DISPLAY_SLOTS; i++)
            DisplaySlots[i] = [];

        // Distribute events to display slots: SEvent page → slot = min(page - 1, 5)
        // Slot layout: 0=SEvent1, 1=SEvent2, 2=SEvent3, 3=SEvent4, 4=SEvent5, 5=SEvent6+7
        foreach (var entry in events)
        {
            var slotIndex = Math.Min(entry.Page - 1, MAX_DISPLAY_SLOTS - 1);

            if (slotIndex < 0)
                slotIndex = 0;

            DisplaySlots[slotIndex]
                .Add(entry);
        }

        CurrentPage = 0;
        LeftScrollOffset = 0;
        RightScrollOffset = 0;
        UpdateScrollBars();
        Dirty = true;
    }

    private void SetRowVisibilityForBaseDraw(bool visible)
    {
        foreach (var row in LeftRows)
            if (row.Entry is not null)
                row.Visible = visible;

        foreach (var row in RightRows)
            if (row.Entry is not null)
                row.Visible = visible;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var sx = ScreenX;
        var sy = ScreenY;
        var leftScreenRect = new Rectangle(sx + LeftRect.X, sy + LeftRect.Y, LeftRect.Width, LeftRect.Height);
        var rightScreenRect = new Rectangle(sx + RightRect.X, sy + RightRect.Y, RightRect.Width, RightRect.Height);

        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;
        var leftCount = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot].Count : 0;
        var rightCount = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot].Count : 0;

        if (leftScreenRect.Contains(e.ScreenX, e.ScreenY) && (leftCount > MAX_VISIBLE_ROWS))
        {
            LeftScrollOffset = Math.Clamp(LeftScrollOffset - e.Delta, 0, leftCount - MAX_VISIBLE_ROWS);
            LeftScrollBar.Value = LeftScrollOffset;
            Dirty = true;
            e.Handled = true;
        } else if (rightScreenRect.Contains(e.ScreenX, e.ScreenY) && (rightCount > MAX_VISIBLE_ROWS))
        {
            RightScrollOffset = Math.Clamp(RightScrollOffset - e.Delta, 0, rightCount - MAX_VISIBLE_ROWS);
            RightScrollBar.Value = RightScrollOffset;
            Dirty = true;
            e.Handled = true;
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        RefreshRows();
        base.Update(gameTime);
    }

    private void UpdateScrollBars()
    {
        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;

        var leftCount = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot].Count : 0;
        var rightCount = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot].Count : 0;

        LeftScrollBar.TotalItems = leftCount;
        LeftScrollBar.MaxValue = Math.Max(0, leftCount - MAX_VISIBLE_ROWS);
        LeftScrollBar.Value = Math.Min(LeftScrollOffset, LeftScrollBar.MaxValue);

        RightScrollBar.TotalItems = rightCount;
        RightScrollBar.MaxValue = Math.Max(0, rightCount - MAX_VISIBLE_ROWS);
        RightScrollBar.Value = Math.Min(RightScrollOffset, RightScrollBar.MaxValue);
    }
}