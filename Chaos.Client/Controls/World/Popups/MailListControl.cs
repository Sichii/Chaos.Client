#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Mail list panel using _nmaill prefab. Displays a scrollable list of mail posts with author, date, and subject.
///     Buttons: View, New, Reply, Delete, Up (back to boards), Quit (close).
/// </summary>
public sealed class MailListControl : PrefabPanel
{
    private const float DOUBLE_CLICK_MS = 400f;

    // Server caps board responses at sbyte.MaxValue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = 16;
    private const int TEXT_INDENT = 24;
    private const int POSTID_CHARS = 6;
    private const int AUTHOR_CHARS = 17;
    private const int DATE_CHARS = 7;

    private readonly Rectangle MailListRect;
    private readonly int MaxVisibleRows;
    private readonly UILabel[] RowLabels;
    private readonly ScrollBarControl ScrollBar;
    private int DataVersion;

    private List<MailEntry> Entries = [];
    private bool HasMorePosts;
    private float LastClickTime;

    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;
    private int TargetX;

    public ushort BoardId { get; private set; }
    public bool IsPublicBoard { get; private set; }
    public UIButton? DeleteButton { get; }
    public UIButton? HighlightButton { get; }
    public UIButton? NewButton { get; }

    public UIButton? QuitButton { get; }
    public UIButton? ReplyButton { get; }
    public UIButton? UpButton { get; }

    public UIButton? ViewButton { get; }

    public MailListControl(string prefabName = "_nmaill")
        : base(prefabName, false)
    {
        Name = "MailList";
        Visible = false;

        ViewButton = CreateButton("View");
        NewButton = CreateButton("New");
        ReplyButton = CreateButton("Reply");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        QuitButton = CreateButton("Quit") ?? CreateButton("Close");

        if (QuitButton is not null)
            QuitButton.OnClick += () => OnClose?.Invoke();

        if (ViewButton is not null)
            ViewButton.OnClick += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnViewPost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (NewButton is not null)
            NewButton.OnClick += () => OnNewMail?.Invoke();

        if (ReplyButton is not null)
            ReplyButton.OnClick += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnReplyPost?.Invoke(Entries[SelectedIndex].PostId);
            };

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

        // List rect for scrollable area — "MailList" in _nmaill, "ArticleList" in _narlist
        var listRect = GetRect("MailList");

        if (listRect.Width == 0)
            listRect = GetRect("ArticleList");

        MailListRect = listRect;
        MaxVisibleRows = MailListRect.Height > 0 ? MailListRect.Height / ROW_HEIGHT : 0;

        // Scrollbar
        ScrollBar = new ScrollBarControl
        {
            Name = "ScrollBar",
            X = MailListRect.X + MailListRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = MailListRect.Y,
            Height = MailListRect.Height
        };

        ScrollBar.OnValueChanged += v =>
        {
            ScrollOffset = v;
            DataVersion++;
        };
        AddChild(ScrollBar);

        // Row labels — one per visible row, columns via fixed-width string formatting
        var usableWidth = MailListRect.Width - ScrollBarControl.DEFAULT_WIDTH;

        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                X = MailListRect.X + TEXT_INDENT,
                Y = MailListRect.Y + i * ROW_HEIGHT,
                Width = usableWidth - TEXT_INDENT,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(RowLabels[i]);
        }
    }

    /// <summary>
    ///     Appends additional entries from a subsequent page to the existing list.
    /// </summary>
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

    private static string FormatRow(MailEntry entry)
        => $"{entry.PostId,-POSTID_CHARS}{entry.Author,-AUTHOR_CHARS}{entry.Month + "/" + entry.Day,-DATE_CHARS}{entry.Subject}";

    public override void Hide() => Visible = false;

    public event Action? OnClose;
    public event Action<short>? OnDeletePost;
    public event Action<short>? OnHighlight;

    /// <summary>
    ///     Fired when the user clicks the "Load More" row at the bottom of a full page. The short is the last visible PostId
    ///     to use as the startPostId for the next page request.
    /// </summary>
    public event Action<short>? OnLoadMorePosts;

    public event Action? OnNewMail;
    public event Action<short>? OnReplyPost;
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
                RowLabels[i]
                    .SetText("-- Load More --", Color.LightGray);
            } else if (entryIndex < Entries.Count)
            {
                var entry = Entries[entryIndex];
                var isSelected = entryIndex == SelectedIndex;

                var textColor = isSelected
                    ? new Color(100, 149, 237)
                    : entry.IsHighlighted
                        ? Color.Yellow
                        : Color.White;

                RowLabels[i]
                    .SetText(FormatRow(entry), textColor);
            } else
                RowLabels[i]
                    .SetText(string.Empty);
        }
    }

    /// <summary>
    ///     Shows or hides the Highlight button based on GM status and board type.
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
    ///     Populates the mail list from server data (first page).
    /// </summary>
    public void ShowMailList(ushort boardId, List<MailEntry> entries, bool isPublicBoard)
    {
        BoardId = boardId;
        IsPublicBoard = isPublicBoard;
        Entries = entries;
        HasMorePosts = entries.Count >= MAX_POSTS_PER_PAGE;
        SelectedIndex = -1;
        ScrollOffset = 0;
        DataVersion++;

        UpdateScrollBar();

        // Public boards don't support reply (no recipient)
        if (ReplyButton is not null)
            ReplyButton.Visible = !isPublicBoard;

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

        LastClickTime += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        // Click to select row or trigger "Load More"
        if (input.WasLeftButtonPressed)
        {
            var mx = input.MouseX - ScreenX - MailListRect.X;
            var my = input.MouseY - ScreenY - MailListRect.Y;

            if ((mx >= 0) && (mx < (MailListRect.Width - 16)) && (my >= 0) && (my < MailListRect.Height))
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
                    if ((entryIndex == SelectedIndex) && (LastClickTime < DOUBLE_CLICK_MS))
                    {
                        OnViewPost?.Invoke(Entries[entryIndex].PostId);
                        LastClickTime = float.MaxValue;
                    } else
                    {
                        SelectedIndex = entryIndex;
                        DataVersion++;
                        UpdateButtonStates();
                        LastClickTime = 0;
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

        if (ReplyButton is not null)
            ReplyButton.Enabled = hasSelection;

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