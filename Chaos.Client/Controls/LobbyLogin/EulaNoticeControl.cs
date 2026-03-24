#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class EulaNoticeControl : PrefabPanel
{
    private readonly UIImage? AgreementTextImage;
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

            AgreementTextImage = new UIImage
            {
                Name = "AgreementText",
                X = textRect.X,
                Y = textRect.Y,
                Width = TextAreaWidth,
                Height = TextAreaHeight,
                Visible = false
            };

            AddChild(AgreementTextImage);
        }
    }

    public override void Hide()
    {
        Visible = false;

        AgreementTextImage?.Visible = false;
    }

    public event Action? OnCancel;

    public event Action? OnOk;

    public void Show(string agreementText)
    {
        if (AgreementTextImage is not null)
        {
            AgreementTextImage.Texture?.Dispose();

            AgreementTextImage.Texture = TextRenderer.RenderWrappedText(
                agreementText,
                TextAreaWidth,
                TextAreaHeight,
                Color.White);

            AgreementTextImage.Visible = true;
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