#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Multi-line text editor popup for editable and readonly notepad displays. Text is split into lines, each backed by a
///     UITextBox. Vertical scrolling when content exceeds the visible area. OK saves (editable), Cancel/Escape closes. The
///     original DA client uses Width/Height sizing hints: display_chars ~ round(2.5 * Width), display_lines ~ round(1.4 *
///     Height).
/// </summary>
public sealed class NotepadControl : UIPanel
{
    private const int PADDING = 10;
    private const int LINE_HEIGHT = 14;
    private const int BUTTON_WIDTH = 50;
    private const int BUTTON_HEIGHT = 18;
    private const int BUTTON_SPACING = 10;
    private const int BUTTON_AREA_HEIGHT = 28;
    private const int MAX_MESSAGE_LENGTH = 3500;
    private const int SCROLLBAR_WIDTH = 8;
    private const int MIN_WIDTH = 180;
    private const int MIN_HEIGHT = 100;
    private const int MAX_WIDTH = 520;
    private const int MAX_HEIGHT = 420;

    private static readonly Color BackgroundFill = new(
        20,
        15,
        10,
        240);

    private static readonly Color FrameColor = new(80, 60, 40);
    private static readonly Color ButtonFill = new(50, 40, 30);
    private static readonly Color ButtonBorder = new(120, 100, 70);
    private static readonly Color ButtonHoverFill = new(70, 55, 40);
    private static readonly Color ScrollTrackColor = new(40, 35, 25);
    private static readonly Color ScrollThumbColor = new(140, 120, 80);
    private readonly UILabel CancelLabel;
    private readonly UILabel CloseLabel;

    private readonly List<string> Lines = [];
    private readonly int MaxPossibleVisibleLines;

    private readonly UILabel OkLabel;

    // Visible-line labels for readonly rendering
    private readonly UILabel[] ReadonlyLineLabels;
    private Rectangle CancelButtonRect;
    private bool CancelHovered;

    // Editable mode: one UITextBox per visible line, mapped to Lines via ScrollOffset
    private UITextBox[] EditBoxes = [];

    private byte EditSlot;
    private bool IsEditable;
    private int MaxCharsPerLine;

    // Button rectangles (screen-relative within the panel, calculated in Show)
    private Rectangle OkButtonRect;
    private bool OkHovered;

    // Readonly dirty tracking
    private int ReadonlyDataVersion;
    private int ReadonlyRenderedVersion = -1;
    private int ScrollOffset;
    private int VisibleLineCount;

    public NotepadControl()
    {
        Name = "Notepad";
        Visible = false;

        // Pre-allocate readonly line labels for the largest possible visible area
        MaxPossibleVisibleLines = (MAX_HEIGHT - PADDING * 2 - BUTTON_AREA_HEIGHT) / LINE_HEIGHT;
        ReadonlyLineLabels = new UILabel[MaxPossibleVisibleLines];

        for (var i = 0; i < MaxPossibleVisibleLines; i++)
        {
            ReadonlyLineLabels[i] = new UILabel
            {
                Name = $"ReadonlyLine{i}",
                X = PADDING,
                Height = LINE_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0,
                Visible = false
            };

            AddChild(ReadonlyLineLabels[i]);
        }

        OkLabel = new UILabel
        {
            Name = "OkLabel",
            Text = "OK",
            Alignment = TextAlignment.Center,
            PaddingLeft = 0,
            PaddingTop = 0,
            Visible = false
        };

        CancelLabel = new UILabel
        {
            Name = "CancelLabel",
            Text = "Cancel",
            Alignment = TextAlignment.Center,
            PaddingLeft = 0,
            PaddingTop = 0,
            Visible = false
        };

        CloseLabel = new UILabel
        {
            Name = "CloseLabel",
            Text = "Close",
            Alignment = TextAlignment.Center,
            PaddingLeft = 0,
            PaddingTop = 0,
            Visible = false
        };

        AddChild(OkLabel);
        AddChild(CancelLabel);
        AddChild(CloseLabel);
    }

