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
    private readonly UITextBox BodyBox;

    // Receiver — display label + editable overlay
    private readonly UILabel? ReceiverDisplayLabel;
    private readonly UITextBox? ReceiverEditBox;

    // Subject
    private readonly UITextBox? TitleBox;
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

        ReceiverEditBox?.ForegroundColor = LegendColors.White;
        TitleBox?.ForegroundColor = LegendColors.White;

        // Content rect for multi-line body text entry
        var contentRect = GetRect("Content");

        BodyBox = new UITextBox
        {
            X = contentRect.X,
            Y = contentRect.Y,
            Width = contentRect.Width,
            Height = contentRect.Height,
            IsMultiLine = true,
            IsFocusable = true,
            IsSelectable = true,
            MaxLength = 10000,
            PaddingLeft = 0,
            PaddingRight = 0,
            PaddingTop = 0,
            PaddingBottom = 0,
            ForegroundColor = TextColors.Default
        };

        AddChild(BodyBox);
    }

    private void HandleSend()
    {
        var recipient = ReceiverEditBox?.Text ?? string.Empty;
        var subject = TitleBox?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(recipient))
            return;

        OnSend?.Invoke(recipient, subject, BodyBox.Text);
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

        BodyBox.Text = string.Empty;
        BodyBox.ScrollOffset = 0;
        BodyBox.CursorPosition = 0;

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

        // Tab navigation: Receiver → Title → BodyBox
        if (input.WasKeyPressed(Keys.Tab))
        {
            if (ReceiverEditBox?.IsFocused == true)
            {
                ReceiverEditBox.IsFocused = false;

                if (TitleBox is not null)
                    TitleBox.IsFocused = true;
                else
                    BodyBox.IsFocused = true;
            } else if (TitleBox?.IsFocused == true)
            {
                TitleBox.IsFocused = false;
                BodyBox.IsFocused = true;
            }
        }

        // Enter in receiver → focus title (or body if no title)
        if ((ReceiverEditBox?.IsFocused == true) && input.WasKeyPressed(Keys.Enter))
        {
            ReceiverEditBox.IsFocused = false;

            if (TitleBox is not null)
                TitleBox.IsFocused = true;
            else
                BodyBox.IsFocused = true;
        }

        base.Update(gameTime, input);
    }
}