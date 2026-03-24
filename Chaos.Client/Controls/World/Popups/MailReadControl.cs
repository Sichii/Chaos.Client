#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Mail reading panel using _nmailr prefab. Displays a received mail message with author, title, date, and body text.
///     Buttons: Prev, Next, New, Reply, Delete, Up (back to list), Quit (close).
/// </summary>
public sealed class MailReadControl : PrefabPanel
{
    private readonly UILabel? AuthorLabel;
    private readonly UILabel BodyLabel;
    private readonly UILabel? DateLabel;
    private readonly UILabel? TitleLabel;
    private readonly int VisibleHeight;
    private int TargetX;

    public ushort BoardId { get; set; }
    public string CurrentAuthor { get; private set; } = string.Empty;

    public short CurrentPostId { get; private set; }
    public bool IsPublicBoard { get; set; }
    public UIButton? DeleteButton { get; }
    public UIButton? NewButton { get; }
    public UIButton? NextButton { get; }

    public UIButton? PrevButton { get; }
    public UIButton? QuitButton { get; }
    public UIButton? ReplyButton { get; }
    public UIButton? UpButton { get; }

    public MailReadControl(string prefabName = "_nmailr")
        : base(prefabName, false)
    {
        Name = "MailRead";
        Visible = false;

        PrevButton = CreateButton("Prev");
        NextButton = CreateButton("Next");
        NewButton = CreateButton("New");
        ReplyButton = CreateButton("Reply");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        QuitButton = CreateButton("Quit") ?? CreateButton("Close");

        if (QuitButton is not null)
            QuitButton.OnClick += () => OnQuit?.Invoke();

        if (UpButton is not null)
            UpButton.OnClick += () => OnUp?.Invoke();

        if (PrevButton is not null)
            PrevButton.OnClick += () => OnPrev?.Invoke();

        if (NextButton is not null)
            NextButton.OnClick += () => OnNext?.Invoke();

        if (NewButton is not null)
            NewButton.OnClick += () => OnNewMail?.Invoke();

        if (ReplyButton is not null)
            ReplyButton.OnClick += () => OnReplyPost?.Invoke(CurrentPostId);

        if (DeleteButton is not null)
            DeleteButton.OnClick += () => OnDeletePost?.Invoke(CurrentPostId);

        // Labels
        AuthorLabel = CreateLabel("Author");
        TitleLabel = CreateLabel("Title");
        DateLabel = CreateLabel("Mmdd");

        // Body content area
        var contentRect = GetRect("Content");
        VisibleHeight = contentRect.Height;

        BodyLabel = new UILabel
        {
            X = contentRect.X,
            Y = contentRect.Y,
            Width = contentRect.Width,
            Height = contentRect.Height,
            PaddingLeft = 0,
            PaddingTop = 0
        };

        AddChild(BodyLabel);
    }

    public override void Hide() => Visible = false;

    public event Action<short>? OnDeletePost;
    public event Action? OnNewMail;
    public event Action? OnNext;
    public event Action? OnPrev;
    public event Action? OnQuit;
    public event Action<short>? OnReplyPost;
    public event Action? OnUp;

    public void SetViewportBounds(Rectangle viewport)
    {
        TargetX = viewport.X + viewport.Width - Width;
        Y = viewport.Y;
    }

    public override void Show()
    {
        X = TargetX;
        Visible = true;
    }

    /// <summary>
    ///     Displays a mail message.
    /// </summary>
    public void ShowMail(
        short postId,
        string author,
        int month,
        int day,
        string subject,
        string message,
        bool enablePrev)
    {
        CurrentPostId = postId;
        CurrentAuthor = author;

        AuthorLabel?.SetText(author);
        TitleLabel?.SetText(subject);
        DateLabel?.SetText($"{month}/{day}");

        PrevButton?.Enabled = enablePrev;

        // Public boards don't support reply (no recipient)
        if (ReplyButton is not null)
            ReplyButton.Visible = !IsPublicBoard;

        BodyLabel.ScrollOffset = 0;
        BodyLabel.SetWrappedText(message, Color.White);

        Show();
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            OnUp?.Invoke();

            return;
        }

        base.Update(gameTime, input);

        // Scroll wheel
        if (input.ScrollDelta != 0)
        {
            var maxScroll = Math.Max(0, BodyLabel.ContentHeight - VisibleHeight);
            BodyLabel.ScrollOffset = Math.Clamp(BodyLabel.ScrollOffset - input.ScrollDelta * TextRenderer.CHAR_HEIGHT, 0, maxScroll);
        }
    }
}