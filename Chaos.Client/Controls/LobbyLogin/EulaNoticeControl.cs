#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public class EulaNoticeControl : UIPanel
{
    private const float TEXT_FONT_SIZE = 0f;
    private UIImage AgreementTextImage { get; }
    public UIButton CancelButton { get; }

    private GraphicsDevice Device { get; }

    public UIButton OkButton { get; }
    private int TextAreaHeight { get; }
    private int TextAreaWidth { get; }
    private int TextAreaX { get; }
    private int TextAreaY { get; }

    public EulaNoticeControl(GraphicsDevice device)
    {
        Device = device;
        Name = "AgreePanel";
        Visible = false;

        var prefabSet = DataContext.UserControls.Get("_nagree");

        if (prefabSet is null)
            throw new InvalidOperationException("Failed to load _nagree control prefab set");

        // Anchor control provides panel dimensions and background
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;

        // Center on screen
        X = (640 - Width) / 2;
        Y = (480 - Height) / 2;

        if (anchor.Images.Count > 0)
            Background = TextureConverter.ToTexture2D(device, anchor.Images[0]);

        // Agreement text display region
        var textPrefab = prefabSet["AGREEMENTTEXT"];
        var textRect = textPrefab.Control.Rect!.Value;

        TextAreaX = (int)textRect.Left;
        TextAreaY = (int)textRect.Top;
        TextAreaWidth = (int)textRect.Width - 25;
        TextAreaHeight = (int)textRect.Height;

        AgreementTextImage = new UIImage
        {
            Name = "AgreementText",
            X = TextAreaX,
            Y = TextAreaY,
            Width = TextAreaWidth,
            Height = TextAreaHeight,
            Visible = false
        };
        AddChild(AgreementTextImage);

        // OK button
        var okPrefab = prefabSet["OK"];
        var okRect = okPrefab.Control.Rect!.Value;

        OkButton = new UIButton
        {
            Name = "OK",
            X = (int)okRect.Left,
            Y = (int)okRect.Top,
            Width = (int)okRect.Width,
            Height = (int)okRect.Height,
            NormalTexture = okPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, okPrefab.Images[0]) : null,
            PressedTexture = okPrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, okPrefab.Images[1]) : null
        };
        OkButton.OnClick += () => OnOk?.Invoke();
        AddChild(OkButton);

        // Cancel button
        var cancelPrefab = prefabSet["CANCEL"];
        var cancelRect = cancelPrefab.Control.Rect!.Value;

        CancelButton = new UIButton
        {
            Name = "Cancel",
            X = (int)cancelRect.Left,
            Y = (int)cancelRect.Top,
            Width = (int)cancelRect.Width,
            Height = (int)cancelRect.Height,
            NormalTexture = cancelPrefab.Images.Count > 0 ? TextureConverter.ToTexture2D(device, cancelPrefab.Images[0]) : null,
            PressedTexture = cancelPrefab.Images.Count > 1 ? TextureConverter.ToTexture2D(device, cancelPrefab.Images[1]) : null
        };
        CancelButton.OnClick += () => OnCancel?.Invoke();
        AddChild(CancelButton);
    }

    public void Hide()
    {
        Visible = false;
        AgreementTextImage.Visible = false;
    }

    public event Action? OnCancel;
    public event Action? OnOk;

    public void Show(string agreementText)
    {
        // Render the agreement text into the text area
        AgreementTextImage.Texture?.Dispose();

        AgreementTextImage.Texture = TextRenderer.RenderWrappedText(
            Device,
            agreementText,
            TextAreaWidth,
            TextAreaHeight,
            TEXT_FONT_SIZE,
            Color.White);

        AgreementTextImage.Visible = true;
        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Enter))
            OkButton.PerformClick();

        if (input.WasKeyPressed(Keys.Escape))
            CancelButton.PerformClick();

        base.Update(gameTime, input);
    }
}