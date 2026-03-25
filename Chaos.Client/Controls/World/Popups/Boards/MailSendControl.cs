#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

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
    public UIButton? CancelButton { get; }
    public UIButton? SendButton { get; }

    public MailSendControl()
        : base("_nmails", false)
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

        ReceiverDisplayLabel = CreateLabel("Receiver");
        ReceiverEditBox = CreateTextBox("ReceiverEdit", 24);
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
            PaddingTop = 0,
            WordWrap = true,
            ForegroundColor = Color.White
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

        BodyLabel.Text = BodyText;

        // Auto-scroll to bottom
        var maxScroll = Math.Max(0, BodyLabel.ContentHeight - VisibleHeight);
        BodyLabel.ScrollOffset = maxScroll;
    }

    private void HandleSend()
    {
        var recipient = ReceiverEditBox?.Text ?? string.Empty;
        var subject = TitleBox?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(recipient))
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
        if (ReceiverEditBox is not null)
        {
            ReceiverEditBox.Text = recipient ?? string.Empty;
            ReceiverEditBox.IsFocused = recipient is null;
        }

        if (TitleBox is not null)
            TitleBox.Text = string.Empty;

        BodyText = string.Empty;
        BodyLabel.ScrollOffset = 0;
        BodyLabel.Text = string.Empty;

        Show();

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
}