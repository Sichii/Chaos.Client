#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Friends list popup using _nfriend prefab. Two-column layout: left column (online friends), right column (offline
///     friends). Row height 16px. OK/Cancel buttons at bottom.
/// </summary>
public class FriendsListControl : PrefabPanel
{
    private const int ROW_HEIGHT = 16;
    private const int MAX_VISIBLE_ROWS = 12;
    private readonly GraphicsDevice DeviceRef;

    private readonly Rectangle LeftColumnRect;

    private readonly CachedText[] LeftNameCaches = new CachedText[MAX_VISIBLE_ROWS];
    private readonly Rectangle RightColumnRect;
    private readonly CachedText[] RightNameCaches = new CachedText[MAX_VISIBLE_ROWS];
    private int DataVersion;

    private List<FriendEntry> Friends = [];
    private int RenderedVersion = -1;
    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public FriendsListControl(GraphicsDevice device)
        : base(device, "_nfriend")
    {
        DeviceRef = device;
        Name = "FriendsList";
        Visible = false;

        var elements = AutoPopulate();

        CancelButton = elements.GetValueOrDefault("Cancel") as UIButton;
        OkButton = elements.GetValueOrDefault("OK") as UIButton;

        if (CancelButton is not null)
            CancelButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        if (OkButton is not null)
            OkButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

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
                    .Update(onlineFriends[i].Name, 0, new Color(150, 255, 150));
            else
                LeftNameCaches[i]
                    .Update(string.Empty, 0, Color.White);

            if (i < offlineFriends.Count)
                RightNameCaches[i]
                    .Update(offlineFriends[i].Name, 0, new Color(150, 150, 150));
            else
                RightNameCaches[i]
                    .Update(string.Empty, 0, Color.White);
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

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime, input);
    }
}

/// <summary>
///     A single entry in the friends list.
/// </summary>
public record FriendEntry(string Name, bool IsOnline);