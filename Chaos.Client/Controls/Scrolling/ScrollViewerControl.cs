#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Controls.Scrolling;

/// <summary>
///     A container that scrolls a single content element through the <see cref="IVerticalScrollable" /> /
///     <see cref="IHorizontalScrollable" /> interfaces. Owns the scrollbar chrome, per-frame sizing/sync, and wheel
///     handling; the content keeps its own clipping/virtualization. Each bar shows per its visibility mode (Auto shows on
///     overflow) using a deterministic, monotonic two-pass layout that reserves a gutter per visible bar.
/// </summary>
public sealed class ScrollViewerControl : UIPanel
{
    private const int GUTTER = ScrollBarControl.DEFAULT_WIDTH;

    private readonly UIElement ContentElement;
    private readonly IVerticalScrollable? VContent;
    private readonly IHorizontalScrollable? HContent;
    private ScrollBarControl? VScrollBar;
    private ScrollBarControl? HScrollBar;

    //default Visible (not Auto): the codebase convention is an always-present bar that ScrollBarControl renders
    //disabled (no thumb, greyed arrows, non-interactive) when the content fits — matching the pre-migration controls.
    //Auto (hide-on-fit) remains available for content that wants it. The horizontal bar still only appears when the
    //content implements IHorizontalScrollable, so vertical-only content shows no bottom bar regardless of this default.
    public ScrollBarVisibility VerticalScrollBarVisibility { get; set; } = ScrollBarVisibility.Visible;
    public ScrollBarVisibility HorizontalScrollBarVisibility { get; set; } = ScrollBarVisibility.Visible;

    //extra pixels trimmed from the right of the content area, on top of the vertical-bar gutter. Lets content keep a
    //gap between its clip edge and the scrollbar (e.g. exchange item text vs. the prefab element left of the bar).
    //Default 0 leaves all other hosted content unchanged.
    public int ContentRightPadding { get; set; }

    public ScrollViewerControl(UIElement content)
    {
        ContentElement = content;
        VContent = content as IVerticalScrollable;
        HContent = content as IHorizontalScrollable;
        AddChild(content);
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        //update content + bars first so the content's own layout (e.g. text wrap, row count) is current
        base.Update(gameTime);
        LayoutAndSync();
    }

    private void LayoutAndSync()
    {
        //── visibility: deterministic, monotonic two-pass (only ever adds a bar, so it can't oscillate) ──
        //pass 1: assume no bars
        ApplyContentBounds(false, false);
        var vVisible = (VContent is not null) && AxisShows(VerticalScrollBarVisibility, VContent.VerticalExtent, VContent.VerticalViewport);
        var hVisible = (HContent is not null) && AxisShows(HorizontalScrollBarVisibility, HContent.HorizontalExtent, HContent.HorizontalViewport);

        //pass 2: reserve gutters for the pass-1 bars, then re-check Auto (reserving a gutter can push the other axis over)
        ApplyContentBounds(vVisible, hVisible);

        if (!vVisible && (VerticalScrollBarVisibility == ScrollBarVisibility.Auto) && (VContent is not null) && (VContent.VerticalExtent > VContent.VerticalViewport))
            vVisible = true;

        if (!hVisible && (HorizontalScrollBarVisibility == ScrollBarVisibility.Auto) && (HContent is not null) && (HContent.HorizontalExtent > HContent.HorizontalViewport))
            hVisible = true;

        //final content area
        ApplyContentBounds(vVisible, hVisible);

        //── vertical bar ──
        if (vVisible)
        {
            VScrollBar ??= CreateBar(ScrollOrientation.Vertical);
            VScrollBar.Visible = true;
            VScrollBar.X = Width - GUTTER;
            VScrollBar.Y = 0;
            VScrollBar.Width = GUTTER;
            VScrollBar.Height = Height - (hVisible ? GUTTER : 0);
            SyncBar(VScrollBar, VContent!.VerticalExtent, VContent.VerticalViewport, VContent.VerticalOffset);
            VContent.VerticalOffset = VScrollBar.Value; //viewer is the clamp authority
        } else if (VScrollBar is not null)
            VScrollBar.Visible = false;

        //── horizontal bar ──
        if (hVisible)
        {
            HScrollBar ??= CreateBar(ScrollOrientation.Horizontal);
            HScrollBar.Visible = true;
            HScrollBar.X = 0;
            HScrollBar.Y = Height - GUTTER;
            HScrollBar.Width = Width - (vVisible ? GUTTER : 0);
            HScrollBar.Height = GUTTER;
            SyncBar(HScrollBar, HContent!.HorizontalExtent, HContent.HorizontalViewport, HContent.HorizontalOffset);
            HContent.HorizontalOffset = HScrollBar.Value;
        } else if (HScrollBar is not null)
            HScrollBar.Visible = false;

        //re-clamp each implemented axis every frame — the viewer is the clamp authority, so a stale offset
        //(e.g. after the content shrank below the viewport and its Auto bar hid, or in Hidden mode) never
        //leaves a blank view. No-op on the visible path (already clamped to the bar's value above).
        if (VContent is not null)
            VContent.VerticalOffset = Math.Clamp(VContent.VerticalOffset, 0, Math.Max(0, VContent.VerticalExtent - VContent.VerticalViewport));

        if (HContent is not null)
            HContent.HorizontalOffset = Math.Clamp(HContent.HorizontalOffset, 0, Math.Max(0, HContent.HorizontalExtent - HContent.HorizontalViewport));
    }

    private void ApplyContentBounds(bool vBar, bool hBar)
    {
        ContentElement.X = 0;
        ContentElement.Y = 0;
        ContentElement.Width = Width - (vBar ? GUTTER : 0) - ContentRightPadding;
        ContentElement.Height = Height - (hBar ? GUTTER : 0);
    }

    private ScrollBarControl CreateBar(ScrollOrientation orientation)
    {
        var bar = new ScrollBarControl { Orientation = orientation };

        if (orientation == ScrollOrientation.Vertical)
            bar.OnValueChanged += v =>
            {
                if (VContent is not null)
                    VContent.VerticalOffset = v;
            };
        else
            bar.OnValueChanged += v =>
            {
                if (HContent is not null)
                    HContent.HorizontalOffset = v;
            };

        AddChild(bar); //added after content → drawn on top

        return bar;
    }

    private static void SyncBar(ScrollBarControl bar, int extent, int viewport, int offset)
    {
        var max = Math.Max(0, extent - viewport);

        bar.TotalItems = extent;
        bar.VisibleItems = viewport;
        bar.MaxValue = max;
        bar.Value = Math.Clamp(offset, 0, max);
    }

    private static bool AxisShows(ScrollBarVisibility mode, int extent, int viewport)
        => mode switch
        {
            ScrollBarVisibility.Visible => true,
            ScrollBarVisibility.Hidden  => false,
            _                           => extent > viewport //Auto
        };

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (VContent is null)
            return;

        var max = Math.Max(0, VContent.VerticalExtent - VContent.VerticalViewport);

        if (max <= 0)
            return;

        VContent.VerticalOffset = Math.Clamp(VContent.VerticalOffset - e.Delta, 0, max);
        e.Handled = true;
    }
}
