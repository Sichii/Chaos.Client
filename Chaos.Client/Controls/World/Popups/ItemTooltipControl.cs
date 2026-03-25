#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Chaos.Client.Utilities;
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
    private const int CHAR_CELL_WIDTH = TextRenderer.CHAR_WIDTH;
    private const int LINE_HEIGHT = TextRenderer.CHAR_HEIGHT;
    private const int PADDING = 6;

    private static readonly Color DurabilityColor = new(100, 149, 237);

    private readonly UIImage? TooltipImage;
    private string? DurabilityText;
    private List<string>? NameLines;

    public ItemTooltipControl()
    {
        Name = "ItemTooltip";
        Visible = false;

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

    private static Texture2D CompositeBackground(int totalWidth, int totalHeight)
    {
        using var background = DialogFrame.Composite(
            new SKColor(
                0,
                0,
                0,
                255),
            totalWidth,
            totalHeight);

        var info = new SKImageInfo(
            totalWidth,
            totalHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        if (background is not null)
            canvas.DrawImage(background, 0, 0);
        else
            canvas.Clear(
                new SKColor(
                    0,
                    0,
                    0,
                    255));

        using var snapshot = surface.Snapshot();

        return TextureConverter.ToTexture2D(snapshot);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        // Draw text lines on top of the background
        if (NameLines is null)
            return;

        var textY = (float)(ScreenY + PADDING);
        var textX = (float)(ScreenX + PADDING);

        foreach (var line in NameLines)
        {
            TextRenderer.DrawText(
                spriteBatch,
                new Vector2(textX, textY),
                line,
                Color.White);
            textY += LINE_HEIGHT;
        }

        if (DurabilityText is not null)
            TextRenderer.DrawText(
                spriteBatch,
                new Vector2(textX, textY),
                DurabilityText,
                DurabilityColor);
    }

    public void Hide() => Visible = false;

    public void Show(
        string itemName,
        int currentDurability,
        int maxDurability,
        int mouseX,
        int mouseY)
    {
        // Character-wrap the name
        NameLines = CharacterWrap(itemName);
        var hasDurability = maxDurability > 0;
        DurabilityText = hasDurability ? $"{currentDurability}/{maxDurability}" : null;

        // Content dimensions
        var contentWidth = MAX_CHARS_PER_LINE * CHAR_CELL_WIDTH;
        var contentHeight = NameLines.Count * LINE_HEIGHT;

        if (hasDurability)
            contentHeight += LINE_HEIGHT;

        // Total size = content + padding, border overlaps the edges
        var totalWidth = PADDING + contentWidth + PADDING;
        var totalHeight = PADDING + contentHeight + PADDING;

        Width = totalWidth;
        Height = totalHeight;

        // Composite background only (text drawn at draw time)
        TooltipImage?.Texture?.Dispose();

        var texture = CompositeBackground(totalWidth, totalHeight);
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