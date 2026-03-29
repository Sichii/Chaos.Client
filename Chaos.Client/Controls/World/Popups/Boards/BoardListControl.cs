#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Extensions;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Board selection panel using _nbdlist prefab. Displays a scrollable list of available bulletin boards. View button
///     opens the selected board; Quit closes. Slides in from the right edge of the viewport.
/// </summary>
public sealed class BoardListControl : PrefabPanel
{
    private const int ROW_HEIGHT = 18;

    private readonly Rectangle BoardListRect;
    private readonly int MaxVisibleRows;
    private readonly UILabel[] RowLabels;
    private readonly ScrollBarControl ScrollBar;
    private List<(ushort BoardId, string Name)> Boards = [];
    private int DataVersion;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;
    private SlideAnimator Slide;

    public UIButton? QuitButton { get; }
    public UIButton? ViewButton { get; }

    public BoardListControl()
        : base("_nbdlist", false)
    {
        Name = "BoardList";
        Visible = false;

        ViewButton = CreateButton("View");
        QuitButton = CreateButton("Quit");

        if (QuitButton is not null)
            QuitButton.OnClick += () => Slide.SlideOut();

        if (ViewButton is not null)
            ViewButton.OnClick += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Boards.Count))
                    OnViewBoard?.Invoke(Boards[SelectedIndex].BoardId);
            };

        BoardListRect = GetRect("BoardList");
        MaxVisibleRows = BoardListRect.Height > 0 ? BoardListRect.Height / ROW_HEIGHT : 0;

        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                X = BoardListRect.X,
                Y = BoardListRect.Y + i * ROW_HEIGHT,
                Width = BoardListRect.Width - ScrollBarControl.DEFAULT_WIDTH,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(RowLabels[i]);
        }

        ScrollBar = new ScrollBarControl
        {
            X = BoardListRect.AlignRight(ScrollBarControl.DEFAULT_WIDTH),
            Y = BoardListRect.Y,
            Height = BoardListRect.Height
        };

        AddChild(ScrollBar);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshLabels();
        base.Draw(spriteBatch);
    }

    public override void Hide() => Slide.Hide(this);

    public event Action? OnClose;
    public event Action<ushort>? OnViewBoard;

    private void RefreshLabels()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var boardIndex = ScrollOffset + i;

            if (boardIndex < Boards.Count)
            {
                var textColor = boardIndex == SelectedIndex ? new Color(100, 149, 237) : Color.White;

                RowLabels[i].ForegroundColor = textColor;
                RowLabels[i].Text = Boards[boardIndex].Name;
            } else
                RowLabels[i].Text = string.Empty;
        }
    }

    public void SetViewportBounds(Rectangle viewport)
    {
        Slide.SetViewportBounds(viewport, Width);
        Y = viewport.Y;
    }

    public override void Show()
    {
        if (!Visible)
            Slide.SlideIn(this);
    }

    public void ShowBoards(List<(ushort BoardId, string Name)> boards, bool slide = true)
    {
        Boards = boards;
        SelectedIndex = boards.Count > 0 ? 0 : -1;
        ScrollOffset = 0;
        DataVersion++;
        ScrollBar.Value = 0;
        ScrollBar.TotalItems = boards.Count;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, boards.Count - MaxVisibleRows);
        UpdateButtonStates();

        if (slide)
            Show();
        else
            Slide.ShowInPlace(this);
    }

    public void SlideClose() => Slide.SlideOut();

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        if (input.WasKeyPressed(Keys.Escape))
        {
            Slide.SlideOut();

            return;
        }

        // Row click selection + double-click to open
        if (input.WasLeftButtonPressed && !Slide.Sliding)
        {
            var relX = input.MouseX - ScreenX - BoardListRect.X;
            var relY = input.MouseY - ScreenY - BoardListRect.Y;

            if ((relX >= 0)
                && (relX < (BoardListRect.Width - ScrollBarControl.DEFAULT_WIDTH))
                && (relY >= 0)
                && (relY < BoardListRect.Height))
            {
                var clickedRow = relY / ROW_HEIGHT;
                var boardIndex = ScrollOffset + clickedRow;

                if (boardIndex < Boards.Count)
                {
                    if (input.WasLeftButtonDoubleClicked && (boardIndex == SelectedIndex))
                    {
                        OnViewBoard?.Invoke(Boards[boardIndex].BoardId);
                    } else
                    {
                        SelectedIndex = boardIndex;
                        DataVersion++;
                        UpdateButtonStates();
                    }
                }
            }
        }

        // Scroll bar
        var prevScroll = ScrollOffset;
        ScrollBar.Update(gameTime, input);

        if (ScrollBar.Value != prevScroll)
        {
            ScrollOffset = ScrollBar.Value;
            DataVersion++;
        }

        // Mouse wheel
        if (input.ScrollDelta != 0)
        {
            var maxScroll = Math.Max(0, Boards.Count - MaxVisibleRows);
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, maxScroll);
            ScrollBar.Value = ScrollOffset;
            ScrollBar.MaxValue = maxScroll;
            DataVersion++;
        }

        base.Update(gameTime, input);
    }

    private void UpdateButtonStates()
    {
        var hasSelection = SelectedIndex >= 0;

        if (ViewButton is not null)
            ViewButton.Enabled = hasSelection;
    }
}