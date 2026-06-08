#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Definitions;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Scrolling;

/// <summary>
///     A virtualized single-column vertical list. It recycles a fixed pool of caller-built row controls and exposes
///     <see cref="IVerticalScrollable" /> in row-index units, so a <see cref="ScrollViewerControl" /> owns the scrollbar
///     chrome and wheel routing. Row appearance is fully delegated: the caller supplies a row factory and a binder, so a
///     row may be a single label or a composite (icon + labels). Selection (parent-managed click-to-select + tint) is
///     opt-in via <c>selectable</c>; lists whose rows are interactive sub-controls leave it off and let each row handle
///     its own activation (wired once in the row factory, since rows are recycled).
/// </summary>
internal sealed class VirtualizedRowList<T> : UIPanel, IVerticalScrollable
{
    private readonly Action<UIElement, T, bool> BindRow;
    private readonly Func<UIElement> CreateRow;
    private readonly bool AutoSelectFirst;
    private readonly int OverscanRows;
    private readonly bool PinToBottom;
    private readonly int RowGap;
    private readonly int RowHeight;
    private readonly bool Selectable;
    private UIElement[] RowPool;

    private IReadOnlyList<T> Items = [];
    private bool Pinned;
    private int DataVersion;
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public int SelectedIndex { get; private set; } = -1;
    public bool HasSelection => Selectable && (SelectedIndex >= 0) && (SelectedIndex < Items.Count);
    public T? SelectedItem => HasSelection ? Items[SelectedIndex] : default;

    //optional trailing row not backed by an item (e.g. a "Load More" footer). When ShowSentinel is set it counts as
    //one extra unit of vertical extent, renders via SentinelBinder, and routes its click to SentinelActivated.
    public Action<UIElement>? SentinelBinder { get; set; }
    public Action? SentinelActivated { get; set; }
    public bool ShowSentinel { get; set; }

    //the sentinel participates (extent, render, click) only when it is both enabled and has a binder, so the scroll
    //extent can never reserve a row the refresh would leave blank.
    private bool HasSentinel => ShowSentinel && (SentinelBinder is not null);

    //fully-visible row count for the current height, clamped to the recycled pool. A property (not a ctor-frozen
    //field) so a host whose viewport grows at runtime (e.g. an expandable chat panel) shows more rows once the pool
    //is grown via EnsureViewportCapacity. Fixed-height hosts keep height/rowHeight unchanged (pool always covers it).
    private int VisibleRows => RowHeight > 0 ? Math.Min(Height / RowHeight, RowPool.Length) : 0;

    //largest valid top-anchored scroll offset (the last unit pinned to the viewport bottom), including the sentinel.
    private int MaxOffset => Math.Max(0, Items.Count + (HasSentinel ? 1 : 0) - VisibleRows);

    //fired when the selected row changes (selectable lists). Hosts use it to refresh dependent button states.
    public event Action? SelectionChanged;

    //fired on double-click of a data row (selectable lists). Non-selectable lists raise activation from their own rows.
    public event Action<T>? RowActivated;

    // IVerticalScrollable — row-index units (one unit = one row); the trailing sentinel counts as a unit when shown
    int IVerticalScrollable.VerticalExtent => Items.Count + (HasSentinel ? 1 : 0);
    int IVerticalScrollable.VerticalViewport => VisibleRows;

    int IVerticalScrollable.VerticalOffset
    {
        get => ScrollOffset;
        set
        {
            if (ScrollOffset != value)
            {
                ScrollOffset = value;
                DataVersion++;
            }

            //pin-to-bottom hosts re-evaluate "is the view sitting on the newest row" on every offset write (the viewer
            //drives this each frame), so a later content append knows whether to follow the growth down to the bottom.
            if (PinToBottom)
                Pinned = ScrollOffset >= MaxOffset;
        }
    }

