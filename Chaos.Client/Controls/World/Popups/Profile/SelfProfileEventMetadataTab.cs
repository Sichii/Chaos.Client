#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Scrolling;
using Chaos.Client.Data.Models;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Events/quest tab page (_nui_ev). Three display pages, each with two columns (EV1 left, EV2 right).
///     Each column corresponds to one SEvent file (circle level). NEXT/PREV buttons cycle through pages.
///     Each row uses the _nui_ski prefab with a leicon.epf state icon (completed/available/unavailable).
///     Layout: Page 0 = SEvent1|SEvent2, Page 1 = SEvent3|SEvent4, Page 2 = SEvent5|SEvent6+7.
/// </summary>
public sealed class SelfProfileEventMetadataTab : PrefabPanel
{
    private const int ROW_HEIGHT = 45;
    private const int MAX_DISPLAY_PAGES = 3;
    private const int COLUMNS_PER_PAGE = 2;
    private const int MAX_DISPLAY_SLOTS = MAX_DISPLAY_PAGES * COLUMNS_PER_PAGE;

    //6 display slots (3 pages x 2 columns), each holding events for that slot
    private readonly List<EventMetadataEntry>[] DisplaySlots = new List<EventMetadataEntry>[MAX_DISPLAY_SLOTS];
    private readonly VirtualizedRowList<EventMetadataEntry> LeftList;
    private readonly UIButton? NextButton;
    private readonly UIButton? PrevButton;
    private readonly VirtualizedRowList<EventMetadataEntry> RightList;

    private BaseClass BaseClass;
    private HashSet<string> CompletedEventIds = new(StringComparer.OrdinalIgnoreCase);
    private int CurrentPage;
    private bool EnableMasterQuests;

    public SelfProfileEventMetadataTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        for (var i = 0; i < MAX_DISPLAY_SLOTS; i++)
            DisplaySlots[i] = [];

        var leftRect = GetRect("EV1");
        var rightRect = GetRect("EV2");

        if (leftRect == Rectangle.Empty)
            leftRect = new Rectangle(
                32,
                33,
                233,
                239);

        if (rightRect == Rectangle.Empty)
            rightRect = new Rectangle(
                331,
                33,
                233,
                239);

        LeftList = CreateColumn(leftRect);
        RightList = CreateColumn(rightRect);

        NextButton = CreateButton("NEXT");
        PrevButton = CreateButton("PREV");

        if (NextButton is not null)
            NextButton.Clicked += () =>
            {
                if (CurrentPage < (MAX_DISPLAY_PAGES - 1))
                {
                    CurrentPage++;
                    SetBackgroundFrame(CurrentPage);
                    ShowCurrentPage();
                }
            };

        if (PrevButton is not null)
            PrevButton.Clicked += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    SetBackgroundFrame(CurrentPage);
                    ShowCurrentPage();
                }
            };

        //event state depends on the player's attrs/completed events, which can change while the tab is hidden, so
        //re-bind both columns whenever the tab becomes visible again.
        VisibilityChanged += visible =>
        {
            if (visible)
            {
                LeftList.Invalidate();
                RightList.Invalidate();
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
        SetBackgroundFrame(0);
        ShowCurrentPage();
    }

    private VirtualizedRowList<EventMetadataEntry> CreateColumn(Rectangle columnRect)
    {
        //overscanRows:1 gives the bottom "peek" row; the generic clips it to the column bounds.
        var list = new VirtualizedRowList<EventMetadataEntry>(
            columnRect.Width,
            columnRect.Height,
            ROW_HEIGHT,
            () =>
            {
                var row = new EventMetadataEntryControl();
                row.OnClicked += (entry, state) => OnEntryClicked?.Invoke(entry, state);

                return row;
            },
            BindRow,
            overscanRows: 1);

        var viewer = new ScrollViewerControl(list)
        {
            X = columnRect.X,
            Y = columnRect.Y,
            Width = columnRect.Width,
            Height = columnRect.Height
        };

        AddChild(viewer);

        return list;
    }

    private void BindRow(UIElement row, EventMetadataEntry entry, bool selected)
        => ((EventMetadataEntryControl)row).SetEntry(entry, ResolveEventState(entry));

    /// <summary>
    ///     Fired when any entry row is clicked. Passes the entry and its resolved state.
    /// </summary>
    public event EventMetadataClickedHandler? OnEntryClicked;

    private EventState ResolveEventState(EventMetadataEntry entry)
    {
        //completed: player has a legend mark with key matching this event's id
        if (!string.IsNullOrEmpty(entry.Id) && CompletedEventIds.Contains(entry.Id))
            return EventState.Completed;

        var attrs = WorldState.Attributes.Current;
        var playerLevel = attrs?.Level ?? 1;

        //derive player's circle from level; master flag overrides to circle 6
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

        //check qualifying circles — player's circle must be in the list
        if (!string.IsNullOrEmpty(entry.QualifyingCircles))
        {
            var circleChar = (char)('0' + playerCircle);

            if (!entry.QualifyingCircles.Contains(circleChar))
                return EventState.Unavailable;
        }

        //check qualifying classes — player's class must be in the list
        if (!string.IsNullOrEmpty(entry.QualifyingClasses))
        {
            var classChar = (char)('0' + (int)BaseClass);

            if (!entry.QualifyingClasses.Contains(classChar))
                return EventState.Unavailable;
        }

        //check prerequisite event — must be completed
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

        //distribute events to display slots: sevent page → slot = min(page - 1, 5)
        //slot layout: 0=sevent1, 1=sevent2, 2=sevent3, 3=sevent4, 4=sevent5, 5=sevent6+7
        foreach (var entry in events)
        {
            var slotIndex = Math.Min(entry.Page - 1, MAX_DISPLAY_SLOTS - 1);

            if (slotIndex < 0)
                slotIndex = 0;

            DisplaySlots[slotIndex]
                .Add(entry);
        }

        CurrentPage = 0;
        SetBackgroundFrame(0);
        ShowCurrentPage();
    }

    //binds each column to its current page's display slot; SetItems resets that column's scroll to the top.
    private void ShowCurrentPage()
    {
        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;

        LeftList.SetItems(leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot] : []);
        RightList.SetItems(rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot] : []);
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime);
    }
}
