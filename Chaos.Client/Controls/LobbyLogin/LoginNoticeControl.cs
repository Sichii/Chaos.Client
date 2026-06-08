#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Scrolling;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class LoginNoticeControl : PrefabPanel
{
    private readonly UILabel? AgreementTextLabel;
    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public LoginNoticeControl()
        : base("_nagree")
    {
        Name = "AgreePanel";
        Visible = false;
        UsesControlStack = true;

        //buttons — _nagree is one of the few prefabs with actual type 3 controls
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("CANCEL");

        if (OkButton is not null)
            OkButton.Clicked += () => OnOk?.Invoke();

        if (CancelButton is not null)
            CancelButton.Clicked += () => OnCancel?.Invoke();

        //agreement text display region — type 7, 0 images
        var textRect = GetRect("AGREEMENTTEXT");

        if (textRect != Rectangle.Empty)
        {
            AgreementTextLabel = new UILabel
            {
                Name = "AgreementText",
                Width = textRect.Width,
                Height = textRect.Height,
                PaddingLeft = 0,
                PaddingTop = 0,
                WordWrap = true
            };

            //the prefab leaves a 9px gap between the text and the scrollbar column (the old label was inset
            //25px from the right while the viewer's bar gutter is 16px); ContentRightPadding reproduces it.
            var viewer = new ScrollViewerControl(AgreementTextLabel)
            {
                Name = "AgreementScroll",
                X = textRect.X,
                Y = textRect.Y,
                Width = textRect.Width,
                Height = textRect.Height,
                ContentRightPadding = 9
            };

            AddChild(viewer);
        }
    }

    public event CancelHandler? OnCancel;

    public event OkHandler? OnOk;

    public void Show(string agreementText)
    {
        if (AgreementTextLabel is not null)
        {
            AgreementTextLabel.ForegroundColor = TextColors.Default;
            AgreementTextLabel.ScrollOffset = 0;
            AgreementTextLabel.Text = agreementText;
        }

        base.Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Enter:
                OkButton?.PerformClick();
                e.Handled = true;

                break;

            case Keys.Escape:
                //intentionally blocked — escape is a no-op during eula display
                e.Handled = true;

                break;
        }
    }

}