    /// <param name="width">Initial content width (the hosting <see cref="ScrollViewerControl" /> narrows it per frame).</param>
    /// <param name="height">List viewport height; determines the fully-visible row count and clips a trailing peek row.</param>
    /// <param name="rowHeight">Fixed per-row height.</param>
    /// <param name="createRow">Builds one reusable row control. Called <c>visibleRows + overscanRows</c> times (plus any later <see cref="EnsureViewportCapacity" /> growth). Wire any per-row activation events here (rows are recycled).</param>
    /// <param name="bindRow">Binds an item (and its selected state) into a recycled row: set text/color/icon. Must also refresh whatever internal state the row's own click handler reads (e.g. call the row's SetEntry), or a self-activating recycled row will fire on a stale item.</param>
    /// <param name="rowGap">Vertical spacing added between rows.</param>
    /// <param name="overscanRows">Extra pooled rows beyond the viewport so the next row peeks in (clipped to bounds).</param>
    /// <param name="selectable">When true the list hit-tests rows for click-select + double-click activation; otherwise rows handle their own input.</param>
    /// <param name="autoSelectFirst">When selectable, select row 0 on <see cref="SetItems" /> instead of clearing the selection.</param>
    /// <param name="pinToBottom">When true the list opens on the newest row and follows appended content to the bottom while the view is sitting there (chat-style); scrolling up into history holds position.</param>
    public VirtualizedRowList(
        int width,
        int height,
        int rowHeight,
        Func<UIElement> createRow,
        Action<UIElement, T, bool> bindRow,
        int rowGap = 0,
        int overscanRows = 0,
        bool selectable = false,
        bool autoSelectFirst = false,
        bool pinToBottom = false)
    {
        Width = width;
        Height = height;
        RowHeight = rowHeight;
        RowGap = rowGap;
        BindRow = bindRow;
        CreateRow = createRow;
        Selectable = selectable;
        AutoSelectFirst = autoSelectFirst;
        OverscanRows = overscanRows;
        PinToBottom = pinToBottom;
        Pinned = pinToBottom;

        //interactive row sub-controls (non-selectable lists) must receive their own clicks; only selectable lists
        //act as the hit target that arithmetically maps a click to a row index.
        IsPassThrough = !selectable;

        var initialViewport = rowHeight > 0 ? height / rowHeight : 0;
        var poolSize = Math.Max(0, initialViewport + overscanRows);
        RowPool = new UIElement[poolSize];

        for (var i = 0; i < poolSize; i++)
        {
            var row = createRow();
            row.X = 0;
            row.Y = i * (rowHeight + rowGap);
            row.Width = width;
            row.Height = rowHeight;
            row.Visible = false;

            RowPool[i] = row;
            AddChild(row);
        }
    }

