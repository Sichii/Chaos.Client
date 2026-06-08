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
///     Public board article list panel using _narlist prefab. Displays a scrollable list of board posts with author, date,
///     and subject. Buttons: View, New, Delete, Hilight, Up (back to boards), Close.
/// </summary>
public sealed class ArticleListControl : PrefabPanel
{
    //server caps board responses at sbyte.maxvalue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int POSTID_CHARS = 5;
    private const int AUTHOR_CHARS = 12;
    private const int DATE_CHARS = 5;
    private const int PREFIX_CHARS = POSTID_CHARS + AUTHOR_CHARS + DATE_CHARS;
    private const string SPACER5 = "     ";
    private const string SPACER3 = "   ";

    private readonly int MaxSubjectChars;
    private readonly VirtualizedRowList<MailEntry> RowList;

    private List<MailEntry> Entries = [];
    private int TargetX;

    public ushort BoardId { get; private set; }
    public UIButton? CloseButton { get; }
    public UIButton? DeleteButton { get; }
    public UIButton? HighlightButton { get; }
    public UIButton? NewButton { get; }
    public UIButton? UpButton { get; }
    public UIButton? ViewButton { get; }

    public ArticleListControl()
        : base("_narlist", false)
    {
        Name = "ArticleList";
        Visible = false;
        UsesControlStack = true;

        ViewButton = CreateButton("View");
        NewButton = CreateButton("New");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        CloseButton = CreateButton("Close");
        HighlightButton = CreateButton("Hilight");

        if (HighlightButton is not null)
            HighlightButton.Visible = false;

        var articleListRect = GetRect("ArticleList");

        //columns are faked via fixed-width string formatting; subject is truncated to whatever fits past the
        //postid/author/date prefix in the box that remains after the scrollbar gutter.
        var usableWidth = articleListRect.Width - ScrollBarControl.DEFAULT_WIDTH;
        MaxSubjectChars = Math.Max(0, (usableWidth / TextRenderer.CHAR_WIDTH) - PREFIX_CHARS);

        RowList = new VirtualizedRowList<MailEntry>(
            articleListRect.Width,
            articleListRect.Height,
            ROW_HEIGHT,
            static () => new UILabel
            {
                PaddingLeft = 0,
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
            X = articleListRect.X,
            Y = articleListRect.Y,
            Width = articleListRect.Width,
            Height = articleListRect.Height
        };

        AddChild(viewer);

        //button handlers are wired after RowList exists so the selection-reading lambdas capture a non-null list
        if (CloseButton is not null)
            CloseButton.Clicked += () => OnClose?.Invoke();

        if (ViewButton is not null)
            ViewButton.Clicked += () =>
            {
                if (RowList.SelectedItem is { } entry)
                    OnViewPost?.Invoke(entry.PostId);
            };

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewPost?.Invoke();

        if (DeleteButton is not null)
            DeleteButton.Clicked += () =>
            {
                if (RowList.SelectedItem is { } entry)
                    OnDeletePost?.Invoke(entry.PostId);
            };

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();

        if (HighlightButton is not null)
            HighlightButton.Clicked += () =>
            {
                if (RowList.SelectedItem is { } entry)
                    OnHighlight?.Invoke(entry.PostId);
            };
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

        var date = $"{entry.Month,2}/{entry.Day,2}";

        return $"{entry.PostId,POSTID_CHARS}{SPACER5}{entry.Author,-AUTHOR_CHARS}{SPACER5}{date,DATE_CHARS}{SPACER3}{subject}";
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CloseHandler? OnClose;
    public event DeletePostHandler? OnDeletePost;
    public event HighlightPostHandler? OnHighlight;

    /// <summary>
    ///     Fired when the user clicks the "Load More" row at the bottom of a full page. The short is the last visible PostId
    ///     to use as the startPostId for the next page request.
    /// </summary>
    public event LoadMorePostsHandler? OnLoadMorePosts;

    public event NewPostHandler? OnNewPost;
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

    public void ToggleHighlight(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        var entry = Entries[index];
        Entries[index] = entry with { IsHighlighted = !entry.IsHighlighted };
        RowList.Invalidate();
    }

    /// <summary>
    ///     Shows or hides the Highlight button based on GM status.
    /// </summary>
    public void SetHighlightEnabled(bool enabled) => HighlightButton?.Visible = enabled;

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
    ///     Populates the article list from server data (first page).
    /// </summary>
    public void ShowArticles(ushort boardId, List<MailEntry> entries)
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

        if (HighlightButton is { Visible: true })
            HighlightButton.Enabled = hasSelection;
    }
}
