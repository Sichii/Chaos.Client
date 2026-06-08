#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Scrolling;
#endregion

namespace Chaos.Client.Controls.World.Popups.Exchange;

/// <summary>
///     Virtualized item panel for one side of the exchange. Holds a fixed pool of
///     <see cref="ExchangeItemControl" /> row controls and implements both <see cref="IVerticalScrollable" /> (row-index
///     units) and <see cref="IHorizontalScrollable" /> (pixel units) so a <see cref="ScrollViewerControl" /> owns both
///     scrollbars. The viewer reserves a 16px bottom gutter for the horizontal bar; the host sizes the viewer to the full
///     item rect (~144px) so content height = 144 − 16 = 128 = exactly the 4×32px rows (no clipping), and the bar lands
///     in the rect's dedicated bottom strip.
/// </summary>
internal sealed class ExchangeItemList : UIPanel, IVerticalScrollable, IHorizontalScrollable
{
    private const int MAX_VISIBLE_ITEMS = 4;
    private const int ITEM_ROW_HEIGHT = 32;

    private readonly bool RightSide;
    private readonly ExchangeItemControl[] Items = new ExchangeItemControl[MAX_VISIBLE_ITEMS];
    private int ScrollOffset;

    // IVerticalScrollable — row-index units
    int IVerticalScrollable.VerticalExtent => WorldState.Exchange.GetItemCount(RightSide);
    int IVerticalScrollable.VerticalViewport => MAX_VISIBLE_ITEMS;

    int IVerticalScrollable.VerticalOffset
    {
        get => ScrollOffset;
        set
        {
            if (ScrollOffset == value)
                return;

            ScrollOffset = value;
            Refresh();
        }
    }

    // IHorizontalScrollable — pixel units. Extent is the widest visible entry; viewport is the live content width the
    // viewer sets each frame (rect.Width − V-gutter − ContentRightPadding); offset pans the rows via ApplyHorizontalOffset.
    int IHorizontalScrollable.HorizontalExtent => ComputeMaxEntryWidth();
    int IHorizontalScrollable.HorizontalViewport => Width;

    int IHorizontalScrollable.HorizontalOffset
    {
        get;
        set
        {
            field = value;
            ApplyHorizontalOffset(value);
        }
    }

    public ExchangeItemList(bool rightSide)
    {
        RightSide = rightSide;
        IsPassThrough = true;

        for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
        {
            var control = new ExchangeItemControl
            {
                Name = $"ExchangeItem{i}",
                Y = i * ITEM_ROW_HEIGHT
            };

            control.SetBaseX(0);
            Items[i] = control;
            AddChild(control);
        }
    }

    /// <summary>
    ///     Clears all item slots and resets both the vertical scroll offset and the horizontal pixel offset to zero.
    /// </summary>
    public void Reset()
    {
        ScrollOffset = 0;

        //zero the stored horizontal offset (backing field) and snap rows back to base X. The viewer self-heals when the
        //list empties (extent 0 → re-clamp drives offset to 0), but resetting here avoids a one-frame glitch on re-open.
        ((IHorizontalScrollable)this).HorizontalOffset = 0;

        foreach (var item in Items)
            item.ClearItem();
    }

    /// <summary>
    ///     Re-populates visible rows from <see cref="WorldState.Exchange" /> at the current scroll offset.
    ///     Called by the viewer-driven <see cref="IVerticalScrollable.VerticalOffset" /> setter and by
    ///     <see cref="ExchangeControl" /> when an item is added. The viewer polls <see cref="IHorizontalScrollable.HorizontalExtent" />
    ///     every frame, so the horizontal bar's range resyncs to the new visible window automatically — no notification needed.
    /// </summary>
    public void Refresh()
    {
        var totalCount = WorldState.Exchange.GetItemCount(RightSide);

        for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
        {
            var dataIndex = (byte)(ScrollOffset + i);

            if (dataIndex < totalCount)
            {
                var data = WorldState.Exchange.GetItem(RightSide, dataIndex);

                if (data.HasValue)
                {
                    Items[i]
                        .SetItem(data.Value.Sprite, data.Value.Color, data.Value.Name ?? string.Empty);

                    continue;
                }
            }

            Items[i]
                .ClearItem();
        }
    }

    /// <summary>
    ///     Applies a horizontal pixel offset to every item row so long names pan into view. Driven by the viewer through
    ///     the <see cref="IHorizontalScrollable.HorizontalOffset" /> setter. The offset lives on the 4 reused controls and
    ///     persists across vertical scrolls (the same controls are repopulated in place), so it survives a <see cref="Refresh" />.
    /// </summary>
    private void ApplyHorizontalOffset(int offset)
    {
        foreach (var item in Items)
            item.HorizontalOffset = offset;
    }

    /// <summary>
    ///     The widest entry (icon + padding + name) among the currently visible rows, in pixels. Backs
    ///     <see cref="IHorizontalScrollable.HorizontalExtent" />, which the viewer polls to size the horizontal bar.
    /// </summary>
    private int ComputeMaxEntryWidth()
    {
        var maxEntryWidth = 0;

        foreach (var item in Items)
            if (item.Visible && (item.EntryWidth > maxEntryWidth))
                maxEntryWidth = item.EntryWidth;

        return maxEntryWidth;
    }
}
