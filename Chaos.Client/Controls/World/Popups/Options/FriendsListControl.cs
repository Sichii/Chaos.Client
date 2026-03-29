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

    private readonly TextElement[] NamesColumn1 = new TextElement[MAX_VISIBLE_ROWS];
    private readonly TextElement[] NamesColumn2 = new TextElement[MAX_VISIBLE_ROWS];
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

        CancelButton = CreateButton("Cancel");
        OkButton = CreateButton("OK");

        if (CancelButton is not null)
            CancelButton.OnClick += Close;

        if (OkButton is not null)
            OkButton.OnClick += CloseWithOk;

        // Column rects from prefab
        LeftColumnRect = GetRect("TextTopLeft");
        RightColumnRect = GetRect("TextTopRight");

        // If no rects found, use defaults based on prefab layout
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
            NamesColumn1[i] = new TextElement();
            NamesColumn2[i] = new TextElement();
        }
    }

    private void Close()
    {
        ClosedWithOk = false;

        if (SlideMode)
            Slide.SlideOut();
        else
        {
            Hide();
            OnClose?.Invoke();
        }
    }

    private void CloseWithOk()
    {
        ClosedWithOk = true;

        if (SlideMode)
            Slide.SlideOut();
        else
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

        base.Draw(spriteBatch);

        RefreshCaches();

        var sx = ScreenX;
        var sy = ScreenY;

        // Draw online friends (left column)
        var leftX = sx + LeftColumnRect.X;
        var leftY = sy + LeftColumnRect.Y;

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
            NamesColumn1[i]
                .Draw(spriteBatch, new Vector2(leftX, leftY + i * ROW_HEIGHT));

        // Draw offline friends (right column)
        var rightX = sx + RightColumnRect.X;
        var rightY = sy + RightColumnRect.Y;

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
            NamesColumn2[i]
                .Draw(spriteBatch, new Vector2(rightX, rightY + i * ROW_HEIGHT));
    }

    /// <summary>
    ///     Returns all friend names in the current list.
    /// </summary>
    public List<string> GetFriendNames()
        => Friends.Select(f => f.Name)
                  .ToList();

    public override void Hide()
    {
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
                NamesColumn1[i]
                    .Update(onlineFriends.Array[i].Name, new Color(150, 255, 150));
            else
                NamesColumn1[i]
                    .Update(string.Empty, Color.White);

            if (i < offlineFriends.Count)
                NamesColumn2[i]
                    .Update(offlineFriends.Array[i].Name, new Color(150, 150, 150));
            else
                NamesColumn2[i]
                    .Update(string.Empty, Color.White);
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
        Slide.SlideIn(this);
        SlideMode = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
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

        if (input.WasKeyPressed(Keys.Escape))
        {
            Close();

            return;
        }

        base.Update(gameTime, input);
    }
}