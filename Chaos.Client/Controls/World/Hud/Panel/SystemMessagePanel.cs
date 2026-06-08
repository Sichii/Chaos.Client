#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.Scrolling;
using Chaos.Client.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Hud.Panel;

/// <summary>
///     Message history panel (Shift+F). Displays server message history (same text as the orange bar) in its own tab-sized
///     panel. Reads from the shared history list and preserves the per-message color (orange for system messages,
///     whisper/group/guild colors for echoed chat). Text is rendered by a <see cref="VirtualizedRowList{T}" /> hosted in
///     a <see cref="ScrollViewerControl" />; the list pins to the bottom (newest at the bottom, scroll up for history).
/// </summary>
public sealed class SystemMessagePanel : ExpandablePanel
{
    private const int GLYPH_HEIGHT = 12;
    private readonly IReadOnlyList<Chat.OrangeBarMessage> History;
    private readonly Rectangle NormalDisplayBounds;
    private readonly int PanelOriginX;
    private readonly int PanelOriginY;
    private readonly VirtualizedRowList<Chat.OrangeBarMessage> RowList;
    private readonly ScrollViewerControl Viewer;

    private Rectangle DisplayBounds;
    private Rectangle ExpandedDisplayBounds;
    private int LastHistoryCount;

    public SystemMessagePanel(Rectangle displayBounds, Rectangle panelBounds, IReadOnlyList<Chat.OrangeBarMessage> history)
    {
        Name = "MessageHistory";
        NormalDisplayBounds = displayBounds;
        DisplayBounds = displayBounds;
        PanelOriginX = panelBounds.X;
        PanelOriginY = panelBounds.Y;
        History = history;

        Background = UiRenderer.Instance!.GetSpfTexture("_nchatbk.spf");

        RowList = new VirtualizedRowList<Chat.OrangeBarMessage>(
            displayBounds.Width,
            displayBounds.Height,
            GLYPH_HEIGHT,
            static () => new UILabel
            {
                PaddingLeft = 0,
                PaddingTop = 0
            },
            static (row, msg, _) =>
            {
                var label = (UILabel)row;
                label.Text = msg.Text;
                label.ForegroundColor = msg.Color;
            },
            pinToBottom: true);

        RowList.SetItems(History);

        //LayoutViewer sets Y/Height (bottom-anchored to whole rows)
        Viewer = new ScrollViewerControl(RowList)
        {
            X = displayBounds.X - panelBounds.X,
            Width = displayBounds.Width
        };

        LayoutViewer(NormalDisplayBounds);

        AddChild(Viewer);
    }

    /// <summary>
    ///     Configures expand support for the large HUD message history panel (larger text area).
    /// </summary>
    public void ConfigureExpand(Texture2D? expandedBackground, Rectangle expandedBounds, Rectangle panelBounds)
    {
        ExpandedDisplayBounds = expandedBounds;

        //clear the normal background so expandyoffset is computed from panel height, not the
        //texture height (which is the same as the expanded texture, yielding expandyoffset=0).
        Background = null;
        Height = panelBounds.Height;

        ConfigureExpand(expandedBackground);

        //grow the recycled row pool to cover the expanded line count (kept across expand/collapse)
        RowList.EnsureViewportCapacity(expandedBounds.Height / GLYPH_HEIGHT);

        //in the large hud the compact area hides the bar but keeps the right-edge gap; SetExpanded shows it
        Viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        Viewer.ContentRightPadding = ScrollBarControl.DEFAULT_WIDTH;
    }

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        if (!CanExpand)
            return;

        DisplayBounds = expanded ? ExpandedDisplayBounds : NormalDisplayBounds;

        //re-anchor the viewer to the current mode's box (also makes VisibleRows current for the re-pin below). X/Width
        //stay normal and the panel's Y-shift grows the area upward, so the newest line stays pinned at the box bottom.
        LayoutViewer(DisplayBounds);
        Viewer.VerticalScrollBarVisibility = expanded ? ScrollBarVisibility.Visible : ScrollBarVisibility.Hidden;
        Viewer.ContentRightPadding = expanded ? 0 : ScrollBarControl.DEFAULT_WIDTH;

        //collapse shrinks the viewport (MaxOffset grows): re-pin to the new bottom if we were sitting there
        RowList.Invalidate();
    }

    //bottom-anchors the viewer: floors the height to whole GLYPH_HEIGHT rows and pushes it down by the remainder so the
    //newest line sits flush with the box bottom (matching the bottom-anchored labels). The shipped chat rects (36 normal,
    //96 expanded) are whole multiples of GLYPH_HEIGHT, so the remainder is 0 in every mode and this is a no-op; the floor
    //keeps the bottom flush should a rect ever not be. X/Width are fixed at the normal position; the panel Y-shift expands.
    private void LayoutViewer(Rectangle bounds)
    {
        var usedHeight = bounds.Height / GLYPH_HEIGHT * GLYPH_HEIGHT;
        Viewer.Y = NormalDisplayBounds.Y - PanelOriginY + (bounds.Height - usedHeight);
        Viewer.Height = usedHeight;
        RowList.Height = usedHeight;
    }

    public void ScrollToBottom() => RowList.ScrollToBottom();

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (Scroll(e.Delta))
            e.Handled = true;
    }

    //positive delta scrolls toward older messages (matching the wheel + Shift+Up); top-anchored offset moves the
    //opposite way, hence the sign flip into the list.
    public bool Scroll(int delta) => RowList.ScrollByRows(-delta);

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        //the history buffer is owned elsewhere; re-render when it grows (matches the pre-migration count poll). Done
        //before base.Update so the viewer syncs the re-pinned offset to the bar in this same frame.
        if (History.Count != LastHistoryCount)
        {
            LastHistoryCount = History.Count;
            RowList.Invalidate();
        }

        base.Update(gameTime);
    }
}