    private string AssembleText()
    {
        // Make sure we capture latest edits
        SyncLinesToEditBoxes();

        // Trim trailing empty lines
        var lastNonEmpty = Lines.Count - 1;

        while ((lastNonEmpty >= 0) && string.IsNullOrEmpty(Lines[lastNonEmpty]))
            lastNonEmpty--;

        if (lastNonEmpty < 0)
            return string.Empty;

        var result = string.Join("\n", Lines.GetRange(0, lastNonEmpty + 1));

        if (result.Length > MAX_MESSAGE_LENGTH)
            result = result[..MAX_MESSAGE_LENGTH];

        return result;
    }

    private void Cancel() => Hide();

    private void ConfigureSize(byte width, byte height)
    {
        // Apply sizing hints from the protocol
        MaxCharsPerLine = (int)Math.Round(2.5 * width);
        VisibleLineCount = (int)Math.Round(1.4 * height);

        if (MaxCharsPerLine < 20)
            MaxCharsPerLine = 20;

        if (VisibleLineCount < 4)
            VisibleLineCount = 4;

        // Clamp visible lines to what we can fit
        if (VisibleLineCount > MaxPossibleVisibleLines)
            VisibleLineCount = MaxPossibleVisibleLines;

        // Calculate pixel dimensions from character/line counts
        var textAreaWidth = MaxCharsPerLine * TextRenderer.CHAR_WIDTH;
        var textAreaHeight = VisibleLineCount * LINE_HEIGHT;

        Width = Math.Clamp(textAreaWidth + PADDING * 2 + SCROLLBAR_WIDTH, MIN_WIDTH, MAX_WIDTH);
        Height = Math.Clamp(textAreaHeight + PADDING * 2 + BUTTON_AREA_HEIGHT, MIN_HEIGHT, MAX_HEIGHT);

        this.CenterOnScreen();

        ScrollOffset = 0;
        BackgroundColor = BackgroundFill;

        // Calculate button positions (centered at bottom of panel)
        var buttonY = Height - BUTTON_AREA_HEIGHT + (BUTTON_AREA_HEIGHT - BUTTON_HEIGHT) / 2;
        var totalButtonWidth = BUTTON_WIDTH * 2 + BUTTON_SPACING;
        var buttonStartX = (Width - totalButtonWidth) / 2;

        OkButtonRect = new Rectangle(
            buttonStartX,
            buttonY,
            BUTTON_WIDTH,
            BUTTON_HEIGHT);

        CancelButtonRect = new Rectangle(
            buttonStartX + BUTTON_WIDTH + BUTTON_SPACING,
            buttonY,
            BUTTON_WIDTH,
            BUTTON_HEIGHT);

        // Position button labels within their button rects
        PositionButtonLabel(OkLabel, OkButtonRect);
        PositionButtonLabel(CancelLabel, CancelButtonRect);
        PositionButtonLabel(CloseLabel, OkButtonRect);

        // Position readonly line labels
        var labelWidth = Width - PADDING * 2 - SCROLLBAR_WIDTH;

        for (var i = 0; i < MaxPossibleVisibleLines; i++)
        {
            ReadonlyLineLabels[i].Y = PADDING + i * LINE_HEIGHT;
            ReadonlyLineLabels[i].Width = labelWidth;
        }
    }

    private void Confirm()
    {
        if (!IsEditable)
        {
            Hide();

            return;
        }

        var text = AssembleText();
        Hide();
        OnSave?.Invoke(EditSlot, text);
    }

    private void CreateEditBoxes()
    {
        DestroyEditBoxes();

        var textAreaWidth = Width - PADDING * 2 - SCROLLBAR_WIDTH;
        EditBoxes = new UITextBox[VisibleLineCount];

        for (var i = 0; i < VisibleLineCount; i++)
        {
            var box = new UITextBox
            {
                Name = $"NoteLine{i}",
                X = PADDING,
                Y = PADDING + i * LINE_HEIGHT,
                Width = textAreaWidth,
                Height = LINE_HEIGHT,
                MaxLength = MaxCharsPerLine,
                PaddingX = 0,
                PaddingY = 1,
                ForegroundColor = Color.White,
                IsFocusable = true,
                IsReadOnly = false,
                IsSelectable = true,
                ZIndex = 1
            };

            EditBoxes[i] = box;
            AddChild(box);
        }

        // Focus the first line
        if (EditBoxes.Length > 0)
            EditBoxes[0].IsFocused = true;
    }

