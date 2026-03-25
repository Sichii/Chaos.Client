#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Generic;

/// <summary>
///     Generic popup text window used for ScrollWindow (Sense), NonScrollWindow (Perish Lore), WoodenBoard (signposts),
///     and Notepad displays. Centered on screen with word-wrapped text, optional scrollbar, and a close button. Escape or
///     click outside closes.
/// </summary>
public sealed class TextPopupControl : UIPanel
{
    private const int LINE_HEIGHT = 12;
    private const int PADDING = 12;
    private const int MIN_WIDTH = 200;
    private const int MIN_HEIGHT = 100;
    private const int MAX_WIDTH = 500;
    private const int MAX_HEIGHT = 400;

    private readonly TextElement[] Lines;
    private readonly int MaxVisibleLines;
    private int DataVersion;
    private int RenderedVersion = -1;
    private bool Scrollable;
    private int ScrollOffset;

    private List<string> WrappedLines = [];

    public TextPopupControl()
    {
        Name = "TextPopup";
        Visible = false;

        MaxVisibleLines = (MAX_HEIGHT - PADDING * 2) / LINE_HEIGHT;
        Lines = new TextElement[MaxVisibleLines];

        for (var i = 0; i < MaxVisibleLines; i++)
            Lines[i] = new TextElement();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        // 2px outer wooden frame
        var sx = ScreenX;
        var sy = ScreenY;

        DrawRect(
            spriteBatch,
            new Rectangle(
                sx - 2,
                sy - 2,
                Width + 4,
                Height + 4),
            new Color(80, 60, 40));

        // Dark background via base.Draw()
        base.Draw(spriteBatch);

        // Text
        RefreshLineCaches();

        var textX = sx + PADDING;
        var textY = sy + PADDING;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            var lineIndex = ScrollOffset + i;

            if (lineIndex >= WrappedLines.Count)
                break;

            Lines[i]
                .Draw(spriteBatch, new Vector2(textX, textY + i * LINE_HEIGHT));
        }

        // Scroll indicator
        if (Scrollable && (WrappedLines.Count > MaxVisibleLines))
        {
            var scrollPct = (float)ScrollOffset / (WrappedLines.Count - MaxVisibleLines);
            var barHeight = Height - PADDING * 2;
            var thumbY = sy + PADDING + (int)(scrollPct * (barHeight - 20));
            var barX = sx + Width - PADDING;

            DrawRect(
                spriteBatch,
                new Rectangle(
                    barX,
                    sy + PADDING,
                    4,
                    barHeight),
                new Color(60, 50, 40));

            DrawRect(
                spriteBatch,
                new Rectangle(
                    barX,
                    thumbY,
                    4,
                    20),
                new Color(160, 140, 100));
        }
    }

    public void Hide()
    {
        Visible = false;
        OnClose?.Invoke();
    }

    public event Action? OnClose;

    private void RefreshLineCaches()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            var lineIndex = ScrollOffset + i;

            Lines[i]
                .Update(lineIndex < WrappedLines.Count ? WrappedLines[lineIndex] : string.Empty, Color.White);
        }
    }

    /// <summary>
    ///     Shows a popup with the given text. PopupStyle controls appearance: Scroll = dark bg with scrollbar, NonScroll =
    ///     dark bg no scroll, Wooden = brown border.
    /// </summary>
    public void Show(string text, PopupStyle style = PopupStyle.Scroll)
    {
        Scrollable = style == PopupStyle.Scroll;

        // Calculate dimensions based on text
        var contentWidth = MAX_WIDTH - PADDING * 2;
        WrappedLines = TextRenderer.WrapLines(text, contentWidth);

        var lineCount = Math.Min(WrappedLines.Count, MaxVisibleLines);
        var contentHeight = lineCount * LINE_HEIGHT;

        Width = Math.Clamp(contentWidth + PADDING * 2, MIN_WIDTH, MAX_WIDTH);
        Height = Math.Clamp(contentHeight + PADDING * 2, MIN_HEIGHT, MAX_HEIGHT);

        X = (640 - Width) / 2;
        Y = (480 - Height) / 2;

        BackgroundColor = new Color(
            20,
            15,
            10,
            240);

        ScrollOffset = 0;
        DataVersion++;
        Visible = true;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        if (input.WasKeyPressed(Keys.Escape))
        {
            Hide();

            return;
        }

        // Click outside to close
        if (input.WasLeftButtonPressed && !ContainsPoint(input.MouseX, input.MouseY))
        {
            Hide();

            return;
        }

        // Scroll
        if (Scrollable && (input.ScrollDelta != 0) && (WrappedLines.Count > MaxVisibleLines))
        {
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, WrappedLines.Count - MaxVisibleLines);

            DataVersion++;
        }
    }
}