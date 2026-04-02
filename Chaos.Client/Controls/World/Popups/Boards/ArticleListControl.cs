#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Public board article list panel using _narlist prefab. Displays a scrollable list of board posts with author, date,
///     and subject. Buttons: View, New, Delete, Hilight, Up (back to boards), Close.
/// </summary>
public sealed class ArticleListControl : PrefabPanel
{
    // Server caps board responses at sbyte.MaxValue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = 18;
    private const int TEXT_INDENT = 24;
    private const int POSTID_CHARS = 6;
    private const int AUTHOR_CHARS = 17;
    private const int DATE_CHARS = 7;
    private const int PREFIX_CHARS = POSTID_CHARS + AUTHOR_CHARS + DATE_CHARS;

    private readonly Rectangle ArticleListRect;
    private readonly int MaxSubjectChars;
    private readonly int MaxVisibleRows;
    private readonly UILabel[] RowLabels;
    private readonly ScrollBarControl ScrollBar;
    private int DataVersion;

    private List<MailEntry> Entries = [];
    private bool HasMorePosts;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;
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

        ViewButton = CreateButton("View");
        NewButton = CreateButton("New");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        CloseButton = CreateButton("Close");

        if (CloseButton is not null)
            CloseButton.OnClick += () => OnClose?.Invoke();

        if (ViewButton is not null)
            ViewButton.OnClick += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnViewPost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (NewButton is not null)
            NewButton.OnClick += () => OnNewPost?.Invoke();

        if (DeleteButton is not null)
            DeleteButton.OnClick += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnDeletePost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (UpButton is not null)
            UpButton.OnClick += () => OnUp?.Invoke();

        HighlightButton = CreateButton("Hilight");

