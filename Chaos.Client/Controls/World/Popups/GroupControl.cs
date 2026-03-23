#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Group/party panel using _ngcdlg0 (empty/invite mode) prefab. Displays group member slots (USER0/USER1) and an
///     invite button. When in a group, shows member names and a "leave" option. BTN_OK confirms actions.
/// </summary>
public sealed class GroupControl : PrefabPanel
{
    private const int MAX_MEMBERS = 13;
    private const int ROW_HEIGHT = 22;
    private const int NAME_X = 53;
    private const int NAME_START_Y = 47;

    private readonly UILabel?[] MemberLabels = new UILabel?[MAX_MEMBERS];
    private int DataVersion;

    private List<string> Members = [];
    private int RenderedVersion = -1;

    public UIButton? InviteButton { get; }
    public UIButton? OkButton { get; }

    public GroupControl(GraphicsDevice device)
        : base(device, "_ngcdlg0", false)
    {
        Name = "Group";
        Visible = false;

        // Position at top-left of screen
        X = 0;
        Y = 0;

        var elements = AutoPopulate();

        InviteButton = elements.GetValueOrDefault("B_BTN0") as UIButton;
        OkButton = elements.GetValueOrDefault("BTN_OK") as UIButton;

        if (OkButton is not null)
            OkButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        if (InviteButton is not null)
            InviteButton.OnClick += () => OnInvite?.Invoke();

        // Create member name labels from USER0/USER1 prefab controls (and extras manually)
        for (var i = 0; i < MAX_MEMBERS; i++)
        {
            var controlName = $"USER{i}";

            if (elements.TryGetValue(controlName, out var element))
            {
                // Replace the image with a label at the same position
                MemberLabels[i] = new UILabel(device)
                {
                    Name = controlName,
                    X = element.X,
                    Y = element.Y,
                    Width = element.Width,
                    Height = element.Height
                };

                AddChild(MemberLabels[i]!);
            } else if (i < 2)
            {
                // Create default labels for USER0/USER1 if not found in prefab
                MemberLabels[i] = new UILabel(device)
                {
                    Name = controlName,
                    X = NAME_X,
                    Y = NAME_START_Y + i * ROW_HEIGHT,
                    Width = 72,
                    Height = 12
                };

                AddChild(MemberLabels[i]!);
            }
        }
    }

    /// <summary>
    ///     Clears the group state (left or disbanded).
    /// </summary>
    public void ClearGroup()
    {
        Members.Clear();
        DataVersion++;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshLabels();

        base.Draw(spriteBatch);
    }

    public event Action? OnClose;
    public event Action? OnInvite;
    #pragma warning disable CS0067 // not yet wired
    public event Action? OnLeave;
    #pragma warning restore CS0067

    private void RefreshLabels()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MAX_MEMBERS; i++)
        {
            if (MemberLabels[i] is null)
                continue;

            if (i < Members.Count)
                MemberLabels[i]!.SetText(Members[i], Color.White);
            else
                MemberLabels[i]!.SetText(string.Empty);
        }
    }

    /// <summary>
    ///     Updates the group display with member names.
    /// </summary>
    public void SetMembers(List<string> members)
    {
        Members = members;
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