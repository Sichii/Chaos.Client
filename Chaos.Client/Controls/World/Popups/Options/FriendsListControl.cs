#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Utilities;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Options;

/// <summary>
///     Friends list popup using _nfriend prefab. Two-column layout: left column (online friends), right column (offline
///     friends). Row height 16px. OK/Cancel buttons at bottom.
/// </summary>
public sealed class FriendsListControl : PrefabPanel
{
    private const int ROW_HEIGHT = 16;
    private const int MAX_VISIBLE_ROWS = 12;

    private readonly Rectangle LeftColumnRect;

    private readonly UILabel[] NamesColumn1 = new UILabel[MAX_VISIBLE_ROWS];
    private readonly UILabel[] NamesColumn2 = new UILabel[MAX_VISIBLE_ROWS];
    private readonly Rectangle RightColumnRect;
    private bool ClosedWithOk;
    private int DataVersion;

    private List<FriendEntry> Friends = [];
    private int RenderedVersion = -1;
    private SlideAnimator Slide;
    private int SlideAnchorY;
    private bool SlideMode;

    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public FriendsListControl()
        : base("_nfriend", false)
    {
        Name = "FriendsList";
        Visible = false;
        UsesControlStack = true;

        CancelButton = CreateButton("Cancel");
        OkButton = CreateButton("OK");

        if (CancelButton is not null)
            CancelButton.Clicked += Close;

        if (OkButton is not null)
            OkButton.Clicked += CloseWithOk;

        //column rects from prefab
        LeftColumnRect = GetRect("TextTopLeft");
        RightColumnRect = GetRect("TextTopRight");

        //if no rects found, use defaults based on prefab layout
        if (LeftColumnRect == Rectangle.Empty)
            LeftColumnRect = new Rectangle(
                40,
                40,
                175,
                MAX_VISIBLE_ROWS * ROW_HEIGHT);

        if (RightColumnRect == Rectangle.Empty)
            RightColumnRect = new Rectangle(
                251,
                40,
                175,
                MAX_VISIBLE_ROWS * ROW_HEIGHT);

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            NamesColumn1[i] = new UILabel
            {
                Name = $"Left{i}",
                X = LeftColumnRect.X,
                Y = LeftColumnRect.Y + i * ROW_HEIGHT,
                Width = LeftColumnRect.Width,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            NamesColumn2[i] = new UILabel
            {
                Name = $"Right{i}",
                X = RightColumnRect.X,
                Y = RightColumnRect.Y + i * ROW_HEIGHT,
                Width = RightColumnRect.Width,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(NamesColumn1[i]);
            AddChild(NamesColumn2[i]);
        }
    }

    private void Close()
    {
        ClosedWithOk = false;

        if (SlideMode)
        {
            InputDispatcher.Instance?.RemoveControl(this);
            Slide.SlideOut();
        } else
        {
            Hide();
            OnClose?.Invoke();
        }
    }

    private void CloseWithOk()
    {
        ClosedWithOk = true;

        if (SlideMode)
        {
            InputDispatcher.Instance?.RemoveControl(this);
            Slide.SlideOut();
        } else
        {
            OnOk?.Invoke();
            Hide();
            OnClose?.Invoke();
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshCaches();

        //labels are children — drawn by base.draw()
        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Returns all friend names in the current list.
    /// </summary>
    public List<string> GetFriendNames()
        => Friends.Select(f => f.Name)
                  .ToList();

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);

        if (SlideMode)
            Slide.Hide(this);
        else
            Visible = false;
    }

    public event Action? OnClose;
    public event Action? OnOk;

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        using var onlineFriends = Friends.Where(f => f.IsOnline)
                                         .ToRented();

        using var offlineFriends = Friends.Where(f => !f.IsOnline)
                                          .ToRented();

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            if (i < onlineFriends.Count)
            {
                NamesColumn1[i].Text = onlineFriends.Array[i].Name;
                NamesColumn1[i].ForegroundColor = new Color(150, 255, 150);
            } else
                NamesColumn1[i].Text = string.Empty;

            if (i < offlineFriends.Count)
            {
                NamesColumn2[i].Text = offlineFriends.Array[i].Name;
                NamesColumn2[i].ForegroundColor = new Color(150, 150, 150);
            } else
                NamesColumn2[i].Text = string.Empty;
        }
    }

    /// <summary>
    ///     Populates the friends list. Online friends on left, offline on right.
    /// </summary>
    public void SetFriends(List<FriendEntry> friends)
    {
        Friends = friends;
        DataVersion++;
    }

    public void SetSlideAnchor(int anchorX, int anchorY)
    {
        Slide.SetSlideAnchor(anchorX, Width);
        SlideAnchorY = anchorY;
    }

    /// <summary>
    ///     Shows immediately at top-center of screen (hotkey mode).
    /// </summary>
    public override void Show()
    {
        this.CenterHorizontallyOnScreen();
        Y = 0;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
        SlideMode = false;
    }

    /// <summary>
    ///     Slides out from the left edge of MainOptionsControl (button mode).
    /// </summary>
    public void SlideIn()
    {
        if (Visible)
            return;

        Y = SlideAnchorY;
        InputDispatcher.Instance?.PushControl(this);
        Slide.SlideIn(this);
        SlideMode = true;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            if (ClosedWithOk)
                OnOk?.Invoke();

            OnClose?.Invoke();

            return;
        }

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
}