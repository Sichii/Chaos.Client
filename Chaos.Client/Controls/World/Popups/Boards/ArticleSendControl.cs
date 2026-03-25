#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Article compose panel using _nartin prefab. Provides subject and body text entry fields for posting to a public
///     board. No recipient field — public board posts have no addressee.
/// </summary>
public sealed class ArticleSendControl : PrefabPanel
{
    private readonly UILabel? AuthorLabel;
    private readonly UILabel BodyLabel;
    private readonly UITextBox? TitleBox;
    private readonly int VisibleHeight;
    private string BodyText = string.Empty;
    private int TargetX;

    public ushort BoardId { get; set; }
    public UIButton? CancelButton { get; }
    public UIButton? SendButton { get; }

    public ArticleSendControl()
        : base("_nartin", false)
    {
        Name = "ArticleSend";
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

        AuthorLabel = CreateLabel("Author");
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
        var subject = TitleBox?.Text ?? string.Empty;
        OnSend?.Invoke(subject, BodyText);
    }

    public override void Hide() => Visible = false;

    public event Action? OnCancel;
    public event Action<string, string>? OnSend; // subject, body

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
    ///     Shows the compose dialog for a new public board post.
    /// </summary>
    public void ShowCompose(string authorName)
    {
        AuthorLabel?.Text = authorName;

        if (TitleBox is not null)
        {
            TitleBox.Text = string.Empty;
            TitleBox.IsFocused = true;
        }

        BodyText = string.Empty;
        BodyLabel.ScrollOffset = 0;
        BodyLabel.Text = string.Empty;

        Show();
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

        // Tab out of title into body
        if (input.WasKeyPressed(Keys.Tab) && (TitleBox?.IsFocused == true))
            TitleBox.IsFocused = false;

        base.Update(gameTime, input);

        // Handle body text input when title is not focused
        if (TitleBox?.IsFocused != true)
            HandleBodyInput(input);
    }
}