#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Controls.Scrolling;
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
    private readonly ScrollViewerControl Viewer;
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
        UsesControlStack = true;

        SendButton = CreateButton("Send");
        CancelButton = CreateButton("Cancel");

        if (SendButton is not null)
            SendButton.Clicked += HandleSend;

        if (CancelButton is not null)
            CancelButton.Clicked += () =>
            {
                Hide();
                OnCancel?.Invoke();
            };

        AuthorLabel = CreateLabel("Author");
        TitleBox = CreateTextBox("Title", 60);
        TitleBox?.ForegroundColor = LegendColors.White;
        TitleBox?.IsTabStop = true;

        //content rect for multi-line body text entry. the compose prefabs (_nmails/_nartin) inset the editable Content
        //2px further right than the read prefabs (_nmailr/_narti), so the scrollbar gutter would otherwise land 2px past
        //the read panels' bar column. trimming the viewer width by that overshoot keeps the body width and the bar
        //column identical to the pre-binding layout (and aligned with the read views).
        var contentRect = GetRect("Content");
        const int COMPOSE_CONTENT_RIGHT_OVERSHOOT = 2;

        BodyBox = new UITextBox
        {
            Width = contentRect.Width - ScrollBarControl.DEFAULT_WIDTH - COMPOSE_CONTENT_RIGHT_OVERSHOOT,
            Height = contentRect.Height,
            IsMultiLine = true,
            IsSelectable = true,
            MaxLength = 10000,
            PaddingLeft = 0,
            PaddingRight = 0,
            PaddingTop = 0,
            PaddingBottom = 0,
            ForegroundColor = TextColors.Default,
            IsTabStop = true
        };

        //the viewer owns the bar + wheel routing and sizes the body each frame (UITextBox is IVerticalScrollable in
        //line units). width is trimmed by the overshoot so the gutter lands exactly on the read panels' bar column.
        Viewer = new ScrollViewerControl(BodyBox)
        {
            X = contentRect.X,
            Y = contentRect.Y,
            Width = contentRect.Width - COMPOSE_CONTENT_RIGHT_OVERSHOOT,
            Height = contentRect.Height
        };

        AddChild(Viewer);
    }

    private void HandleSend()
    {
        var subject = TitleBox?.Text ?? string.Empty;
        OnSend?.Invoke(subject, BodyBox.Text);
    }

    //a wheel anywhere over the compose panel scrolls the body, even when focus is on a header field — restoring the
    //pre-migration panel-wide wheel. Wheel directly over the body/bar is handled deeper and never bubbles here.
    public override void OnMouseScroll(MouseScrollEvent e) => Viewer.OnMouseScroll(e);

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CancelHandler? OnCancel;
    public event ArticleSendHandler? OnSend; //subject, body

    public void SetViewportBounds(Rectangle viewport)
    {
        TargetX = viewport.X + viewport.Width - Width;
        Y = viewport.Y;
    }

    public override void Show()
    {
        X = TargetX;
        InputDispatcher.Instance?.PushControl(this);
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

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();
                OnCancel?.Invoke();
                e.Handled = true;

                break;

            case Keys.Tab when TitleBox?.IsFocused == true:
                TitleBox.IsFocused = false;
                BodyBox.IsFocused = true;
                e.Handled = true;

                break;
        }
    }
}