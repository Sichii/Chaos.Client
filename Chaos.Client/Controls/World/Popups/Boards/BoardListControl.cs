#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Scrolling;
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
    private readonly Rectangle BoardListRect;
    private readonly BoardRowList RowList;
    private SlideAnimator Slide;
    private bool SlideMode;

    public UIButton? QuitButton { get; }
    public UIButton? ViewButton { get; }

    public BoardListControl()
        : base("_nbdlist", false)
    {
        Name = "BoardList";
        Visible = false;
        UsesControlStack = true;

        ViewButton = CreateButton("View");
        QuitButton = CreateButton("Quit");

        if (QuitButton is not null)
            QuitButton.Clicked += Close;

        BoardListRect = GetRect("BoardList");

        RowList = new BoardRowList(BoardListRect.Width, BoardListRect.Height);
        RowList.SelectionChanged += UpdateButtonStates;
        RowList.RowActivated += id => OnViewBoard?.Invoke(id);

        if (ViewButton is not null)
            ViewButton.Clicked += () =>
            {
                var boardId = RowList.SelectedBoardId;

                if (boardId >= 0)
                    OnViewBoard?.Invoke((ushort)boardId);
            };

        var viewer = new ScrollViewerControl(RowList)
        {
            X = BoardListRect.X,
            Y = BoardListRect.Y,
            Width = BoardListRect.Width,
            Height = BoardListRect.Height
        };

        AddChild(viewer);
    }

    public void Close()
    {
        InputDispatcher.Instance?.RemoveControl(this);

        if (SlideMode)
            Slide.SlideOut();
        else
        {
            Slide.Hide(this);
            OnClose?.Invoke();
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Slide.Hide(this);
    }

    public event CloseHandler? OnClose;
    public event ViewBoardHandler? OnViewBoard;

    public void SetViewportBounds(Rectangle viewport)
    {
        Slide.SetViewportBounds(viewport, Width);
        Y = viewport.Y;
    }

    public override void Show()
    {
        if (!Visible)
        {
            SlideMode = true;
            InputDispatcher.Instance?.PushControl(this);
            Slide.SlideIn(this);
        }
    }

    public void ShowBoards(List<(ushort BoardId, string Name)> boards, bool slide = true)
    {
        RowList.SetBoards(boards); //fires SelectionChanged -> UpdateButtonStates

        if (slide)
            Show();
        else
        {
            SlideMode = false;
            InputDispatcher.Instance?.PushControl(this);
            Slide.ShowInPlace(this);
        }
    }

    public void SlideClose() => Close();

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        RowList.Enabled = !Slide.Sliding;
        base.Update(gameTime);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (Slide.Sliding)
            return;

        if (e.Key == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void UpdateButtonStates()
    {
        if (ViewButton is not null)
            ViewButton.Enabled = RowList.HasSelection;
    }
}
