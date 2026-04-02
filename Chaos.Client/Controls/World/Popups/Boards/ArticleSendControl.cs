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
    private readonly UITextBox BodyBox;
    private readonly UITextBox? TitleBox;
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
            PaddingX = 0,
            PaddingY = 0,
            ForegroundColor = Color.White
        };

        AddChild(BodyBox);
    }

    private void HandleSend()
    {
        var subject = TitleBox?.Text ?? string.Empty;
        OnSend?.Invoke(subject, BodyBox.Text);
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

        BodyBox.Text = string.Empty;
        BodyBox.ScrollOffset = 0;
        BodyBox.CursorPosition = 0;

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
        {
            TitleBox.IsFocused = false;
            BodyBox.IsFocused = true;
        }

        base.Update(gameTime, input);
    }
}