#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Options;

/// <summary>
///     Friends list popup using _nfriend prefab. Two columns of 10 editable textboxes (20 total slots), filled
///     sequentially — slots 1-10 populate the left column, slots 11-20 the right. OK saves, Cancel discards.
/// </summary>
public sealed class FriendsListControl : PrefabPanel
{
    //row stride matches the _nfriend prefab's wooden slot lines. must stay in sync with the
    //background graphic — if the prefab is ever replaced with a different vertical cadence,
    //update this.
    private const int ROW_HEIGHT = 21;
    private const int ROWS_PER_COLUMN = 10;
    private const int MAX_FRIENDS = ROWS_PER_COLUMN * 2;
    private const int NAME_MAX_LENGTH = 12;

    private readonly Rectangle LeftColumnRect;
    private readonly UITextBox[] NameSlots = new UITextBox[MAX_FRIENDS];
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
                ROWS_PER_COLUMN * ROW_HEIGHT);

        if (RightColumnRect == Rectangle.Empty)
            RightColumnRect = new Rectangle(
                251,
                40,
                175,
                ROWS_PER_COLUMN * ROW_HEIGHT);

        //20 slots total — first 10 fill left column top-to-bottom, next 10 fill right column.
        //UITextBox defaults (PaddingLeft/Top = 2) keep text centered inside each slot — same
        //pattern used by MacrosListControl.
        for (var i = 0; i < MAX_FRIENDS; i++)
        {
            var isLeft = i < ROWS_PER_COLUMN;
            var column = isLeft ? LeftColumnRect : RightColumnRect;
            var row = isLeft ? i : i - ROWS_PER_COLUMN;

            NameSlots[i] = new UITextBox
            {
                Name = $"Slot{i}",
                X = column.X,
                Y = column.Y + row * ROW_HEIGHT,
                Width = column.Width,
                Height = ROW_HEIGHT,
                MaxLength = NAME_MAX_LENGTH,
                ForegroundColor = Color.White,
                IsTabStop = true
            };

            AddChild(NameSlots[i]);
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

        //textboxes are children — drawn by base.draw()
        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Returns the current non-empty friend names in slot order — what the user sees in the textboxes right
    ///     now, not the last SetFriends argument.
    /// </summary>
    public List<string> GetFriendNames()
    {
        var names = new List<string>(MAX_FRIENDS);

        for (var i = 0; i < MAX_FRIENDS; i++)
        {
            var text = NameSlots[i].Text.Trim();

            if (!string.IsNullOrEmpty(text))
                names.Add(text);
        }

        return names;
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);

        if (SlideMode)
            Slide.Hide(this);
        else
            Visible = false;
    }

    public event CloseHandler? OnClose;
    public event OkHandler? OnOk;

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        //sequential fill — first MAX_FRIENDS entries populate slots 0..MAX_FRIENDS-1. excess ignored.
        for (var i = 0; i < MAX_FRIENDS; i++)
            NameSlots[i].Text = i < Friends.Count ? Friends[i].Name : string.Empty;
    }

    /// <summary>
    ///     Populates the friends list. Entries fill slots sequentially — left column first (1-10), then right
    ///     column (11-20). Online status is not distinguished visually (all text in white).
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
    ///     Slides in from the configured slide anchor (button mode).
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
