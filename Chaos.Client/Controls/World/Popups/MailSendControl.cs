#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Mail compose/send panel using _nmails prefab. Provides recipient, subject, and body text entry fields. Receiver has
///     a display label (read-only) and an editable overlay. Content is a multi-line text area (480x204).
/// </summary>
public sealed class MailSendControl : PrefabPanel
{
    private const int LINE_HEIGHT = 12;
    private const float SLIDE_DURATION_MS = 250f;

    // Content area — multi-line body
    private readonly Rectangle ContentRect;
    private readonly CachedText[] LineCaches;
    private readonly int MaxVisibleLines;

    // Receiver — display label + editable overlay
    private readonly UILabel? ReceiverDisplayLabel;
    private readonly UITextBox? ReceiverEditBox;

    // Subject
    private readonly UITextBox? TitleBox;
    private List<string> BodyLines = [];
    private string BodyText = string.Empty;
    private int DataVersion;
    private int OffScreenX;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private float SlideTimer;
    private bool Sliding;
    private int TargetX;

    public ushort BoardId { get; set; }
    public bool IsPublicBoard { get; set; }
    public UIButton? CancelButton { get; }

    public UIButton? SendButton { get; }

    public MailSendControl(string prefabName = "_nmails")
        : base(prefabName, false)
    {
        Name = "MailSend";
        Visible = false;

        SendButton = CreateButton("Send");
        CancelButton = CreateButton("Cancel");

        if (SendButton is not null)
            SendButton.OnClick += HandleSend;

        if (CancelButton is not null)
            CancelButton.OnClick += () =>
            {
                Hide();
                OnCancel?.Invoke();
            };

        // Receiver display (read-only label from "Receiver" control, or "Author" in _nartin)
        ReceiverDisplayLabel = CreateLabel("Receiver") ?? CreateLabel("Author");

        // Receiver editable overlay (from "ReceiverEdit" control — null for _nartin)
        ReceiverEditBox = CreateTextBox("ReceiverEdit", 24);

        // Subject/title textbox
        TitleBox = CreateTextBox("Title", 60);

        // Content rect for body text display
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

        if (MaxVisibleLines == 0)
            return;

        RefreshLineCaches();

        var contentX = ScreenX + ContentRect.X;
        var contentY = ScreenY + ContentRect.Y;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            var lineIndex = ScrollOffset + i;

            if (lineIndex >= BodyLines.Count)
                break;

            LineCaches[i]
                .Draw(spriteBatch, new Vector2(contentX, contentY + i * LINE_HEIGHT));
        }
    }

    private void HandleBodyInput(InputBuffer input)
    {
        var changed = false;

        foreach (var c in input.TextInput)
        {
            if (c == '\b')
            {
                if (BodyText.Length > 0)
                {
                    BodyText = BodyText[..^1];
                    changed = true;
                }

                continue;
            }

            if ((c == '\r') || (c == '\n'))
            {
                BodyText += '\n';
                changed = true;

                continue;
            }

            if (char.IsControl(c))
                continue;

            BodyText += c;
            changed = true;
        }

        if (!changed)
            return;

        // Re-wrap body text
        BodyLines = TextRenderer.WrapLines(BodyText, ContentRect.Width);

        // Auto-scroll to bottom
        if (BodyLines.Count > MaxVisibleLines)
            ScrollOffset = BodyLines.Count - MaxVisibleLines;

        DataVersion++;
    }

    private void HandleSend()
    {
        var recipient = ReceiverEditBox?.Text ?? string.Empty;
        var subject = TitleBox?.Text ?? string.Empty;

        // Public boards don't require a recipient
        if (!IsPublicBoard && string.IsNullOrWhiteSpace(recipient))
            return;

        OnSend?.Invoke(recipient, subject, BodyText);
    }

    public override void Hide()
    {
        Visible = false;
        Sliding = false;
        X = OffScreenX;
    }

    public event Action? OnCancel;

    public event Action<string, string, string>? OnSend; // recipient, subject, body

    private void RefreshLineCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            var lineIndex = ScrollOffset + i;

            LineCaches[i]
                .Update(lineIndex < BodyLines.Count ? BodyLines[lineIndex] : string.Empty, Color.White);
        }
    }

    public void SetViewportBounds(Rectangle viewport)
    {
        TargetX = viewport.X + viewport.Width - Width;
        OffScreenX = viewport.X + viewport.Width;
        Y = viewport.Y;
    }

    public override void Show()
    {
        if (!Visible)
            SlideIn();
    }

    /// <summary>
    ///     Shows the compose dialog, optionally pre-filling the recipient.
    /// </summary>
    public void ShowCompose(string? recipient = null)
    {
        // Public boards hide the receiver field entirely
        if (ReceiverEditBox is not null)
        {
            ReceiverEditBox.Visible = !IsPublicBoard;
            ReceiverEditBox.Text = recipient ?? string.Empty;
            ReceiverEditBox.IsFocused = !IsPublicBoard && recipient is null;
        }

        if (ReceiverDisplayLabel is not null)
            ReceiverDisplayLabel.Visible = !IsPublicBoard;

        TitleBox?.Text = string.Empty;

        BodyText = string.Empty;
        BodyLines.Clear();
        ScrollOffset = 0;
        DataVersion++;

        Show();

        // Public boards skip receiver, focus title directly
        if (IsPublicBoard)
        {
            TitleBox?.IsFocused = true;
        } else if (ReceiverEditBox is not null && string.IsNullOrEmpty(recipient))
            ReceiverEditBox.IsFocused = true;
        else
            TitleBox?.IsFocused = true;
    }

    private void SlideIn()
    {
        X = OffScreenX;
        Visible = true;
        Sliding = true;
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
            X = (int)MathHelper.Lerp(OffScreenX, TargetX, eased);

            if (t >= 1f)
            {
                X = TargetX;
                Sliding = false;
            }
        }

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnCancel?.Invoke();

            return;
        }

        // Tab navigation between fields
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (!IsPublicBoard && (ReceiverEditBox?.IsFocused == true))
            {
                ReceiverEditBox.IsFocused = false;

                TitleBox?.IsFocused = true;
            } else if (TitleBox?.IsFocused == true)
                TitleBox.IsFocused = false;

            // Body would get focus here — for now body is simplified
        }

        // Enter in receiver → focus title (mail only)
        if (!IsPublicBoard && (ReceiverEditBox?.IsFocused == true) && input.WasKeyPressed(Keys.Enter))
        {
            ReceiverEditBox.IsFocused = false;

            TitleBox?.IsFocused = true;
        }

        base.Update(gameTime, input);

        // Handle body text input when no textbox is focused
        if ((ReceiverEditBox?.IsFocused != true) && (TitleBox?.IsFocused != true))
            HandleBodyInput(input);
    }
}