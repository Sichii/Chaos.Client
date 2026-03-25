#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Data.Models;
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
    private int CurrentPage;
    private int DataVersion;
    private int LeftScrollOffset;
    private int RenderedVersion = -1;
    private int RightScrollOffset;

    private Func<EventMetadataEntry, EventState>? StateResolver;

    public SelfProfileEventsTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

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
                DataVersion++;
            });

        RightScrollBar = CreateScrollBar(
            RightRect,
            v =>
            {
                RightScrollOffset = v;
                DataVersion++;
            });

        NextButton = CreateButton("NEXT");
        PrevButton = CreateButton("PREV");

        if (NextButton is not null)
            NextButton.OnClick += () =>
            {
                if (CurrentPage < (MAX_DISPLAY_PAGES - 1))
                {
                    CurrentPage++;
                    LeftScrollOffset = 0;
                    RightScrollOffset = 0;
                    DataVersion++;
                }
            };

        if (PrevButton is not null)
            PrevButton.OnClick += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    LeftScrollOffset = 0;
                    RightScrollOffset = 0;
                    DataVersion++;
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

        StateResolver = null;
        CurrentPage = 0;
        LeftScrollOffset = 0;
        RightScrollOffset = 0;
        LeftScrollBar.TotalItems = 0;
        LeftScrollBar.MaxValue = 0;
        LeftScrollBar.Value = 0;
        RightScrollBar.TotalItems = 0;
        RightScrollBar.MaxValue = 0;
        RightScrollBar.Value = 0;
        DataVersion++;
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

        RefreshRows();

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

    private static void RefreshColumn(
        EventEntryControl[] rows,
        IReadOnlyList<EventMetadataEntry> entries,
        int scrollOffset,
        Func<EventMetadataEntry, EventState>? stateResolver)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var entryIndex = scrollOffset + i;

            if (entryIndex < entries.Count)
            {
                var entry = entries[entryIndex];
                var state = stateResolver?.Invoke(entry) ?? EventState.Unavailable;

                rows[i]
                    .SetEntry(entry, state);
            } else
                rows[i]
                    .Clear();
        }
    }

    private void RefreshRows()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;
        UpdateScrollBars();

        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;

        var leftEntries = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot] : [];
        var rightEntries = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot] : [];

        RefreshColumn(
            LeftRows,
            leftEntries,
            LeftScrollOffset,
            StateResolver);

        RefreshColumn(
            RightRows,
            rightEntries,
            RightScrollOffset,
            StateResolver);
    }

    /// <summary>
    ///     Sets the event entries from parsed metadata, distributed into display slots.
    /// </summary>
    public void SetEvents(IReadOnlyList<EventMetadataEntry> events, Func<EventMetadataEntry, EventState> stateResolver)
    {
        StateResolver = stateResolver;

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
        DataVersion++;
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

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Update click clip bounds so peek rows ignore clicks in the clipped area
        var sx = ScreenX;
        var sy = ScreenY;

        var leftClip = new Rectangle(
            sx + LeftRect.X,
            sy + LeftRect.Y,
            LeftRect.Width,
            LeftRect.Height);

        var rightClip = new Rectangle(
            sx + RightRect.X,
            sy + RightRect.Y,
            RightRect.Width,
            RightRect.Height);

        foreach (var row in LeftRows)
            row.ClickClipBounds = leftClip;

        foreach (var row in RightRows)
            row.ClickClipBounds = rightClip;

        base.Update(gameTime, input);

        // Scroll wheel — determine which column based on mouse position
        if (input.ScrollDelta != 0)
        {
            var mx = input.MouseX - ScreenX;

            var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
            var rightSlot = leftSlot + 1;
            var leftCount = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot].Count : 0;
            var rightCount = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot].Count : 0;

            if ((mx >= LeftRect.X) && (mx < (LeftRect.X + LeftRect.Width)) && (leftCount > MAX_VISIBLE_ROWS))
            {
                LeftScrollOffset = Math.Clamp(LeftScrollOffset - input.ScrollDelta, 0, leftCount - MAX_VISIBLE_ROWS);
                LeftScrollBar.Value = LeftScrollOffset;
                DataVersion++;
            } else if ((mx >= RightRect.X) && (mx < (RightRect.X + RightRect.Width)) && (rightCount > MAX_VISIBLE_ROWS))
            {
                RightScrollOffset = Math.Clamp(RightScrollOffset - input.ScrollDelta, 0, rightCount - MAX_VISIBLE_ROWS);
                RightScrollBar.Value = RightScrollOffset;
                DataVersion++;
            }
        }
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