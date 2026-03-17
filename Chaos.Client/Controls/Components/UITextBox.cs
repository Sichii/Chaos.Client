#region
using Chaos.Client.Definitions;
using Chaos.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Components;

// ReSharper disable once ClassCanBeSealed.Global
public class UITextBox : UIElement
{
    private const int CURSOR_BLINK_MS = 530;
    private const int CURSOR_WIDTH = 1;

    private readonly GraphicsDevice Device;
    private readonly CachedText TextCache;
    private double CursorTimer;
    private bool CursorVisible;
    private bool Dragging;
    private CachedText? PrefixCache;
    private int SelectionAnchor;
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    public int CursorPosition { get; private set; }

    /// <summary>
    ///     Background color drawn behind the textbox when focused. Null = no overlay.
    /// </summary>
    public Color? FocusedBackgroundColor { get; set; }

    public bool IsFocusable { get; set; } = true;

    public bool IsFocused
    {
        get => field;

        set
        {
            field = value;
            BackgroundColor = value ? FocusedBackgroundColor : null;
        }
    }

    public bool IsMasked { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsSelectable { get; set; } = true;
    public int MaxLength { get; set; } = 12;

    public int PaddingX { get; set; } = 2;
    public int PaddingY { get; set; } = 2;

    /// <summary>
    ///     Non-editable prefix rendered before the editable text (e.g. "Name: " for chat). Not included in <see cref="Text" />
    ///     and cannot be deleted by the user.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>
    ///     Color used for rendering both prefix and editable text. Default white.
    /// </summary>
    public Color TextColor { get; set; } = Color.White;

    public bool HasSelection => IsSelectable && (SelectionAnchor != CursorPosition);

    public string SelectedText => HasSelection ? Text[SelectionStart..Math.Min(SelectionEnd, Text.Length)] : string.Empty;

    public int SelectionEnd => Math.Max(SelectionAnchor, CursorPosition);
    public int SelectionLength => SelectionEnd - SelectionStart;

    public int SelectionStart => Math.Min(SelectionAnchor, CursorPosition);

    public UITextBox(GraphicsDevice device)
    {
        Device = device;
        TextCache = new CachedText(device);
    }

    /// <summary>
    ///     Ensures cursor and anchor are within valid range after external Text changes.
    /// </summary>
    private void ClampPositions()
    {
        var len = Text.Length;

        if (CursorPosition > len)
            CursorPosition = len;

        if (SelectionAnchor > len)
            SelectionAnchor = len;
    }

    private void DeleteSelection()
    {
        if (!HasSelection)
            return;

        var start = SelectionStart;
        var length = Math.Min(SelectionLength, Text.Length - start);

        Text = Text.Remove(start, length);
        CursorPosition = start;
        SelectionAnchor = start;
    }

    public override void Dispose()
    {
        TextCache.Dispose();
        PrefixCache?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var displayText = IsMasked ? new string('*', Text.Length) : Text;
        var sx = ScreenX;
        var sy = ScreenY;
        var textY = sy + PaddingY;
        var textHeight = Height - PaddingY * 2;

        // Prefix offset — non-editable text rendered before editable content
        var prefixWidth = 0;

        if ((Prefix.Length > 0) && IsFocused)
        {
            prefixWidth = TextRenderer.MeasureWidth(Prefix);
            PrefixCache ??= new CachedText(Device);
            PrefixCache.Update(Prefix, TextColor);
            PrefixCache.Draw(spriteBatch, new Vector2(sx + PaddingX, textY));
        }

        var textStartX = sx + PaddingX + prefixWidth;

        // Selection highlight
        if (HasSelection && (displayText.Length > 0))
        {
            var selStart = Math.Min(SelectionStart, displayText.Length);
            var selEnd = Math.Min(SelectionEnd, displayText.Length);
            var selStartX = textStartX;

            if (selStart > 0)
                selStartX += TextRenderer.MeasureWidth(displayText[..selStart]);

            var selWidth = TextRenderer.MeasureWidth(displayText[selStart..selEnd]);

            DrawRect(
                spriteBatch,
                Device,
                new Rectangle(
                    selStartX,
                    textY,
                    selWidth,
                    textHeight),
                new Color(
                    80,
                    120,
                    200,
                    150));
        }

        TextCache.Update(displayText, TextColor);

        if ((Alignment != TextAlignment.Left) && !IsFocused)
        {
            TextCache.Alignment = Alignment;

            TextCache.Draw(
                spriteBatch,
                new Rectangle(
                    sx + PaddingX,
                    textY,
                    Width - PaddingX * 2,
                    textHeight));
        } else
        {
            TextCache.Alignment = TextAlignment.Left;
            TextCache.Draw(spriteBatch, new Vector2(textStartX, textY));
        }

        if (!IsFocused || !CursorVisible || IsReadOnly)
            return;

        var cursorX = textStartX;
        var clampedPos = Math.Min(CursorPosition, displayText.Length);

        if (clampedPos > 0)
            cursorX += TextRenderer.MeasureWidth(displayText[..clampedPos]) + 1;

        DrawRect(
            spriteBatch,
            Device,
            new Rectangle(
                cursorX,
                textY,
                CURSOR_WIDTH,
                textHeight),
            Color.White);
    }

    private int FindWordBoundaryLeft(int from)
    {
        if (from <= 0)
            return 0;

        var i = from - 1;

        while ((i > 0) && (Text[i] == ' '))
            i--;

        while ((i > 0) && (Text[i - 1] != ' '))
            i--;

        return i;
    }

    private int FindWordBoundaryRight(int from)
    {
        if (from >= Text.Length)
            return Text.Length;

        var i = from;

        while ((i < Text.Length) && (Text[i] != ' '))
            i++;

        while ((i < Text.Length) && (Text[i] == ' '))
            i++;

        return i;
    }

    private void HandleBackspace()
    {
        if (HasSelection)
            DeleteSelection();
        else if (CursorPosition > 0)
        {
            // Capture target position before mutating Text
            var newPos = CursorPosition - 1;
            Text = Text.Remove(newPos, 1);
            CursorPosition = newPos;
            SelectionAnchor = newPos;
        }

        ResetCursor();
    }

    private void HandleEditing(InputBuffer input, bool ctrl)
    {
        // Delete key
        if (input.WasKeyPressed(Keys.Delete) && !IsReadOnly)
        {
            if (HasSelection)
                DeleteSelection();
            else if (CursorPosition < Text.Length)
                Text = Text.Remove(CursorPosition, 1);

            ResetCursor();
        }

        if (IsReadOnly)
            return;

        // Ctrl+A handled in navigation, skip 'a' character input when ctrl is held
        if (ctrl)
            return;

        foreach (var c in input.TextInput)
        {
            if (c == '\b')
            {
                HandleBackspace();

                continue;
            }

            // Tab signals focus transfer — parent handles the actual transfer
            if (c == '\t')
                continue;

            if (char.IsControl(c))
                continue;

            // Replace selection with typed character
            if (HasSelection)
                DeleteSelection();

            if (Text.Length >= MaxLength)
                continue;

            // Capture position before mutation to avoid setter interactions
            var insertPos = CursorPosition;
            Text = Text.Insert(insertPos, c.ToString());
            CursorPosition = insertPos + 1;
            SelectionAnchor = CursorPosition;
            ResetCursor();
        }
    }

    private void HandleMouse(InputBuffer input, bool shift)
    {
        // Mouse down — focus, set cursor, begin drag
        if (IsFocusable && input.WasLeftButtonPressed && ContainsPoint(input.MouseX, input.MouseY))
        {
            var clickPos = HitTestCursorPosition(input.MouseX - ScreenX - PaddingX);

            if (shift && IsFocused && IsSelectable)

                // Shift+click extends selection — anchor stays, cursor moves
                CursorPosition = clickPos;
            else
            {
                CursorPosition = clickPos;
                SelectionAnchor = clickPos;
            }

            Dragging = true;
            ResetCursor();

            if (!IsFocused)
            {
                IsFocused = true;
                OnFocused?.Invoke(this);
            }
        }

        // Mouse drag — extend selection
        if (Dragging && IsSelectable && input.IsLeftButtonHeld)
        {
            var dragPos = HitTestCursorPosition(input.MouseX - ScreenX - PaddingX);

            if (dragPos != CursorPosition)
            {
                CursorPosition = dragPos;
                ResetCursor();
            }
        }

        // Mouse released — end drag
        if (Dragging && !input.IsLeftButtonHeld)
            Dragging = false;
    }

    private void HandleNavigation(InputBuffer input, bool shift, bool ctrl)
    {
        if (input.WasKeyPressed(Keys.Left))
        {
            if (!shift && HasSelection)
                MoveCursor(SelectionStart, false);
            else if (CursorPosition > 0)
                MoveCursor(ctrl ? FindWordBoundaryLeft(CursorPosition) : CursorPosition - 1, shift);
        }

        if (input.WasKeyPressed(Keys.Right))
        {
            if (!shift && HasSelection)
                MoveCursor(SelectionEnd, false);
            else if (CursorPosition < Text.Length)
                MoveCursor(ctrl ? FindWordBoundaryRight(CursorPosition) : CursorPosition + 1, shift);
        }

        if (input.WasKeyPressed(Keys.Home))
            MoveCursor(0, shift);

        if (input.WasKeyPressed(Keys.End))
            MoveCursor(Text.Length, shift);

        // Ctrl+A to select all
        if (IsSelectable && ctrl && input.WasKeyPressed(Keys.A))
            SelectAll();
    }

    /// <summary>
    ///     Determines which character position the click landed on by measuring character widths.
    /// </summary>
    private int HitTestCursorPosition(int localX)
    {
        // Offset by prefix width when focused
        if ((Prefix.Length > 0) && IsFocused)
            localX -= TextRenderer.MeasureWidth(Prefix);

        if ((Text.Length == 0) || (localX <= 0))
            return 0;

        var displayText = IsMasked ? new string('*', Text.Length) : Text;

        for (var i = 1; i <= displayText.Length; i++)
        {
            var charWidth = TextRenderer.MeasureWidth(displayText[..i]);
            var prevWidth = i > 1 ? TextRenderer.MeasureWidth(displayText[..(i - 1)]) : 0;

            var midpoint = (prevWidth + charWidth) / 2;

            if (localX < midpoint)
                return i - 1;
        }

        return displayText.Length;
    }

    /// <summary>
    ///     Moves the cursor to a new position. When extendSelection is false, the selection anchor follows the cursor (no
    ///     selection). When true, the anchor stays put so the selection grows or shrinks.
    /// </summary>
    private void MoveCursor(int newPosition, bool extendSelection)
    {
        // If starting a new selection, pin the anchor at the current position
        if (extendSelection && !HasSelection)
            SelectionAnchor = CursorPosition;

        CursorPosition = Math.Clamp(newPosition, 0, Text.Length);

        // When not extending, collapse selection by syncing anchor to new cursor position
        if (!extendSelection)
            SelectionAnchor = CursorPosition;

        ResetCursor();
    }

    public event Action<UITextBox>? OnFocused;

    private void ResetCursor()
    {
        CursorVisible = true;
        CursorTimer = 0;
    }

    public void SelectAll()
    {
        if (!IsSelectable || (Text.Length == 0))
            return;

        SelectionAnchor = 0;
        CursorPosition = Text.Length;
    }

    public override void Update(GameTime gameTime, InputBuffer input)
    {
        if (!Visible || !Enabled)
            return;

        // Clamp positions in case Text was changed externally
        ClampPositions();

        var shift = input.IsKeyHeld(Keys.LeftShift) || input.IsKeyHeld(Keys.RightShift);
        var ctrl = input.IsKeyHeld(Keys.LeftControl) || input.IsKeyHeld(Keys.RightControl);

        HandleMouse(input, shift);

        if (!IsFocused)
            return;

        UpdateCursorBlink(gameTime);
        HandleNavigation(input, shift, ctrl);
        HandleEditing(input, ctrl);
    }

    private void UpdateCursorBlink(GameTime gameTime)
    {
        CursorTimer += gameTime.ElapsedGameTime.TotalMilliseconds;

        if (CursorTimer >= CURSOR_BLINK_MS)
        {
            CursorVisible = !CursorVisible;
            CursorTimer -= CURSOR_BLINK_MS;
        }
    }
}