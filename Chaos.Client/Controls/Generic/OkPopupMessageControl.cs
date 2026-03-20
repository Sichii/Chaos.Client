#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     A popup message dialog with DlgBack2.spf tiled background (clipped to interior height), dlgframe.epf 16×16 border,
///     and butt001.epf OK (+ optional Cancel) button.
/// </summary>
public sealed class OkPopupMessageControl : UIPanel
{
    private const int TILES_WIDE = 4;
    private const int INTERIOR_HEIGHT = 54;
    private const int CONTENT_PADDING = 6;
    private const int BUTTON_MARGIN = 1;

    // butt001.epf frame indices — 2 frames per button (normal/pressed)
    private const int OK_NORMAL = 15;
    private const int OK_PRESSED = 16;
    private const int CANCEL_NORMAL = 21;
    private const int CANCEL_PRESSED = 22;

    private readonly int ContentHeight;
    private readonly int ContentWidth;
    private readonly int ContentX;
    private readonly int ContentY;

    private readonly GraphicsDevice Device;
    public UIButton? CancelButton { get; }

    private UIImage MessageImage { get; }
    public UIButton OkButton { get; }

    public OkPopupMessageControl(GraphicsDevice device, bool showCancel = false)
    {
        Device = device;
        Name = "PopupMessage";
        Visible = false;

        // Load background tile
        using var bgTile = DataContext.UserControls.GetSpfImage("DlgBack2.spf");

        if (bgTile is null)
            throw new InvalidOperationException("Failed to load DlgBack2.spf");

        var borderSize = DialogFrame.BORDER_SIZE;
        var interiorWidth = bgTile.Width * TILES_WIDE - 23;
        var totalWidth = borderSize + interiorWidth + borderSize;
        var totalHeight = borderSize + INTERIOR_HEIGHT + borderSize;

        // Composite tiled background with border
        using var composite = DialogFrame.Composite(bgTile, totalWidth, totalHeight);

        if (composite is null)
            throw new InvalidOperationException("Failed to composite dialog background");

        Width = totalWidth;
        Height = totalHeight;
        X = (ChaosGame.VIRTUAL_WIDTH - Width) / 2;
        Y = (ChaosGame.VIRTUAL_HEIGHT - Height) / 2;
        Background = TextureConverter.ToTexture2D(device, composite);

        // OK button (butt001.epf — frame indices for OK)
        var cache = UiRenderer.Instance!;
        var okNormalTex = cache.GetEpfTexture("butt001.epf", OK_NORMAL);
        var okPressedTex = cache.GetEpfTexture("butt001.epf", OK_PRESSED);

        var btnWidth = okNormalTex?.Width ?? 0;
        var btnHeight = okNormalTex?.Height ?? 0;

        OkButton = new UIButton
        {
            Name = "OK",
            X = totalWidth - borderSize - btnWidth - BUTTON_MARGIN + 4,
            Y = totalHeight - borderSize - btnHeight - BUTTON_MARGIN + 5,
            Width = btnWidth,
            Height = btnHeight,
            NormalTexture = okNormalTex,
            PressedTexture = okPressedTex
        };
        OkButton.OnClick += () => OnOk?.Invoke();
        AddChild(OkButton);

        // Content area — text fills above the button row
        ContentX = borderSize + CONTENT_PADDING;
        ContentY = borderSize + CONTENT_PADDING;
        ContentWidth = interiorWidth - CONTENT_PADDING * 2;
        ContentHeight = INTERIOR_HEIGHT - CONTENT_PADDING * 2;

        // Text image placeholder
        MessageImage = new UIImage
        {
            Name = "MessageText",
            Visible = false
        };
        AddChild(MessageImage);

        // Cancel button (optional)
        if (showCancel)
        {
            var cancelNormalTex = cache.GetEpfTexture("butt001.epf", CANCEL_NORMAL);
            var cancelPressedTex = cache.GetEpfTexture("butt001.epf", CANCEL_PRESSED);

            var cancelWidth = cancelNormalTex?.Width ?? 0;

            CancelButton = new UIButton
            {
                Name = "Cancel",
                X = OkButton.X - cancelWidth - BUTTON_MARGIN,
                Y = OkButton.Y,
                Width = cancelWidth,
                Height = cancelNormalTex?.Height ?? 0,
                NormalTexture = cancelNormalTex,
                PressedTexture = cancelPressedTex
            };
            CancelButton.OnClick += () => OnCancel?.Invoke();
            AddChild(CancelButton);
        }
    }

    public void Hide()
    {
        Visible = false;
        MessageImage.Visible = false;
    }

    public event Action? OnCancel;

    public event Action? OnOk;

    public void Show(string message)
    {
        MessageImage.Texture?.Dispose();

        var textTexture = TextRenderer.RenderWrappedText(
            Device,
            message,
            ContentWidth,
            ContentHeight,
            Color.White);

        MessageImage.Texture = textTexture;
        MessageImage.X = ContentX + (ContentWidth - textTexture.Width) / 2 - 9;
        MessageImage.Y = ContentY + (ContentHeight - textTexture.Height) / 2 - 10;
        MessageImage.Width = textTexture.Width;
        MessageImage.Height = textTexture.Height;
        MessageImage.Visible = true;
        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Enter))
            OkButton.PerformClick();

        if (input.WasKeyPressed(Keys.Escape))
        {
            if (CancelButton is not null)
                CancelButton.PerformClick();
            else
                OkButton.PerformClick();
        }

        base.Update(gameTime, input);
    }
}