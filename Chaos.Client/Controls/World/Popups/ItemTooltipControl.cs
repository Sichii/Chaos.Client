#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Inventory item tooltip with black background, dlgframe.epf border drawn on the edges, white item name
///     (character-wrapped at 25 chars), and blue durability text. Follows the cursor when visible.
/// </summary>
public sealed class ItemTooltipControl : UIPanel
{
    private const int MAX_CHARS_PER_LINE = 25;
    private const int CHAR_CELL_WIDTH = 6;
    private const int LINE_HEIGHT = 12;
    private const int BORDER_SIZE = 16;
    private const int PADDING = 6;

    // dlgframe.epf frame indices
    private const int FRAME_TL = 0;
    private const int FRAME_TOP = 1;
    private const int FRAME_TR = 2;
    private const int FRAME_LEFT = 3;
    private const int FRAME_RIGHT = 4;
    private const int FRAME_BL = 5;
    private const int FRAME_BOTTOM = 6;
    private const int FRAME_BR = 7;

    private static readonly Color DurabilityColor = new(100, 149, 237);
    private readonly SKImage[] BorderFrames;

    private readonly GraphicsDevice Device;

    private readonly UIImage? TooltipImage;

    public ItemTooltipControl(GraphicsDevice device)
    {
        Device = device;
        Name = "ItemTooltip";
        Visible = false;

        BorderFrames = LoadBorderFrames();

        TooltipImage = new UIImage
        {
            Name = "TooltipContent"
        };

        AddChild(TooltipImage);
    }

    private static List<string> CharacterWrap(string text)
    {
        var lines = new List<string>();

        if (string.IsNullOrEmpty(text))
        {
            lines.Add(" ");

            return lines;
        }

        var remaining = text;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= MAX_CHARS_PER_LINE)
            {
                lines.Add(remaining);

                break;
            }

            lines.Add(remaining[..MAX_CHARS_PER_LINE]);
            remaining = remaining[MAX_CHARS_PER_LINE..];
        }

        return lines;
    }

    private Texture2D CompositeTooltip(
        List<string> nameLines,
        string? durabilityText,
        int totalWidth,
        int totalHeight)
    {
        var info = new SKImageInfo(
            totalWidth,
            totalHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        // Black background
        canvas.Clear(
            new SKColor(
                0,
                0,
                0,
                255));

        // Draw border on the edges (overlapping, not adding to size)
        DrawBorder(canvas, totalWidth, totalHeight);

        // Render text
        var textY = PADDING;

        foreach (var line in nameLines)
        {
            using var lineTexture = TextRenderer.RenderText(Device, line, Color.White);

            OverlayTexture(
                canvas,
                lineTexture,
                PADDING,
                textY);
            textY += LINE_HEIGHT;
        }

        if (durabilityText is not null)
        {
            using var durTexture = TextRenderer.RenderText(Device, durabilityText, DurabilityColor);

            OverlayTexture(
                canvas,
                durTexture,
                PADDING,
                textY);
        }

        using var snapshot = surface.Snapshot();

        return TextureConverter.ToTexture2D(Device, snapshot);
    }

    public override void Dispose()
    {
        foreach (var frame in BorderFrames)
            frame?.Dispose();

        base.Dispose();
    }

    private void DrawBorder(SKCanvas canvas, int totalWidth, int totalHeight)
    {
        if (BorderFrames.Length < 8)
            return;

        // Top edge
        for (var x = BORDER_SIZE; x < (totalWidth - BORDER_SIZE); x += BorderFrames[FRAME_TOP].Width)
            canvas.DrawImage(BorderFrames[FRAME_TOP], x, 0);

        // Bottom edge
        for (var x = BORDER_SIZE; x < (totalWidth - BORDER_SIZE); x += BorderFrames[FRAME_BOTTOM].Width)
            canvas.DrawImage(BorderFrames[FRAME_BOTTOM], x, totalHeight - BORDER_SIZE);

        // Left edge
        for (var y = BORDER_SIZE; y < (totalHeight - BORDER_SIZE); y += BorderFrames[FRAME_LEFT].Height)
            canvas.DrawImage(BorderFrames[FRAME_LEFT], 0, y);

        // Right edge
        for (var y = BORDER_SIZE; y < (totalHeight - BORDER_SIZE); y += BorderFrames[FRAME_RIGHT].Height)
            canvas.DrawImage(BorderFrames[FRAME_RIGHT], totalWidth - BORDER_SIZE, y);

        // Corners
        canvas.DrawImage(BorderFrames[FRAME_TL], 0, 0);
        canvas.DrawImage(BorderFrames[FRAME_TR], totalWidth - BORDER_SIZE, 0);
        canvas.DrawImage(BorderFrames[FRAME_BL], 0, totalHeight - BORDER_SIZE);
        canvas.DrawImage(BorderFrames[FRAME_BR], totalWidth - BORDER_SIZE, totalHeight - BORDER_SIZE);
    }

    public void Hide() => Visible = false;

    private static SKImage[] LoadBorderFrames()
    {
        var allFrames = DataContext.UserControls.GetEpfImages("dlgframe.epf");

        if (allFrames.Length < 8)
        {
            foreach (var img in allFrames)
                img?.Dispose();

            return [];
        }

        var frames = new SKImage[8];
        Array.Copy(allFrames, frames, 8);

        for (var i = 8; i < allFrames.Length; i++)
            allFrames[i]
                ?.Dispose();

        return frames;
    }

    private static void OverlayTexture(
        SKCanvas canvas,
        Texture2D texture,
        int x,
        int y)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);

        var info = new SKImageInfo(
            texture.Width,
            texture.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);

        for (var row = 0; row < texture.Height; row++)
            for (var col = 0; col < texture.Width; col++)
            {
                var c = pixels[row * texture.Width + col];

                bitmap.SetPixel(
                    col,
                    row,
                    new SKColor(
                        c.R,
                        c.G,
                        c.B,
                        c.A));
            }

        canvas.DrawBitmap(bitmap, x, y);
    }

    public void Show(
        string itemName,
        int currentDurability,
        int maxDurability,
        int mouseX,
        int mouseY)
    {
        // Character-wrap the name
        var nameLines = CharacterWrap(itemName);
        var hasDurability = maxDurability > 0;
        var durabilityText = hasDurability ? $"{currentDurability}/{maxDurability}" : null;

        // Content dimensions
        var contentWidth = MAX_CHARS_PER_LINE * CHAR_CELL_WIDTH;
        var contentHeight = nameLines.Count * LINE_HEIGHT;

        if (hasDurability)
            contentHeight += LINE_HEIGHT;

        // Total size = content + padding, border overlaps the edges
        var totalWidth = PADDING + contentWidth + PADDING;
        var totalHeight = PADDING + contentHeight + PADDING;

        Width = totalWidth;
        Height = totalHeight;

        // Composite the tooltip texture
        TooltipImage?.Texture?.Dispose();

        var texture = CompositeTooltip(
            nameLines,
            durabilityText,
            totalWidth,
            totalHeight);
        TooltipImage!.Texture = texture;
        TooltipImage.X = 0;
        TooltipImage.Y = 0;
        TooltipImage.Width = totalWidth;
        TooltipImage.Height = totalHeight;

        // Position near cursor, clamped to screen
        X = Math.Clamp(mouseX + 15, 0, ChaosGame.VIRTUAL_WIDTH - totalWidth);
        Y = Math.Clamp(mouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - totalHeight);

        Visible = true;
    }
}