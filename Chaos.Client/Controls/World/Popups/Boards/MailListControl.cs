#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.Scrolling;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Mail list panel using _nmaill prefab. Displays a scrollable list of mail posts with author, date, and subject.
///     Buttons: View, New, Reply, Delete, Up (back to boards), Quit (close).
/// </summary>
public sealed class MailListControl : PrefabPanel
{
    //server caps board responses at sbyte.maxvalue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int TEXT_INDENT = 24;
    private const int POSTID_CHARS = 6;
    private const int AUTHOR_CHARS = 17;
    private const int DATE_CHARS = 7;
    private const int PREFIX_CHARS = POSTID_CHARS + AUTHOR_CHARS + DATE_CHARS;

    private readonly int MaxSubjectChars;
    private readonly VirtualizedRowList<MailEntry> RowList;

    private List<MailEntry> Entries = [];
    private int TargetX;

    public ushort BoardId { get; private set; }
    public string CurrentAuthor => RowList.SelectedItem?.Author ?? string.Empty;
    public UIButton? DeleteButton { get; }
    public UIButton? NewButton { get; }

    public UIButton? QuitButton { get; }
    public UIButton? ReplyButton { get; }
    public UIButton? UpButton { get; }

    public UIButton? ViewButton { get; }

    public MailListControl()
        : base("_nmaill", false)
    {
        Name = "MailList";
        Visible = false;
        UsesControlStack = true;

        ViewButton = CreateButton("View");
        NewButton = CreateButton("New");
        ReplyButton = CreateButton("Reply");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        QuitButton = CreateButton("Quit");

        var mailListRect = GetRect("MailList");

        //columns are faked via fixed-width string formatting; subject is truncated to whatever fits past the
        //postid/author/date prefix in the box that remains after the scrollbar gutter and the row text indent.
        var usableWidth = mailListRect.Width - ScrollBarControl.DEFAULT_WIDTH;
        MaxSubjectChars = Math.Max(0, ((usableWidth - TEXT_INDENT) / TextRenderer.CHAR_WIDTH) - PREFIX_CHARS);

        RowList = new VirtualizedRowList<MailEntry>(
            mailListRect.Width,
            mailListRect.Height,
            ROW_HEIGHT,
            static () => new UILabel
            {
                PaddingLeft = TEXT_INDENT,
                PaddingTop = 0
            },
            BindRow,
            selectable: true)
        {
            SentinelBinder = BindSentinel,
            SentinelActivated = () =>
            {
                if (Entries.Count > 0)
                    OnLoadMorePosts?.Invoke(Entries[^1].PostId);
            }
        };

        RowList.SelectionChanged += UpdateButtonStates;
        RowList.RowActivated += entry => OnViewPost?.Invoke(entry.PostId);

        var viewer = new ScrollViewerControl(RowList)
        {
            X = mailListRect.X,
            Y = mailListRect.Y,
            Width = mailListRect.Width,
            Height = mailListRect.Height
        };

        AddChild(viewer);

        //button handlers are wired after RowList exists so the selection-reading lambdas capture a non-null list
        if (QuitButton is not null)
            QuitButton.Clicked += () => OnClose?.Invoke();

        if (ViewButton is not null)
            ViewButton.Clicked += () =>
            {
                if (RowList.SelectedItem is { } entry)
                    OnViewPost?.Invoke(entry.PostId);
            };

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewMail?.Invoke();

        if (ReplyButton is not null)
            ReplyButton.Clicked += () =>
            {
                if (RowList.SelectedItem is { } entry)
                    OnReplyPost?.Invoke(entry.PostId);
            };

        if (DeleteButton is not null)
            DeleteButton.Clicked += () =>
            {
                if (RowList.SelectedItem is { } entry)
                    OnDeletePost?.Invoke(entry.PostId);
            };

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();
    }

    public void AppendEntries(List<MailEntry> entries)
    {
        Entries.AddRange(entries);
        RowList.ShowSentinel = entries.Count >= MAX_POSTS_PER_PAGE;
        RowList.Invalidate();
    }

    private void BindRow(UIElement row, MailEntry entry, bool selected)
    {
        var label = (UILabel)row;

        label.ForegroundColor = selected
            ? new Color(100, 149, 237)
            : entry.IsHighlighted
                ? Color.Yellow
                : TextColors.Default;
        label.Text = FormatRow(entry);
    }

    private static void BindSentinel(UIElement row)
    {
        var label = (UILabel)row;
        label.ForegroundColor = Color.LightGray;
        label.Text = "-- Load More --";
    }

    private string FormatRow(MailEntry entry)
    {
        var subject = entry.Subject.Length > MaxSubjectChars ? entry.Subject[..MaxSubjectChars] : entry.Subject;

        return $"{entry.PostId,-POSTID_CHARS}{entry.Author,-AUTHOR_CHARS}{entry.Month + "/" + entry.Day,-DATE_CHARS}{subject}";
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CloseHandler? OnClose;
    public event DeletePostHandler? OnDeletePost;

    /// <summary>
    ///     Fired when the user clicks the "Load More" row at the bottom of a full page. The short is the last visible PostId
    ///     to use as the startPostId for the next page request.
    /// </summary>
    public event LoadMorePostsHandler? OnLoadMorePosts;

    public event NewMailHandler? OnNewMail;
    public event ReplyPostHandler? OnReplyPost;
    public event UpHandler? OnUp;
    public event ViewPostHandler? OnViewPost;

    /// <summary>
    ///     Removes a post from the list (after a successful delete) and clamps the selection to the new bounds.
    /// </summary>
    public void RemoveEntry(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        Entries.RemoveAt(index);
        RowList.Invalidate();
    }

    public void SetViewportBounds(Rectangle viewport)
    {
        TargetX = viewport.X + viewport.Width - Width;
        Y = viewport.Y;
    }

    public override void Show()
    {
        X = TargetX;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Populates the mail list from server data (first page).
    /// </summary>
    public void ShowMailList(ushort boardId, List<MailEntry> entries)
    {
        BoardId = boardId;
        Entries = entries;
        RowList.ShowSentinel = entries.Count >= MAX_POSTS_PER_PAGE;
        RowList.SetItems(Entries);
        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            OnUp?.Invoke();
            e.Handled = true;
        }
    }

    private void UpdateButtonStates()
    {
        var hasSelection = RowList.HasSelection;

        ViewButton?.Enabled = hasSelection;

        DeleteButton?.Enabled = hasSelection;

        ReplyButton?.Enabled = hasSelection;
    }
}