    private void DestroyEditBoxes()
    {
        foreach (var box in EditBoxes)
        {
            box.IsFocused = false;
            RemoveChild(box);
            box.Dispose();
        }

        EditBoxes = [];
    }

    public override void Dispose()
    {
        DestroyEditBoxes();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var sx = ScreenX;
        var sy = ScreenY;

        // Outer frame
        DrawRect(
            spriteBatch,
            new Rectangle(
                sx - 2,
                sy - 2,
                Width + 4,
                Height + 4),
            FrameColor);

        // Update label visibility and button styling based on mode
        UpdateLabelState();

        if (!IsEditable)
            RefreshReadonlyLineLabels();

        // Background + all children (readonly line labels, button labels, edit boxes)
        base.Draw(spriteBatch);

        // Scrollbar
        if (Lines.Count > VisibleLineCount)
            DrawScrollbar(spriteBatch, sx, sy);
    }

    private void DrawScrollbar(SpriteBatch spriteBatch, int sx, int sy)
    {
        var barX = sx + Width - PADDING - SCROLLBAR_WIDTH + 2;
        var barY = sy + PADDING;
        var barHeight = VisibleLineCount * LINE_HEIGHT;
        var maxScroll = Math.Max(1, Lines.Count - VisibleLineCount);
        var scrollPct = (float)ScrollOffset / maxScroll;
        var thumbHeight = Math.Max(12, barHeight * VisibleLineCount / Lines.Count);
        var thumbY = barY + (int)(scrollPct * (barHeight - thumbHeight));

        DrawRect(
            spriteBatch,
            new Rectangle(
                barX,
                barY,
                SCROLLBAR_WIDTH - 2,
                barHeight),
            ScrollTrackColor);

        DrawRect(
            spriteBatch,
            new Rectangle(
                barX,
                thumbY,
                SCROLLBAR_WIDTH - 2,
                thumbHeight),
            ScrollThumbColor);
    }

    /// <summary>
    ///     Returns the index of the focused UITextBox within EditBoxes, or -1 if none.
    /// </summary>
    private int GetFocusedBoxIndex()
    {
        for (var i = 0; i < EditBoxes.Length; i++)
            if (EditBoxes[i].IsFocused)
                return i;

        return -1;
    }

    public void Hide()
    {
        Visible = false;
        DestroyEditBoxes();
    }

    private void LoadText(string message)
    {
        Lines.Clear();

        if (string.IsNullOrEmpty(message))
        {
            Lines.Add(string.Empty);

            return;
        }

        // Normalize literal escape sequences to actual newlines
        var normalized = message.Replace("\\n", "\n")
                                .Replace("\\r", "\r")
                                .Replace("\r\n", "\n")
                                .Replace("\r", "\n");

        // Split on newlines, then word-wrap each paragraph to fit the line width
        var paragraphs = normalized.Split('\n');

        var linePixelWidth = (Width - PADDING * 2 - SCROLLBAR_WIDTH) > 0
            ? Width - PADDING * 2 - SCROLLBAR_WIDTH
            : MaxCharsPerLine * TextRenderer.CHAR_WIDTH;

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                Lines.Add(string.Empty);

                continue;
            }

            var wrapped = TextRenderer.WrapLines(paragraph, linePixelWidth);