        if (HighlightButton is not null)
        {
            HighlightButton.Visible = false;

            HighlightButton.OnClick += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnHighlight?.Invoke(Entries[SelectedIndex].PostId);
            };
        }

        ArticleListRect = GetRect("ArticleList");
        MaxVisibleRows = ArticleListRect.Height > 0 ? ArticleListRect.Height / ROW_HEIGHT : 0;

        // Scrollbar
        ScrollBar = new ScrollBarControl
        {
            Name = "ScrollBar",
            X = ArticleListRect.X + ArticleListRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = ArticleListRect.Y,
            Height = ArticleListRect.Height
        };

        ScrollBar.OnValueChanged += v =>
        {
            ScrollOffset = v;
            DataVersion++;
        };

        AddChild(ScrollBar);

        // Row labels — one per visible row, columns via fixed-width string formatting
        var usableWidth = ArticleListRect.Width - ScrollBarControl.DEFAULT_WIDTH;
        MaxSubjectChars = Math.Max(0, (usableWidth - TEXT_INDENT) / TextRenderer.CHAR_WIDTH - PREFIX_CHARS);

        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                X = ArticleListRect.X + TEXT_INDENT,
                Y = ArticleListRect.Y + i * ROW_HEIGHT,
                Width = usableWidth - TEXT_INDENT,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(RowLabels[i]);
        }
    }

    public void AppendEntries(List<MailEntry> entries)
    {
        Entries.AddRange(entries);
        HasMorePosts = entries.Count >= MAX_POSTS_PER_PAGE;
        DataVersion++;

        UpdateScrollBar();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshLabels();
        base.Draw(spriteBatch);
    }

    private string FormatRow(MailEntry entry)
    {
        var subject = entry.Subject.Length > MaxSubjectChars ? entry.Subject[..MaxSubjectChars] : entry.Subject;

        return $"{entry.PostId,-POSTID_CHARS}{entry.Author,-AUTHOR_CHARS}{entry.Month + "/" + entry.Day,-DATE_CHARS}{subject}";
    }

    public override void Hide() => Visible = false;

    public event Action? OnClose;
    public event Action<short>? OnDeletePost;
    public event Action<short>? OnHighlight;

    /// <summary>
    ///     Fired when the user clicks the "Load More" row at the bottom of a full page. The short is the last visible PostId
    ///     to use as the startPostId for the next page request.
    /// </summary>
    public event Action<short>? OnLoadMorePosts;

    public event Action? OnNewPost;
    public event Action? OnUp;
    public event Action<short>? OnViewPost;

    private void RefreshLabels()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (HasMorePosts && (entryIndex == Entries.Count))
            {
                RowLabels[i].ForegroundColor = Color.LightGray;
                RowLabels[i].Text = "-- Load More --";
            } else if (entryIndex < Entries.Count)
            {
                var entry = Entries[entryIndex];
                var isSelected = entryIndex == SelectedIndex;

                var textColor = isSelected
                    ? new Color(100, 149, 237)
                    : entry.IsHighlighted
                        ? Color.Yellow
                        : Color.White;

                RowLabels[i].ForegroundColor = textColor;
                RowLabels[i].Text = FormatRow(entry);
            } else
                RowLabels[i].Text = string.Empty;
        }
    }

    /// <summary>
    ///     Appends additional entries from a subsequent page to the existing list.
    /// </summary>
    public void RemoveEntry(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        Entries.RemoveAt(index);

        if (SelectedIndex >= Entries.Count)
            SelectedIndex = Entries.Count - 1;

        DataVersion++;
        UpdateScrollBar();
    }

    /// <summary>
    ///     Shows or hides the Highlight button based on GM status.
    /// </summary>
    public void SetHighlightEnabled(bool enabled)
    {
        if (HighlightButton is not null)
            HighlightButton.Visible = enabled;
    }

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
    ///     Populates the article list from server data (first page).
    /// </summary>
    public void ShowArticles(ushort boardId, List<MailEntry> entries)
    {
        BoardId = boardId;
        Entries = entries;
        HasMorePosts = entries.Count >= MAX_POSTS_PER_PAGE;
        SelectedIndex = -1;
        ScrollOffset = 0;
        DataVersion++;

        UpdateScrollBar();
        UpdateButtonStates();
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

        var totalRows = Entries.Count + (HasMorePosts ? 1 : 0);

        // Click to select row or trigger "Load More"
        if (input.WasLeftButtonPressed)
        {
            var mx = input.MouseX - ScreenX - ArticleListRect.X;
            var my = input.MouseY - ScreenY - ArticleListRect.Y;

            if ((mx >= 0) && (mx < (ArticleListRect.Width - 16)) && (my >= 0) && (my < ArticleListRect.Height))
            {
                var row = my / ROW_HEIGHT;
                var entryIndex = ScrollOffset + row;

                // Clicked the "Load More" virtual row
                if (HasMorePosts && (entryIndex == Entries.Count) && (Entries.Count > 0))
                {
                    var lastPostId = Entries[^1].PostId;
                    OnLoadMorePosts?.Invoke(lastPostId);
                } else if (entryIndex < Entries.Count)
                {
                    if (input.WasLeftButtonDoubleClicked && (entryIndex == SelectedIndex))
                        OnViewPost?.Invoke(Entries[entryIndex].PostId);
                    else
                    {
                        SelectedIndex = entryIndex;
                        DataVersion++;
                        UpdateButtonStates();
                    }
                }
            }
        }

        // Scroll wheel
        if ((input.ScrollDelta != 0) && (totalRows > MaxVisibleRows))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, totalRows - MaxVisibleRows);
            ScrollBar.Value = ScrollOffset;
            DataVersion++;
        }
    }

    private void UpdateButtonStates()
    {
        var hasSelection = (SelectedIndex >= 0) && (SelectedIndex < Entries.Count);

        if (ViewButton is not null)
            ViewButton.Enabled = hasSelection;

        if (DeleteButton is not null)
            DeleteButton.Enabled = hasSelection;

        if (HighlightButton is { Visible: true })
            HighlightButton.Enabled = hasSelection;
    }

    private void UpdateScrollBar()
    {
        // Add 1 virtual row for the "Load More" indicator when more posts exist
        var totalRows = Entries.Count + (HasMorePosts ? 1 : 0);

        ScrollBar.TotalItems = totalRows;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, totalRows - MaxVisibleRows);
    }
}