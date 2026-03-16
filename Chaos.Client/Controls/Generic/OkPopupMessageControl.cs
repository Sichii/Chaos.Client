#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaSharp;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     A popup message dialog with DlgBack2.spf tiled background (clipped to interior height), dlgframe.epf 16×16 border,
///     and butt001.epf OK (+ optional Cancel) button.
/// </summary>
public class OkPopupMessageControl : UIPanel
{
    private const int TILES_WIDE = 4;
    private const int INTERIOR_HEIGHT = 54;
    private const int CONTENT_PADDING = 6;
    private const int BUTTON_MARGIN = 1;
    private const int GUI_PALETTE = 0;

    // butt001.epf frame indices — 2 frames per button (normal/pressed)
    private const int OK_NORMAL = 15;
    private const int OK_PRESSED = 16;
    private const int CANCEL_NORMAL = 19;
    private const int CANCEL_PRESSED = 20;

    // dlgframe.epf frame indices (16×16 each)
    private const int FRAME_TL = 0;
    private const int FRAME_TOP = 1;
    private const int FRAME_TR = 2;
    private const int FRAME_LEFT = 3;
    private const int FRAME_RIGHT = 4;
    private const int FRAME_BL = 5;
    private const int FRAME_BOTTOM = 6;
    private const int FRAME_BR = 7;
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
        using var bgTile = LoadSpfFrame("DlgBack2.spf", 0);

        if (bgTile is null)
            throw new InvalidOperationException("Failed to load DlgBack2.spf");

        // Load border frames
        var borderFrames = LoadBorderFrames();

        if (borderFrames is null)
            throw new InvalidOperationException("Failed to load dlgframe.epf");

        var borderSize = borderFrames[0].Width; // 16
        var interiorWidth = bgTile.Width * TILES_WIDE - 23;
        var totalWidth = borderSize + interiorWidth + borderSize;
        var totalHeight = borderSize + INTERIOR_HEIGHT + borderSize;

        // Composite tiled background (tiles rendered full-size, clipped to interior bounds)
        using var composite = CompositeBackground(
            bgTile,
            borderFrames,
            totalWidth,
            totalHeight,
            borderSize,
            interiorWidth,
            INTERIOR_HEIGHT);

        DisposeBorderFrames(borderFrames);

        Width = totalWidth;
        Height = totalHeight;
        X = (ChaosGame.VIRTUAL_WIDTH - Width) / 2;
        Y = (ChaosGame.VIRTUAL_HEIGHT - Height) / 2;
        Background = TextureConverter.ToTexture2D(device, composite);

        // OK button (butt001.epf — 6th button, indices 10-11)
        using var btnNormal = LoadEpfFrame("butt001.epf", OK_NORMAL);
        using var btnPressed = LoadEpfFrame("butt001.epf", OK_PRESSED);

        var btnWidth = btnNormal?.Width ?? 0;
        var btnHeight = btnNormal?.Height ?? 0;