            if (wrapped.Count == 0)
                Lines.Add(string.Empty);
            else
                Lines.AddRange(wrapped);
        }
    }

    /// <summary>
    ///     Fired when the user confirms editable notepad text. Parameters: slot, edited message.
    /// </summary>
    public event Action<byte, string>? OnSave;

    private static void PositionButtonLabel(UILabel label, Rectangle buttonRect)
    {
        label.X = buttonRect.X;
        label.Y = buttonRect.Y;
        label.Width = buttonRect.Width;
        label.Height = buttonRect.Height;
    }

    private void RefreshReadonlyLineLabels()
    {
        if (ReadonlyRenderedVersion == ReadonlyDataVersion)
            return;

        ReadonlyRenderedVersion = ReadonlyDataVersion;

        for (var i = 0; i < MaxPossibleVisibleLines; i++)
        {
            var lineIndex = ScrollOffset + i;
            ReadonlyLineLabels[i].Text = lineIndex < Lines.Count ? Lines[lineIndex] : string.Empty;
            ReadonlyLineLabels[i].ForegroundColor = Color.White;
        }
    }

    /// <summary>
    ///     Removes a child element from this panel.
    /// </summary>
    private void RemoveChild(UIElement child)
    {
        Children.Remove(child);
        child.Parent = null;
    }

    /// <summary>
    ///     Shows the notepad in editable mode with the given message text and sizing hints.
    /// </summary>
    public void ShowEditable(
        byte slot,
        byte width,
        byte height,
        string message)
    {
        EditSlot = slot;
        IsEditable = true;
        ConfigureSize(width, height);
        LoadText(message);
        CreateEditBoxes();
        SyncEditBoxesToLines();
        Visible = true;
    }

    /// <summary>
    ///     Shows the notepad in readonly mode.
    /// </summary>
    public void ShowReadonly(byte width, byte height, string message)
    {
        IsEditable = false;
        ConfigureSize(width, height);
        LoadText(message);
        ReadonlyDataVersion++;
        Visible = true;
    }

    /// <summary>
    ///     Copies Lines data into the visible UITextBox controls based on the current ScrollOffset.
    /// </summary>
    private void SyncEditBoxesToLines()
    {
        for (var i = 0; i < EditBoxes.Length; i++)
        {
            var lineIndex = ScrollOffset + i;

            if (lineIndex < Lines.Count)
            {
                EditBoxes[i].Text = Lines[lineIndex];
                EditBoxes[i].Visible = true;
            } else
            {
                EditBoxes[i].Text = string.Empty;
                EditBoxes[i].Visible = true;
            }
        }
    }

    /// <summary>
    ///     Copies text from the visible UITextBox controls back into Lines.
    /// </summary>
    private void SyncLinesToEditBoxes()
    {
        for (var i = 0; i < EditBoxes.Length; i++)
        {
            var lineIndex = ScrollOffset + i;

            if (lineIndex < Lines.Count)
                Lines[lineIndex] = EditBoxes[i].Text;
            else if (!string.IsNullOrEmpty(EditBoxes[i].Text))
            {
                // Extend Lines if user typed into a line beyond current count
                while (Lines.Count <= lineIndex)
                    Lines.Add(string.Empty);

                Lines[lineIndex] = EditBoxes[i].Text;
            }
        }
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Escape closes
        if (input.WasKeyPressed(Keys.Escape))
        {
            Cancel();

            return;
        }

        // Button hover detection
        var mx = input.MouseX - ScreenX;
        var my = input.MouseY - ScreenY;
        OkHovered = OkButtonRect.Contains(mx, my);
        CancelHovered = CancelButtonRect.Contains(mx, my);

        // Button clicks
        if (input.WasLeftButtonPressed)
        {
            if (OkHovered)
            {
                Confirm();

                return;
            }

            if (CancelHovered)
            {
                Cancel();

                return;
            }
        }

        if (IsEditable)
            UpdateEditable(gameTime, input);
        else
            UpdateReadonly(input);
    }

    private void UpdateEditable(GameTime gameTime, InputBuffer input)
    {
        var focusedIndex = GetFocusedBoxIndex();

        // Enter inserts a new line below the focused line
        if (input.WasKeyPressed(Keys.Enter) && (focusedIndex >= 0))
        {
            SyncLinesToEditBoxes();
            var lineIndex = ScrollOffset + focusedIndex;
            var currentBox = EditBoxes[focusedIndex];

            // Split the current line at cursor position
            var cursorPos = Math.Min(currentBox.CursorPosition, currentBox.Text.Length);
            var beforeCursor = currentBox.Text[..cursorPos];
            var afterCursor = currentBox.Text[cursorPos..];

            Lines[lineIndex] = beforeCursor;

            if ((lineIndex + 1) < Lines.Count)
                Lines.Insert(lineIndex + 1, afterCursor);
            else
                Lines.Add(afterCursor);

            // Move focus to the new line
            if (focusedIndex < (EditBoxes.Length - 1))
            {
                // Next box is visible — just sync and move focus
                SyncEditBoxesToLines();
                EditBoxes[focusedIndex].IsFocused = false;
                EditBoxes[focusedIndex + 1].IsFocused = true;
                EditBoxes[focusedIndex + 1].CursorPosition = 0;
            } else
            {
                // At bottom of visible area — scroll down
                ScrollOffset++;
                SyncEditBoxesToLines();

                // Keep focus on the last visible box (which now shows the new line)
                EditBoxes[focusedIndex].IsFocused = true;
                EditBoxes[focusedIndex].CursorPosition = 0;
            }

            return;
        }

        // Backspace at start of line joins with previous line
        if (input.WasKeyPressed(Keys.Back) && (focusedIndex >= 0))
        {
            var currentBox = EditBoxes[focusedIndex];

            if (currentBox is { CursorPosition: 0, HasSelection: false })
            {
                var lineIndex = ScrollOffset + focusedIndex;

                if (lineIndex > 0)
                {
                    SyncLinesToEditBoxes();

                    var prevLineText = Lines[lineIndex - 1];
                    var curLineText = Lines[lineIndex];
                    var joinedLength = prevLineText.Length;

                    // Join lines (truncate if too long)
                    var joined = prevLineText + curLineText;

                    if (joined.Length > MaxCharsPerLine)
                        joined = joined[..MaxCharsPerLine];

                    Lines[lineIndex - 1] = joined;
                    Lines.RemoveAt(lineIndex);

                    // Move focus to previous line at the join point
                    if (focusedIndex > 0)
                    {
                        SyncEditBoxesToLines();
                        EditBoxes[focusedIndex].IsFocused = false;
                        EditBoxes[focusedIndex - 1].IsFocused = true;
                        EditBoxes[focusedIndex - 1].CursorPosition = Math.Min(joinedLength, EditBoxes[focusedIndex - 1].Text.Length);
                    } else if (ScrollOffset > 0)
                    {
                        ScrollOffset--;
                        SyncEditBoxesToLines();
                        EditBoxes[0].IsFocused = true;
                        EditBoxes[0].CursorPosition = Math.Min(joinedLength, EditBoxes[0].Text.Length);
                    }

                    return;
                }
            }
        }

        // Delete at end of line joins with next line
        if (input.WasKeyPressed(Keys.Delete) && (focusedIndex >= 0))
        {
            var currentBox = EditBoxes[focusedIndex];

            if ((currentBox.CursorPosition >= currentBox.Text.Length) && !currentBox.HasSelection)
            {
                var lineIndex = ScrollOffset + focusedIndex;

                if ((lineIndex + 1) < Lines.Count)
                {
                    SyncLinesToEditBoxes();

                    var curLineText = Lines[lineIndex];
                    var nextLineText = Lines[lineIndex + 1];
                    var cursorPos = curLineText.Length;

                    var joined = curLineText + nextLineText;

                    if (joined.Length > MaxCharsPerLine)
                        joined = joined[..MaxCharsPerLine];

                    Lines[lineIndex] = joined;
                    Lines.RemoveAt(lineIndex + 1);
                    SyncEditBoxesToLines();
                    EditBoxes[focusedIndex].CursorPosition = Math.Min(cursorPos, EditBoxes[focusedIndex].Text.Length);

                    return;
                }
            }
        }

        // Arrow up/down moves between lines
        if (input.WasKeyPressed(Keys.Up) && (focusedIndex >= 0))
        {
            if (focusedIndex > 0)
            {
                SyncLinesToEditBoxes();
                var cursorPos = EditBoxes[focusedIndex].CursorPosition;
                EditBoxes[focusedIndex].IsFocused = false;
                EditBoxes[focusedIndex - 1].IsFocused = true;
                EditBoxes[focusedIndex - 1].CursorPosition = Math.Min(cursorPos, EditBoxes[focusedIndex - 1].Text.Length);
            } else if (ScrollOffset > 0)
            {
                SyncLinesToEditBoxes();
                var cursorPos = EditBoxes[focusedIndex].CursorPosition;
                ScrollOffset--;
                SyncEditBoxesToLines();
                EditBoxes[0].IsFocused = true;
                EditBoxes[0].CursorPosition = Math.Min(cursorPos, EditBoxes[0].Text.Length);
            }

            return;
        }

        if (input.WasKeyPressed(Keys.Down) && (focusedIndex >= 0))
        {
            var lineIndex = ScrollOffset + focusedIndex;

            if (lineIndex < (Lines.Count - 1))
            {
                if (focusedIndex < (EditBoxes.Length - 1))
                {
                    SyncLinesToEditBoxes();
                    var cursorPos = EditBoxes[focusedIndex].CursorPosition;
                    EditBoxes[focusedIndex].IsFocused = false;
                    EditBoxes[focusedIndex + 1].IsFocused = true;
                    EditBoxes[focusedIndex + 1].CursorPosition = Math.Min(cursorPos, EditBoxes[focusedIndex + 1].Text.Length);
                } else
                {
                    SyncLinesToEditBoxes();
                    var cursorPos = EditBoxes[focusedIndex].CursorPosition;
                    ScrollOffset++;
                    SyncEditBoxesToLines();
                    EditBoxes[^1].IsFocused = true;
                    EditBoxes[^1].CursorPosition = Math.Min(cursorPos, EditBoxes[^1].Text.Length);
                }
            }

            return;
        }

        // Scroll wheel
        if ((input.ScrollDelta != 0) && (Lines.Count > VisibleLineCount))
        {
            SyncLinesToEditBoxes();
            var maxScroll = Math.Max(0, Lines.Count - VisibleLineCount);
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, maxScroll);
            SyncEditBoxesToLines();
        }

        // Let UITextBox children handle their own input (character typing, cursor, selection)
        base.Update(gameTime, input);

        // After textbox updates, sync any changes back to Lines
        SyncLinesToEditBoxes();
    }

    private void UpdateLabelState()
    {
        // Button labels — visibility and hover styling
        OkLabel.Visible = IsEditable;
        OkLabel.BackgroundColor = OkHovered ? ButtonHoverFill : ButtonFill;
        OkLabel.BorderColor = ButtonBorder;

        CloseLabel.Visible = !IsEditable;
        CloseLabel.BackgroundColor = OkHovered ? ButtonHoverFill : ButtonFill;
        CloseLabel.BorderColor = ButtonBorder;

        CancelLabel.Visible = IsEditable;
        CancelLabel.BackgroundColor = CancelHovered ? ButtonHoverFill : ButtonFill;
        CancelLabel.BorderColor = ButtonBorder;

        // Readonly line labels
        for (var i = 0; i < MaxPossibleVisibleLines; i++)
            ReadonlyLineLabels[i].Visible = !IsEditable && (i < VisibleLineCount) && ((ScrollOffset + i) < Lines.Count);
    }

    private void UpdateReadonly(InputBuffer input)
    {
        // Click outside to close
        if (input.WasLeftButtonPressed && !ContainsPoint(input.MouseX, input.MouseY))
        {
            Hide();

            return;
        }

        // Scroll
        if ((input.ScrollDelta != 0) && (Lines.Count > VisibleLineCount))
        {
            var maxScroll = Math.Max(0, Lines.Count - VisibleLineCount);
            ScrollOffset = Math.Clamp(ScrollOffset - input.ScrollDelta, 0, maxScroll);
            ReadonlyDataVersion++;
        }
    }
}