    public void SetItems(IReadOnlyList<T> items)
    {
        Items = items;
        SelectedIndex = Selectable && AutoSelectFirst && (items.Count > 0) ? 0 : -1;

        //pin-to-bottom hosts open on the newest row; everyone else opens at the top.
        if (PinToBottom)
        {
            ScrollOffset = MaxOffset;
            Pinned = true;
        } else
            ScrollOffset = 0;

        DataVersion++;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    ///     Re-renders after the backing list was mutated in place (e.g. a paged append, a chat line arriving, or a row
    ///     removal). The scroll offset and selection are clamped to the new bounds on the same frame (matching the
    ///     pre-migration lists' synchronous clamp), so shrinking the list never flashes a blank tail. A pin-to-bottom
    ///     host that was sitting on the newest row follows growth to the new bottom; one scrolled up into history holds
    ///     its place. <see cref="SelectionChanged" /> fires if the clamp dropped the selection.
    /// </summary>
    public void Invalidate()
    {
        if (PinToBottom && Pinned)
            ScrollOffset = MaxOffset;

        ScrollOffset = Math.Clamp(ScrollOffset, 0, MaxOffset);

        if (SelectedIndex >= Items.Count)
        {
            SelectedIndex = Items.Count - 1;
            SelectionChanged?.Invoke();
        }

        DataVersion++;
    }

    /// <summary>
    ///     Notifies the list that <paramref name="count" /> items were just removed from the FRONT of the backing list
    ///     (e.g. a chat-log cap trimming the oldest lines). Under a top-anchored offset, front removal shifts every
    ///     surviving index down by <paramref name="count" />, so a view scrolled up into history would otherwise jump
    ///     toward newer content; dropping the offset by the same amount keeps the same rows on screen. A view sitting at
    ///     the bottom (pinned) is left untouched for the re-pin in <see cref="Invalidate" />. Call before Invalidate.
    /// </summary>
    public void NotifyRemovedFromFront(int count)
    {
        if ((count > 0) && !Pinned)
            ScrollOffset = Math.Max(0, ScrollOffset - count);
    }

    /// <summary>
    ///     Scrolls so the given item index is centered in the viewport (clamped). Used for "scroll to self"-style jumps.
    /// </summary>
    public void ScrollToIndex(int index)
    {
        if (index < 0)
            return;

        var target = Math.Clamp(index - (VisibleRows / 2), 0, MaxOffset);

        if (target != ScrollOffset)
            ((IVerticalScrollable)this).VerticalOffset = target;
    }

    /// <summary>
    ///     Scrolls by a signed number of rows (positive = toward the bottom / newest row). Returns false when the
    ///     content fits and there is nothing to scroll, so a key handler can leave the event unhandled.
    /// </summary>
    public bool ScrollByRows(int rowDelta)
    {
        if (MaxOffset <= 0)
            return false;

        ((IVerticalScrollable)this).VerticalOffset = Math.Clamp(ScrollOffset + rowDelta, 0, MaxOffset);

        return true;
    }

    /// <summary>
    ///     Jumps to the newest row (and re-pins, for pin-to-bottom hosts). Drives the chat tabs' "scroll to bottom".
    /// </summary>
    public void ScrollToBottom() => ((IVerticalScrollable)this).VerticalOffset = MaxOffset;

    /// <summary>
    ///     Grows the recycled row pool so the list can show at least <paramref name="viewportRows" /> fully-visible rows
    ///     (plus the configured overscan). For hosts whose viewport can grow at runtime (an expandable panel). A no-op
    ///     when the pool already covers it; existing rows are preserved.
    /// </summary>
    public void EnsureViewportCapacity(int viewportRows)
    {
        var needed = Math.Max(0, viewportRows + OverscanRows);

        if (needed <= RowPool.Length)
            return;

        var oldLength = RowPool.Length;
        Array.Resize(ref RowPool, needed);

        for (var i = oldLength; i < needed; i++)
        {
            var row = CreateRow();
            row.X = 0;
            row.Y = i * (RowHeight + RowGap);
            row.Width = Width;
            row.Height = RowHeight;
            row.Visible = false;

            RowPool[i] = row;
            AddChild(row);
        }

        DataVersion++;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshRows();
        base.Draw(spriteBatch);
    }

    private void RefreshRows()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < RowPool.Length; i++)
        {
            var row = RowPool[i];

            //track the viewer-narrowed width (Update runs before Draw) so single-label rows re-truncate against the
            //visible box. Viewport rows keep full height; overscan "peek" rows are clipped to the list bounds, which
            //also bounds their hit-test (ContainsPoint uses Height) — matching the metadata columns' maxHeight clip.
            //Rows are children of this height-bounded panel, so the parent ClipRect trims any spill past the list edge.
            row.Width = Width;
            var y = i * (RowHeight + RowGap);
            row.Height = i < VisibleRows ? RowHeight : Math.Min(RowHeight, Math.Max(0, Height - y));

            var entryIndex = ScrollOffset + i;

            if (entryIndex < Items.Count)
            {
                row.Visible = true;
                BindRow(row, Items[entryIndex], Selectable && (entryIndex == SelectedIndex));
            } else if (HasSentinel && (entryIndex == Items.Count))
            {
                row.Visible = true;
                SentinelBinder!(row);
            } else
                row.Visible = false;
        }
    }

    public override void OnClick(ClickEvent e)
    {
        base.OnClick(e);

        if (!Selectable || (e.Button != MouseButton.Left))
            return;

        var entryIndex = RowAt(e.ScreenY);

        if (entryIndex < 0)
            return;

        if (HasSentinel && (entryIndex == Items.Count))
        {
            SentinelActivated?.Invoke();

            return;
        }

        if (entryIndex >= Items.Count)
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        SelectionChanged?.Invoke();
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        base.OnDoubleClick(e);

        if (!Selectable || (e.Button != MouseButton.Left))
            return;

        var entryIndex = RowAt(e.ScreenY);

        if ((entryIndex < 0) || (entryIndex >= Items.Count))
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        SelectionChanged?.Invoke();
        RowActivated?.Invoke(Items[entryIndex]);
    }

    private int RowAt(int screenY)
    {
        var localY = screenY - ScreenY;

        if ((localY < 0) || (localY >= Height))
            return -1;

        //a click in the inter-row gap (RowGap > 0) attributes to the row above it. Only selectable lists hit-test and
        //none currently use a gap, but this keeps the mapping defined if one ever does.
        var row = localY / (RowHeight + RowGap);

        if ((row < 0) || (row >= RowPool.Length))
            return -1;

        return ScrollOffset + row;
    }
}
