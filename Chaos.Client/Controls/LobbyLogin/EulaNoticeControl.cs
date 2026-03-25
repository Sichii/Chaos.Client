#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class EulaNoticeControl : PrefabPanel
{
    private readonly UILabel? AgreementTextLabel;
    private readonly int TextAreaHeight;
    private readonly int TextAreaWidth;
    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public EulaNoticeControl()
        : base("_nagree")
    {
        Name = "AgreePanel";
        Visible = false;

        // Buttons — _nagree is one of the few prefabs with actual type 3 controls
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("CANCEL");

        if (OkButton is not null)
            OkButton.OnClick += () => OnOk?.Invoke();

        if (CancelButton is not null)
            CancelButton.OnClick += () => OnCancel?.Invoke();

        // Agreement text display region — type 7, 0 images
        var textRect = GetRect("AGREEMENTTEXT");

        if (textRect != Rectangle.Empty)
        {
            TextAreaWidth = textRect.Width - 25;
            TextAreaHeight = textRect.Height;

            AgreementTextLabel = new UILabel
            {
                Name = "AgreementText",
                X = textRect.X,
                Y = textRect.Y,
                Width = TextAreaWidth,
                Height = TextAreaHeight,
                PaddingLeft = 0,
                PaddingTop = 0,
                WordWrap = true,
                Visible = false
            };

            AddChild(AgreementTextLabel);
        }
    }

    public override void Hide()
    {
        Visible = false;

        AgreementTextLabel?.Visible = false;
    }

    public event Action? OnCancel;

    public event Action? OnOk;

    public void Show(string agreementText)
    {
        if (AgreementTextLabel is not null)
        {
            AgreementTextLabel.ForegroundColor = Color.White;
            AgreementTextLabel.Text = agreementText;
            AgreementTextLabel.Visible = true;
        }

        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Enter))
            OkButton?.PerformClick();

        if (input.WasKeyPressed(Keys.Escape))
            CancelButton?.PerformClick();

        base.Update(gameTime, input);
    }
}