        OkButton = new UIButton
        {
            Name = "OK",
            X = totalWidth - borderSize - btnWidth - BUTTON_MARGIN + 4,
            Y = totalHeight - borderSize - btnHeight - BUTTON_MARGIN + 5,
            Width = btnWidth,
            Height = btnHeight,
            NormalTexture = btnNormal is not null ? TextureConverter.ToTexture2D(device, btnNormal) : null,
            PressedTexture = btnPressed is not null ? TextureConverter.ToTexture2D(device, btnPressed) : null
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
            using var cancelNormal = LoadEpfFrame("butt001.epf", CANCEL_NORMAL);
            using var cancelPressed = LoadEpfFrame("butt001.epf", CANCEL_PRESSED);

            var cancelWidth = cancelNormal?.Width ?? 0;

            CancelButton = new UIButton
            {
                Name = "Cancel",
                X = OkButton.X - cancelWidth - BUTTON_MARGIN,
                Y = OkButton.Y,
                Width = cancelWidth,
                Height = cancelNormal?.Height ?? 0,
                NormalTexture = cancelNormal is not null ? TextureConverter.ToTexture2D(device, cancelNormal) : null,
                PressedTexture = cancelPressed is not null ? TextureConverter.ToTexture2D(device, cancelPressed) : null
            };
            CancelButton.OnClick += () => OnCancel?.Invoke();
            AddChild(CancelButton);
        }
    }

    private static SKImage CompositeBackground(
        SKImage bgTile,
        SKImage[] border,
        int totalWidth,
        int totalHeight,
        int borderSize,
        int interiorWidth,
        int interiorHeight)
    {
        var info = new SKImageInfo(
            totalWidth,
            totalHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Tile background across the full surface — border draws on top,
        // its opaque edges overlay the excess while transparent interior-facing
        // pixels correctly reveal the background behind them
        for (var tx = 0; tx < totalWidth; tx += bgTile.Width)
            for (var ty = 0; ty < totalHeight; ty += bgTile.Height)
                canvas.DrawImage(bgTile, tx, ty);

        // Top edge
        for (var x = borderSize; x < (totalWidth - borderSize); x += border[FRAME_TOP].Width)
            canvas.DrawImage(border[FRAME_TOP], x, 0);

        // Bottom edge
        for (var x = borderSize; x < (totalWidth - borderSize); x += border[FRAME_BOTTOM].Width)
            canvas.DrawImage(border[FRAME_BOTTOM], x, totalHeight - borderSize);

        // Left edge
        for (var y = borderSize; y < (totalHeight - borderSize); y += border[FRAME_LEFT].Height)
            canvas.DrawImage(border[FRAME_LEFT], 0, y);

        // Right edge
        for (var y = borderSize; y < (totalHeight - borderSize); y += border[FRAME_RIGHT].Height)
            canvas.DrawImage(border[FRAME_RIGHT], totalWidth - borderSize, y);

        // Corners (drawn last to cover edge overlap)
        canvas.DrawImage(border[FRAME_TL], 0, 0);
        canvas.DrawImage(border[FRAME_TR], totalWidth - borderSize, 0);
        canvas.DrawImage(border[FRAME_BL], 0, totalHeight - borderSize);
        canvas.DrawImage(border[FRAME_BR], totalWidth - borderSize, totalHeight - borderSize);

        return surface.Snapshot();
    }

    private static void DisposeBorderFrames(SKImage[] frames)
    {
        foreach (var frame in frames)
            frame.Dispose();
    }

    public void Hide()
    {
        Visible = false;
        MessageImage.Visible = false;
    }

    private static SKImage[]? LoadBorderFrames()
    {
        if (!DatArchives.Setoa.TryGetValue("dlgframe.epf", out var entry))
            return null;

        var guiPalettes = Palette.FromArchive("gui", DatArchives.Setoa);

        if (!guiPalettes.TryGetValue(GUI_PALETTE, out var palette))
            return null;

        var epf = EpfFile.FromEntry(entry);

        if (epf.Count < 8)
            return null;

        var frames = new SKImage[8];

        for (var i = 0; i < 8; i++)
        {
            var rendered = Graphics.RenderImage(epf[i], palette);

            if (rendered is null)
            {
                for (var j = 0; j < i; j++)
                    frames[j]
                        .Dispose();

                return null;
            }

            frames[i] = rendered;
        }

        return frames;
    }

    private static SKImage? LoadEpfFrame(string epfName, int frameIndex)
    {
        if (!DatArchives.Setoa.TryGetValue(epfName, out var entry))
            return null;

        var guiPalettes = Palette.FromArchive("gui", DatArchives.Setoa);

        if (!guiPalettes.TryGetValue(GUI_PALETTE, out var palette))
            return null;

        var epf = EpfFile.FromEntry(entry);

        if ((frameIndex < 0) || (frameIndex >= epf.Count))
            return null;

        return Graphics.RenderImage(epf[frameIndex], palette);
    }

    private static SKImage? LoadSpfFrame(string spfName, int frameIndex) => DataContext.UserControls.GetSpfImage(spfName, frameIndex);

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
            color: Color.White);

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