#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Board selection panel using _nbdlist prefab. Displays a scrollable list of available bulletin boards. View button
///     opens the selected board; Quit closes. Slides in from the right edge of the viewport.
/// </summary>
public sealed class BoardListControl : PrefabPanel
{
    private const float DOUBLE_CLICK_MS = 400f;
    private const int ROW_HEIGHT = 18;
    private const float SLIDE_DURATION_MS = 250f;

    private readonly Rectangle BoardListRect;
    private readonly int MaxVisibleRows;
    private readonly CachedText[] NameCaches;
    private readonly ScrollBarControl ScrollBar;
    private List<(ushort BoardId, string Name)> Boards = [];
    private int DataVersion;
    private float LastClickTime;
    private int OffScreenX;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;
    private float SlideTimer;
    private bool Sliding;
    private bool SlidingOut;
    private int TargetX;

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
            QuitButton.OnClick += SlideOut;

        if (ViewButton is not null)
            ViewButton.OnClick += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Boards.Count))
                    OnViewBoard?.Invoke(Boards[SelectedIndex].BoardId);
            };

        BoardListRect = GetRect("BoardList");
        MaxVisibleRows = BoardListRect.Height > 0 ? BoardListRect.Height / ROW_HEIGHT : 0;

        NameCaches = new CachedText[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
            NameCaches[i] = new CachedText();

        ScrollBar = new ScrollBarControl
        {
            X = BoardListRect.Right - ScrollBarControl.DEFAULT_WIDTH,
            Y = BoardListRect.Y,
            Height = BoardListRect.Height
        };

        AddChild(ScrollBar);
    }

    public override void Dispose()
    {
        foreach (var c in NameCaches)
            c.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshCaches();
        base.Draw(spriteBatch);

        var sx = ScreenX + BoardListRect.X;
        var sy = ScreenY + BoardListRect.Y;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var boardIndex = ScrollOffset + i;

            if (boardIndex >= Boards.Count)
                break;

            var rowY = sy + i * ROW_HEIGHT;

            NameCaches[i]
                .Draw(spriteBatch, new Vector2(sx + 4, rowY + 2));
        }
    }

    public override void Hide()
    {
        Visible = false;
        Sliding = false;
        X = OffScreenX;
    }

    public event Action? OnClose;
    public event Action<ushort>? OnViewBoard;

    private void RefreshCaches()
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

                NameCaches[i]
                    .Update(Boards[boardIndex].Name, textColor);
            } else
                NameCaches[i]
                    .Update(string.Empty, Color.White);
        }
    }

    public void SetViewportBounds(Rectangle viewport)
    {
        TargetX = viewport.X + viewport.Width - Width;
        OffScreenX = viewport.X + viewport.Width;
        Y = viewport.Y;
    }

    public override void Show()
    {
        if (!Visible)
            SlideIn();
    }

    public void ShowBoards(List<(ushort BoardId, string Name)> boards)
    {
        Boards = boards;
        SelectedIndex = boards.Count > 0 ? 0 : -1;
        ScrollOffset = 0;
        DataVersion++;
        ScrollBar.Value = 0;
        ScrollBar.MaxValue = Math.Max(0, boards.Count - MaxVisibleRows);
        UpdateButtonStates();
        Show();
    }

    private void SlideIn()
    {
        X = OffScreenX;
        Visible = true;
        Sliding = true;
        SlidingOut = false;
        SlideTimer = 0;
    }

    private void SlideOut()
    {
        Sliding = true;
        SlidingOut = true;
        SlideTimer = 0;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (Sliding)
        {
            SlideTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
            var t = Math.Clamp(SlideTimer / SLIDE_DURATION_MS, 0f, 1f);
            var eased = 1f - (1f - t) * (1f - t);

            if (SlidingOut)
            {
                X = (int)MathHelper.Lerp(TargetX, OffScreenX, eased);

                if (t >= 1f)
                {
                    Hide();
                    OnClose?.Invoke();

                    return;
                }
            } else
            {
                X = (int)MathHelper.Lerp(OffScreenX, TargetX, eased);

                if (t >= 1f)
                {
                    X = TargetX;
                    Sliding = false;
                }
            }
        }

        if (input.WasKeyPressed(Keys.Escape))
        {
            SlideOut();

            return;
        }

        LastClickTime += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        // Row click selection + double-click to open
        if (input.WasLeftButtonPressed && !Sliding)
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
                    if ((boardIndex == SelectedIndex) && (LastClickTime < DOUBLE_CLICK_MS))
                    {
                        OnViewBoard?.Invoke(Boards[boardIndex].BoardId);
                        LastClickTime = float.MaxValue;
                    } else
                    {
                        SelectedIndex = boardIndex;
                        DataVersion++;
                        UpdateButtonStates();
                        LastClickTime = 0;
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