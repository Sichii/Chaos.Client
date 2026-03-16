#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World;

/// <summary>
///     Mail compose/send panel using _nmails prefab. Provides recipient, subject, and body text entry fields. Receiver has
///     a display label (read-only) and an editable overlay. Content is a multi-line text area (480x204).
/// </summary>
public class MailSendControl : PrefabPanel
{
    private const int LINE_HEIGHT = 12;

    // Content area — multi-line body
    private readonly Rectangle ContentRect;
    private readonly GraphicsDevice DeviceRef;
    private readonly int MaxVisibleLines;

    // Receiver — display label + editable overlay
    private readonly UILabel? ReceiverDisplayLabel;
    private readonly UITextBox? ReceiverEditBox;

    // Subject
    private readonly UITextBox? TitleBox;
    private List<string> BodyLines = [];
    private string BodyText = string.Empty;
    private int DataVersion;
    private readonly CachedText[] LineCaches = [];
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public ushort BoardId { get; set; }
    public UIButton? CancelButton { get; }

    public UIButton? SendButton { get; }

    public MailSendControl(GraphicsDevice device)
        : base(device, "_nmails")
    {
        DeviceRef = device;
        Name = "MailSend";
        Visible = false;

        var elements = AutoPopulate();

        SendButton = elements.GetValueOrDefault("Send") as UIButton;
        CancelButton = elements.GetValueOrDefault("Cancel") as UIButton;

        if (SendButton is not null)
            SendButton.OnClick += HandleSend;

        if (CancelButton is not null)
            CancelButton.OnClick += () =>
            {
                Hide();
                OnCancel?.Invoke();
            };

        // Receiver display (read-only label from "Receiver" control)
        ReceiverDisplayLabel = CreateLabel("Receiver");

        // Receiver editable overlay (from "ReceiverEdit" control)
        ReceiverEditBox = elements.GetValueOrDefault("ReceiverEdit") as UITextBox;

        if (ReceiverEditBox is not null)
            ReceiverEditBox.MaxLength = 24;

        // Subject/title textbox
        TitleBox = elements.GetValueOrDefault("Title") as UITextBox;

        if (TitleBox is not null)
            TitleBox.MaxLength = 60;

        // Content rect for body text display
        ContentRect = GetRect("Content");
        MaxVisibleLines = ContentRect.Height > 0 ? ContentRect.Height / LINE_HEIGHT : 0;

        LineCaches = new CachedText[MaxVisibleLines];

        for (var i = 0; i < MaxVisibleLines; i++)
            LineCaches[i] = new CachedText(device);
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

    private static int FindLineBreak(string text, int maxWidth)
    {
        var width = 0;
        var lastSpace = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
                lastSpace = i;

            width += TextRenderer.MeasureCharWidth(text[i]);

            if (width > maxWidth)
                return lastSpace > 0 ? lastSpace + 1 : Math.Max(1, i);
        }

        return text.Length;
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
        BodyLines = WrapText(BodyText, ContentRect.Width);

        // Auto-scroll to bottom
        if (BodyLines.Count > MaxVisibleLines)
            ScrollOffset = BodyLines.Count - MaxVisibleLines;

        DataVersion++;
    }

    private void HandleSend()
    {
        var recipient = ReceiverEditBox?.Text ?? string.Empty;
        var subject = TitleBox?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(recipient))
            return;

        OnSend?.Invoke(recipient, subject, BodyText);
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

            if (lineIndex < BodyLines.Count)
                LineCaches[i]
                    .Update(BodyLines[lineIndex], 0, Color.White);
            else
                LineCaches[i]
                    .Update(string.Empty, 0, Color.White);
        }
    }

    /// <summary>
    ///     Shows the compose dialog, optionally pre-filling the recipient.
    /// </summary>
    public void ShowCompose(string? recipient = null)
    {
        if (ReceiverEditBox is not null)
        {
            ReceiverEditBox.Text = recipient ?? string.Empty;
            ReceiverEditBox.IsFocused = recipient is null;
        }

        if (ReceiverDisplayLabel is not null)
            ReceiverDisplayLabel.SetText(recipient ?? string.Empty);

        if (TitleBox is not null)
            TitleBox.Text = string.Empty;

        BodyText = string.Empty;
        BodyLines.Clear();
        ScrollOffset = 0;
        DataVersion++;

        Show();

        // Focus recipient if empty, otherwise focus title
        if (ReceiverEditBox is not null && string.IsNullOrEmpty(recipient))
            ReceiverEditBox.IsFocused = true;
        else if (TitleBox is not null)
            TitleBox.IsFocused = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();
            OnCancel?.Invoke();

            return;
        }

        // Tab navigation between fields
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (ReceiverEditBox?.IsFocused == true)
            {
                ReceiverEditBox.IsFocused = false;

                if (TitleBox is not null)
                    TitleBox.IsFocused = true;
            } else if (TitleBox?.IsFocused == true)
                TitleBox.IsFocused = false;

            // Body would get focus here — for now body is simplified
        }

        // Enter in receiver → focus title
        if ((ReceiverEditBox?.IsFocused == true) && input.WasKeyPressed(Keys.Enter))
        {
            ReceiverEditBox.IsFocused = false;

            if (TitleBox is not null)
                TitleBox.IsFocused = true;
        }

        base.Update(gameTime, input);

        // Handle body text input when no textbox is focused
        if ((ReceiverEditBox?.IsFocused != true) && (TitleBox?.IsFocused != true))
            HandleBodyInput(input);
    }

    private static List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();

        if ((maxWidth <= 0) || string.IsNullOrEmpty(text))
            return lines;

        foreach (var paragraph in text.Split('\n'))
        {
            var remaining = paragraph;

            while (remaining.Length > 0)
            {
                var lineEnd = FindLineBreak(remaining, maxWidth);

                lines.Add(
                    remaining[..lineEnd]
                        .TrimEnd());

                remaining = remaining[lineEnd..]
                    .TrimStart();
            }

            if (paragraph.Length == 0)
                lines.Add(string.Empty);
        }

        return lines;
    }
}