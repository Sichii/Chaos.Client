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
    // lback.txt BulletinRect — mail/board panels are bottom-aligned to this region
    private const int BULLETIN_RECT_LEFT = 8;
    private const int BULLETIN_RECT_BOTTOM = 304;

    // Server caps board responses at sbyte.MaxValue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = 16;
    private const int DATE_WIDTH = 40;
    private const int AUTHOR_WIDTH = 100;
    private const int SUBJECT_OFFSET_X = 145;
    private readonly CachedText[] AuthorCaches;

    private readonly CachedText[] DateCaches;

    private readonly Rectangle MailListRect;
    private readonly int MaxVisibleRows;
    private readonly ScrollBarControl ScrollBar;
    private readonly CachedText[] SubjectCaches;
    private int DataVersion;

    private List<MailEntry> Entries = [];
    private bool HasMorePosts;

    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;

    public ushort BoardId { get; private set; }
    public bool IsPublicBoard { get; private set; }
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
        X = BULLETIN_RECT_LEFT;
        Y = BULLETIN_RECT_BOTTOM - Height;
        Visible = false;

        ViewButton = CreateButton("View");
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

        // MailList rect for scrollable area
        MailListRect = GetRect("MailList");
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

        // Row caches
        DateCaches = new CachedText[MaxVisibleRows];
        AuthorCaches = new CachedText[MaxVisibleRows];
        SubjectCaches = new CachedText[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            DateCaches[i] = new CachedText();
            AuthorCaches[i] = new CachedText();
            SubjectCaches[i] = new CachedText();
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

    public override void Dispose()
    {
        foreach (var c in DateCaches)
            c.Dispose();

        foreach (var c in AuthorCaches)
            c.Dispose();

        foreach (var c in SubjectCaches)
            c.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if ((MaxVisibleRows == 0) || (Entries.Count == 0))
            return;

        RefreshRowCaches();

        var listX = ScreenX + MailListRect.X;
        var listY = ScreenY + MailListRect.Y;

        var totalRows = Entries.Count + (HasMorePosts ? 1 : 0);

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (entryIndex >= totalRows)
                break;

            var rowY = listY + i * ROW_HEIGHT;

            // "Load More" virtual row at the end
            if (HasMorePosts && (entryIndex == Entries.Count))
            {
                SubjectCaches[i]
                    .Draw(spriteBatch, new Vector2(listX + 2, rowY + 2));

                continue;
            }

            // Selection highlight
            if (entryIndex == SelectedIndex)
                DrawRect(
                    spriteBatch,
                    new Rectangle(
                        listX,
                        rowY,
                        MailListRect.Width - 16,
                        ROW_HEIGHT),
                    new Color(
                        80,
                        120,
                        200,
                        100));

            // Date
            DateCaches[i]
                .Draw(spriteBatch, new Vector2(listX + 2, rowY + 2));

            // Author
            AuthorCaches[i]
                .Draw(spriteBatch, new Vector2(listX + DATE_WIDTH + 4, rowY + 2));

            // Subject
            SubjectCaches[i]
                .Draw(spriteBatch, new Vector2(listX + SUBJECT_OFFSET_X, rowY + 2));
        }
    }

    public event Action? OnClose;
    public event Action<short>? OnDeletePost;

    /// <summary>
    ///     Fired when the user clicks the "Load More" row at the bottom of a full page. The short is the last visible PostId
    ///     to use as the startPostId for the next page request.
    /// </summary>
    public event Action<short>? OnLoadMorePosts;

    public event Action? OnNewMail;
    public event Action<short>? OnReplyPost;
    public event Action<short>? OnViewPost;

    private void RefreshRowCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            // "Load More" virtual row
            if (HasMorePosts && (entryIndex == Entries.Count))
            {
                DateCaches[i]
                    .Update(string.Empty, Color.White);

                AuthorCaches[i]
                    .Update(string.Empty, Color.White);

                SubjectCaches[i]
                    .Update("-- Load More --", Color.LightGray);
            } else if (entryIndex < Entries.Count)
            {
                var entry = Entries[entryIndex];
                var textColor = entry.IsHighlighted ? Color.Yellow : Color.White;

                DateCaches[i]
                    .Update($"{entry.Month}/{entry.Day}", textColor);

                AuthorCaches[i]
                    .Update(entry.Author, textColor);

                SubjectCaches[i]
                    .Update(entry.Subject, textColor);
            } else
            {
                DateCaches[i]
                    .Update(string.Empty, Color.White);

                AuthorCaches[i]
                    .Update(string.Empty, Color.White);

                SubjectCaches[i]
                    .Update(string.Empty, Color.White);
            }
        }
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

        var totalRows = Entries.Count + (HasMorePosts ? 1 : 0);

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
                    SelectedIndex = entryIndex;
                    DataVersion++;
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

    private void UpdateScrollBar()
    {
        // Add 1 virtual row for the "Load More" indicator when more posts exist
        var totalRows = Entries.Count + (HasMorePosts ? 1 : 0);

        ScrollBar.TotalItems = totalRows;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, totalRows - MaxVisibleRows);
    }
}