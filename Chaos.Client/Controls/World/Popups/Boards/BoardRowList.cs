#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Scrolling;
using Chaos.Client.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Virtualized row panel that displays a scrollable list of board entries. Implements
///     <see cref="IVerticalScrollable" /> so a <see cref="ScrollViewerControl" /> can own the scrollbar chrome and
///     wheel routing. The panel's Height determines <see cref="MaxVisibleRows" />.
/// </summary>
internal sealed class BoardRowList : UIPanel, IVerticalScrollable
{
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;

    private readonly UILabel[] RowLabels;
    private readonly int MaxVisibleRows;
    private List<(ushort BoardId, string Name)> Boards = [];
    private int DataVersion;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;

    public int SelectedBoardId => (SelectedIndex >= 0) && (SelectedIndex < Boards.Count)
        ? Boards[SelectedIndex].BoardId
        : -1;

    public bool HasSelection => (SelectedIndex >= 0) && (SelectedIndex < Boards.Count);

    // IVerticalScrollable — row-index units (one unit = one board row)
    int IVerticalScrollable.VerticalExtent => Boards.Count;
    int IVerticalScrollable.VerticalViewport => MaxVisibleRows;

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
        }
    }

    public event Action? SelectionChanged;
    public event Action<ushort>? RowActivated;

    public BoardRowList(int width, int height)
    {
        Width = width;
        Height = height;
        MaxVisibleRows = height > 0 ? height / ROW_HEIGHT : 0;
        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                X = 0,
                Y = i * ROW_HEIGHT,
                Width = width,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(RowLabels[i]);
        }
    }

    public void SetBoards(List<(ushort BoardId, string Name)> boards)
    {
        Boards = boards;
        SelectedIndex = boards.Count > 0 ? 0 : -1;
        ScrollOffset = 0;
        DataVersion++;
        SelectionChanged?.Invoke();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshLabels();
        base.Draw(spriteBatch);
    }

    private void RefreshLabels()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            //track the viewer-narrowed width (Update runs before Draw) so ellipsis truncation measures
            //against the visible box, not the wider construction width — matches the pre-migration labels.
            RowLabels[i].Width = Width;

            var boardIndex = ScrollOffset + i;

            if (boardIndex < Boards.Count)
            {
                var textColor = boardIndex == SelectedIndex ? new Color(100, 149, 237) : TextColors.Default;

                RowLabels[i].ForegroundColor = textColor;
                RowLabels[i].Text = Boards[boardIndex].Name;
            } else
                RowLabels[i].Text = string.Empty;
        }
    }

    public override void OnClick(ClickEvent e)
    {
        base.OnClick(e);

        if (e.Button != MouseButton.Left)
            return;

        var localY = e.ScreenY - ScreenY;

        if ((localY < 0) || (localY >= Height))
            return;

        var row = localY / ROW_HEIGHT;

        if (row >= MaxVisibleRows)
            return;

        var entryIndex = ScrollOffset + row;

        if (entryIndex >= Boards.Count)
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        SelectionChanged?.Invoke();
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        base.OnDoubleClick(e);

        if (e.Button != MouseButton.Left)
            return;

        var localY = e.ScreenY - ScreenY;

        if ((localY < 0) || (localY >= Height))
            return;

        var row = localY / ROW_HEIGHT;

        if (row >= MaxVisibleRows)
            return;

        var entryIndex = ScrollOffset + row;

        if (entryIndex >= Boards.Count)
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        SelectionChanged?.Invoke();
        RowActivated?.Invoke(Boards[entryIndex].BoardId);
    }
}
