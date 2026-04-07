#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Extensions;
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

    private List<string>? MessageLines;
    private int MessageTextX;
    private int MessageTextY;

    public UIButton? CancelButton { get; }
    public UIButton OkButton { get; }

    public OkPopupMessageControl(bool showCancel = false)
    {
        Name = "PopupMessage";
        Visible = false;
        UsesControlStack = true;

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
        this.CenterOnScreen();
        Background = TextureConverter.ToTexture2D(composite);

        // Content area — text fills above the button row
        ContentX = borderSize + CONTENT_PADDING;
        ContentY = borderSize + CONTENT_PADDING;
        ContentWidth = interiorWidth - CONTENT_PADDING * 2;
        ContentHeight = INTERIOR_HEIGHT - CONTENT_PADDING * 2;

        // Button textures
        var cache = UiRenderer.Instance!;
        var okNormalTex = cache.GetEpfTexture("butt001.epf", OK_NORMAL);
        var okPressedTex = cache.GetEpfTexture("butt001.epf", OK_PRESSED);

        var okWidth = okNormalTex?.Width ?? 0;
        var okHeight = okNormalTex?.Height ?? 0;
        var buttonY = totalHeight - borderSize - okHeight - BUTTON_MARGIN + 5;
        var rightButtonX = totalWidth - borderSize - BUTTON_MARGIN + 4;
        int okX;

        // Cancel button (optional) — positioned on the right
        if (showCancel)
        {
            var cancelNormalTex = cache.GetEpfTexture("butt001.epf", CANCEL_NORMAL);
            var cancelPressedTex = cache.GetEpfTexture("butt001.epf", CANCEL_PRESSED);

            var cancelWidth = cancelNormalTex?.Width ?? 0;

            CancelButton = new UIButton
            {
                Name = "Cancel",
                X = rightButtonX - cancelWidth,
                Y = buttonY,
                Width = cancelWidth,
                Height = cancelNormalTex?.Height ?? 0,
                NormalTexture = cancelNormalTex,
                PressedTexture = cancelPressedTex
            };
            CancelButton.Clicked += () => OnCancel?.Invoke();
            AddChild(CancelButton);

            okX = CancelButton.X - okWidth - BUTTON_MARGIN;
        } else
        {
            okX = rightButtonX - okWidth;
        }

        // OK button — left of Cancel, or slides right when Cancel absent
        OkButton = new UIButton
        {
            Name = "OK",
            X = okX,
            Y = buttonY,
            Width = okWidth,
            Height = okHeight,
            NormalTexture = okNormalTex,
            PressedTexture = okPressedTex
        };
        OkButton.Clicked += () => OnOk?.Invoke();
        AddChild(OkButton);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if (MessageLines is not null)
            TextRenderer.DrawLines(
                spriteBatch,
                new Vector2(ScreenX + MessageTextX, ScreenY + MessageTextY),
                MessageLines,
                Color.White);
    }

    public void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        MessageLines = null;
    }

    public event Action? OnCancel;

    public event Action? OnOk;

    public void Show(string message)
    {
        var lines = TextRenderer.WrapText(message, ContentWidth);
        var textHeight = Math.Max(TextRenderer.CHAR_HEIGHT, lines.Count * TextRenderer.CHAR_HEIGHT);

        MessageLines = lines;
        MessageTextX = ContentX - 9;
        MessageTextY = ContentY + (ContentHeight - textHeight) / 2 - 10;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Enter)
        {
            OkButton.PerformClick();
            e.Handled = true;
        } else if (e.Key == Keys.Escape)
        {
            if (CancelButton is not null)
                CancelButton.PerformClick();
            else
                OkButton.PerformClick();

            e.Handled = true;
        }
    }
}