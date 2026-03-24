#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Mail compose/send panel using _nmails prefab. Provides recipient, subject, and body text entry fields. Receiver has
///     a display label (read-only) and an editable overlay. Content is a multi-line text area (480x204).
/// </summary>
public sealed class MailSendControl : PrefabPanel
{
    // Content area — multi-line body
    private readonly UILabel BodyLabel;

    // Receiver — display label + editable overlay
    private readonly UILabel? ReceiverDisplayLabel;
    private readonly UITextBox? ReceiverEditBox;

    // Subject
    private readonly UITextBox? TitleBox;
    private readonly int VisibleHeight;
    private string BodyText = string.Empty;
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

        BodyLabel.SetWrappedText(BodyText, Color.White);

        // Auto-scroll to bottom
        var maxScroll = Math.Max(0, BodyLabel.ContentHeight - VisibleHeight);
        BodyLabel.ScrollOffset = maxScroll;
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

    public override void Hide() => Visible = false;

    public event Action? OnCancel;

    public event Action<string, string, string>? OnSend; // recipient, subject, body

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
        BodyLabel.ScrollOffset = 0;
        BodyLabel.SetWrappedText(string.Empty);

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