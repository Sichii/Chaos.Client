#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Mail reading panel using _nmailr prefab. Displays a received mail message with author, title, date, and body text.
///     Buttons: Prev, Next, New, Reply, Delete, Up (back to list), Quit (close).
/// </summary>
public sealed class MailReadControl : PrefabPanel
{
    private const int BULLETIN_RECT_LEFT = 8;
    private const int BULLETIN_RECT_BOTTOM = 304;
    private const int LINE_HEIGHT = 12;

    private readonly UILabel? AuthorLabel;
    private readonly Rectangle ContentRect;
    private readonly UILabel? DateLabel;
    private readonly CachedText[] LineCaches;

    private readonly int MaxVisibleLines;
    private readonly UILabel? TitleLabel;
    private int DataVersion;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private List<string> WrappedLines = [];
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

    public MailReadControl()
        : base("_nmailr", false)
    {
        Name = "MailRead";
        X = BULLETIN_RECT_LEFT;
        Y = BULLETIN_RECT_BOTTOM - Height;
        Visible = false;

        PrevButton = CreateButton("Prev");
        NextButton = CreateButton("Next");
        NewButton = CreateButton("New");
        ReplyButton = CreateButton("Reply");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        QuitButton = CreateButton("Quit");

        if (QuitButton is not null)
            QuitButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        if (UpButton is not null)
            UpButton.OnClick += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

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

        // Content rect
        ContentRect = GetRect("Content");
        MaxVisibleLines = ContentRect.Height > 0 ? ContentRect.Height / LINE_HEIGHT : 0;

        LineCaches = new CachedText[MaxVisibleLines];

        for (var i = 0; i < MaxVisibleLines; i++)
            LineCaches[i] = new CachedText();
    }

    public override void Dispose()
    {
        foreach (var c in LineCaches)
            c.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if ((MaxVisibleLines == 0) || (WrappedLines.Count == 0))
            return;

        RefreshLineCaches();

        var contentX = ScreenX + ContentRect.X;
        var contentY = ScreenY + ContentRect.Y;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            var lineIndex = ScrollOffset + i;

            if (lineIndex >= WrappedLines.Count)
                break;

            LineCaches[i]
                .Draw(spriteBatch, new Vector2(contentX, contentY + i * LINE_HEIGHT));
        }
    }

    public event Action? OnClose;
    public event Action<short>? OnDeletePost;
    public event Action? OnNewMail;
    public event Action? OnNext;
    public event Action? OnPrev;
    public event Action<short>? OnReplyPost;

    private void RefreshLineCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            var lineIndex = ScrollOffset + i;

            LineCaches[i]
                .Update(lineIndex < WrappedLines.Count ? WrappedLines[lineIndex] : string.Empty, Color.White);
        }
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
        ScrollOffset = 0;
        DataVersion++;

        AuthorLabel?.SetText(author);
        TitleLabel?.SetText(subject);
        DateLabel?.SetText($"{month}/{day}");

        PrevButton?.Enabled = enablePrev;

        // Public boards don't support reply (no recipient)
        if (ReplyButton is not null)
            ReplyButton.Visible = !IsPublicBoard;

        // Word-wrap message into lines
        WrappedLines = TextRenderer.WrapLines(message, ContentRect.Width);

        Show();
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

        // Scroll wheel
        if ((input.ScrollDelta != 0) && (WrappedLines.Count > MaxVisibleLines))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, WrappedLines.Count - MaxVisibleLines);

            DataVersion++;
        }
    }
}