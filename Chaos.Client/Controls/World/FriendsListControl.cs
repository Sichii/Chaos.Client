#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Friends list popup using _nfriend prefab. Two-column layout: left column (online friends), right column (offline
///     friends). Row height 16px. OK/Cancel buttons at bottom.
/// </summary>
public sealed class FriendsListControl : PrefabPanel
{
    private const int ROW_HEIGHT = 16;
    private const int MAX_VISIBLE_ROWS = 12;
    private const float SLIDE_DURATION_MS = 250f;

    private readonly Rectangle LeftColumnRect;

    private readonly CachedText[] LeftNameCaches = new CachedText[MAX_VISIBLE_ROWS];
    private readonly Rectangle RightColumnRect;
    private readonly CachedText[] RightNameCaches = new CachedText[MAX_VISIBLE_ROWS];
    private int DataVersion;

    private List<FriendEntry> Friends = [];
    private int OffScreenX;
    private int RenderedVersion = -1;
    private int SlideAnchorY;
    private bool SlideMode;
    private float SlideTimer;
    private bool Sliding;
    private bool SlidingOut;

    // Slide animation
    private int TargetX;

    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public FriendsListControl(GraphicsDevice device)
        : base(device, "_nfriend", false)
    {
        Name = "FriendsList";
        Visible = false;

        var elements = AutoPopulate();

        CancelButton = elements.GetValueOrDefault("Cancel") as UIButton;
        OkButton = elements.GetValueOrDefault("OK") as UIButton;

        if (CancelButton is not null)
            CancelButton.OnClick += Close;

        if (OkButton is not null)
            OkButton.OnClick += Close;

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
            LeftNameCaches[i] = new CachedText(device);
            RightNameCaches[i] = new CachedText(device);
        }
    }

    private void Close()
    {
        if (SlideMode)
            SlideOut();
        else
        {
            Hide();
            OnClose?.Invoke();
        }
    }

    public override void Dispose()
    {
        foreach (var c in LeftNameCaches)
            c.Dispose();

        foreach (var c in RightNameCaches)
            c.Dispose();

        base.Dispose();
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
            LeftNameCaches[i]
                .Draw(spriteBatch, new Vector2(leftX, leftY + i * ROW_HEIGHT));

        // Draw offline friends (right column)
        var rightX = sx + RightColumnRect.X;
        var rightY = sy + RightColumnRect.Y;

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
            RightNameCaches[i]
                .Draw(spriteBatch, new Vector2(rightX, rightY + i * ROW_HEIGHT));
    }

    public override void Hide()
    {
        Visible = false;
        Sliding = false;

        if (SlideMode)
            X = OffScreenX;
    }

    public event Action<string>? OnAddFriend;

    public event Action? OnClose;
    public event Action<string>? OnRemoveFriend;

    private void RefreshCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        var onlineFriends = Friends.Where(f => f.IsOnline)
                                   .ToList();

        var offlineFriends = Friends.Where(f => !f.IsOnline)
                                    .ToList();

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            if (i < onlineFriends.Count)
                LeftNameCaches[i]
                    .Update(onlineFriends[i].Name, new Color(150, 255, 150));
            else
                LeftNameCaches[i]
                    .Update(string.Empty, Color.White);

            if (i < offlineFriends.Count)
                RightNameCaches[i]
                    .Update(offlineFriends[i].Name, new Color(150, 150, 150));
            else
                RightNameCaches[i]
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
        OffScreenX = anchorX;
        TargetX = anchorX - Width;
        SlideAnchorY = anchorY;
    }

    /// <summary>
    ///     Shows immediately at top-center of screen (hotkey mode).
    /// </summary>
    public override void Show()
    {
        X = (640 - Width) / 2;
        Y = 0;
        Visible = true;
        Sliding = false;
        SlideMode = false;
    }

    /// <summary>
    ///     Slides out from the left edge of MainOptionsControl (button mode).
    /// </summary>
    public void SlideIn()
    {
        if (Visible)
            return;

        X = OffScreenX;
        Y = SlideAnchorY;
        Visible = true;
        Sliding = true;
        SlidingOut = false;
        SlideMode = true;
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
            Close();

            return;
        }

        base.Update(gameTime, input);
    }
}