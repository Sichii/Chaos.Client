#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client;

/// <summary>
///     Buffers keyboard and mouse input using window events so that discrete key presses are never lost during frame rate
///     drops. Call <see cref="Update" /> at the start of each frame, then read the snapshot via the query methods.
/// </summary>
public sealed class InputBuffer : IDisposable
{
    private readonly Game Game;
    private readonly HashSet<Keys> HeldKeys = [];

    //accumulation buffers — filled by window events between update() calls
    private readonly List<Keys> PendingPresses = [];
    private readonly List<Keys> PendingReleases = [];
    private readonly List<char> PendingText = [];
    private readonly List<OrderedKeyEvent> PendingOrdered = [];
    private readonly GameWindow Window;
    private MouseState CurrentMouse;

    //frame snapshot — frozen at the start of each update()
    private readonly HashSet<Keys> FrameKeyPresses = [];
    private readonly HashSet<Keys> FrameKeyReleases = [];
    private MouseState PreviousMouse;
    private char[] TextBuffer = [];
    private int TextCount;
    private OrderedKeyEvent[] OrderedBuffer = [];
    private int OrderedCount;

    //virtual resolution transform — raw window coords → virtual 640×480 coords
    private float VirtualScale = 1f;

    public InputBuffer(Game game)
    {
        Game = game;
        Window = game.Window;

        Window.KeyDown += OnKeyDown;
        Window.KeyUp += OnKeyUp;
        Window.TextInput += OnTextInput;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Window.KeyDown -= OnKeyDown;
        Window.KeyUp -= OnKeyUp;
        Window.TextInput -= OnTextInput;
    }

    private void OnKeyDown(object? sender, InputKeyEventArgs e)
    {
        var key = NormalizeNumpadKey(e.Key);

        HeldKeys.Add(key);
        PendingPresses.Add(key);
        PendingOrdered.Add(new OrderedKeyEvent(OrderedKeyEventKind.KeyDown, key, default));
    }

    private void OnKeyUp(object? sender, InputKeyEventArgs e)
    {
        var key = NormalizeNumpadKey(e.Key);

        HeldKeys.Remove(key);
        PendingReleases.Add(key);
    }

    private static Keys NormalizeNumpadKey(Keys key) => key switch
    {
        Keys.NumPad0 => Keys.D0,
        Keys.NumPad1 => Keys.D1,
        Keys.NumPad2 => Keys.D2,
        Keys.NumPad3 => Keys.D3,
        Keys.NumPad4 => Keys.D4,
        Keys.NumPad5 => Keys.D5,
        Keys.NumPad6 => Keys.D6,
        Keys.NumPad7 => Keys.D7,
        Keys.NumPad8 => Keys.D8,
        Keys.NumPad9 => Keys.D9,
        _            => key
    };

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        PendingText.Add(e.Character);
        PendingOrdered.Add(new OrderedKeyEvent(OrderedKeyEventKind.TextInput, default, e.Character));
    }

    /// <summary>
    ///     Sets the scale factor for translating raw window mouse coordinates to virtual coordinates.
    /// </summary>
    public void SetVirtualScale(float scale) => VirtualScale = scale;

    /// <summary>
    ///     Freezes all buffered input for this frame. Call once at the start of each Update.
    /// </summary>
    public void Update(GameTime gameTime)
    {
        if (!Game.IsActive)
        {
            //window not focused — discard buffered input and report nothing
            PendingPresses.Clear();
            PendingReleases.Clear();
            PendingText.Clear();
            PendingOrdered.Clear();
            HeldKeys.Clear();
            FrameKeyPresses.Clear();
            FrameKeyReleases.Clear();
            TextCount = 0;
            OrderedCount = 0;
            PreviousMouse = CurrentMouse;
            CurrentMouse = Mouse.GetState();

            return;
        }

        FrameKeyPresses.Clear();

        foreach (var key in PendingPresses)
            FrameKeyPresses.Add(key);

        FrameKeyReleases.Clear();

        foreach (var key in PendingReleases)
            FrameKeyReleases.Add(key);

        TextCount = PendingText.Count;

        if (TextCount > 0)
        {
            if (TextBuffer.Length < TextCount)
                TextBuffer = new char[Math.Max(TextCount, 16)];

            for (var i = 0; i < TextCount; i++)
                TextBuffer[i] = PendingText[i];
        }

        OrderedCount = PendingOrdered.Count;

        if (OrderedCount > 0)
        {
            if (OrderedBuffer.Length < OrderedCount)
                OrderedBuffer = new OrderedKeyEvent[Math.Max(OrderedCount, 16)];

            for (var i = 0; i < OrderedCount; i++)
                OrderedBuffer[i] = PendingOrdered[i];
        }

        PendingPresses.Clear();
        PendingReleases.Clear();
        PendingText.Clear();
        PendingOrdered.Clear();

        PreviousMouse = CurrentMouse;
        CurrentMouse = Mouse.GetState();

    }

    #region Keyboard
    /// <summary>
    ///     Returns true if the key is currently held down (event-tracked, not polled).
    /// </summary>
    public bool IsKeyHeld(Keys key) => HeldKeys.Contains(key);

    /// <summary>
    ///     Returns true if the key had a rising edge (was pressed) during this frame. Key-repeat events from the OS are
    ///     filtered out — only the initial press fires.
    /// </summary>
    public bool WasKeyPressed(Keys key) => FrameKeyPresses.Contains(key);

    /// <summary>
    ///     Returns true if the key had a falling edge (was released) during this frame.
    /// </summary>
    public bool WasKeyReleased(Keys key) => FrameKeyReleases.Contains(key);

    /// <summary>
    ///     Characters typed during this frame (from TextInput events). Includes key-repeat characters from the OS.
    /// </summary>
    public ReadOnlySpan<char> TextInput => TextBuffer.AsSpan(0, TextCount);
    #endregion

    #region Mouse
    /// <summary>
    ///     Current mouse X position in virtual coordinates (640×480).
    /// </summary>
    public int MouseX => (int)(CurrentMouse.X / VirtualScale);

    /// <summary>
    ///     Current mouse Y position in virtual coordinates (640×480).
    /// </summary>
    public int MouseY => (int)(CurrentMouse.Y / VirtualScale);

    /// <summary>
    ///     Mouse scroll wheel delta in notches (typically +-1 per wheel click).
    ///     Normalized from the raw MonoGame ScrollWheelValue (120 units per notch).
    /// </summary>
    public int ScrollDelta
    {
        get
        {
            var raw = CurrentMouse.ScrollWheelValue - PreviousMouse.ScrollWheelValue;

            if (raw == 0)
                return 0;

            return Math.Sign(raw) * Math.Max(1, Math.Abs(raw) / 120);
        }
    }

    /// <summary>
    ///     Returns true if the left mouse button was pressed this frame (rising edge).
    /// </summary>
    public bool WasLeftButtonPressed
        => (CurrentMouse.LeftButton == ButtonState.Pressed) && (PreviousMouse.LeftButton == ButtonState.Released);

    /// <summary>
    ///     Returns true if the left mouse button was released this frame (falling edge).
    /// </summary>
    public bool WasLeftButtonReleased
        => (CurrentMouse.LeftButton == ButtonState.Released) && (PreviousMouse.LeftButton == ButtonState.Pressed);

    /// <summary>
    ///     Returns true if the right mouse button was pressed this frame (rising edge).
    /// </summary>
    public bool WasRightButtonPressed
        => (CurrentMouse.RightButton == ButtonState.Pressed) && (PreviousMouse.RightButton == ButtonState.Released);

    /// <summary>
    ///     Returns true if the right mouse button was released this frame (falling edge).
    /// </summary>
    public bool WasRightButtonReleased
        => (CurrentMouse.RightButton == ButtonState.Released) && (PreviousMouse.RightButton == ButtonState.Pressed);

    /// <summary>
    ///     Returns true if the left mouse button is currently held down.
    /// </summary>
    public bool IsLeftButtonHeld => CurrentMouse.LeftButton == ButtonState.Pressed;

    /// <summary>
    ///     Returns true if the right mouse button is currently held down.
    /// </summary>
    public bool IsRightButtonHeld => CurrentMouse.RightButton == ButtonState.Pressed;
    #endregion

    #region Internal Accessors (used by InputDispatcher)
    internal IReadOnlySet<Keys> FramePresses => FrameKeyPresses;
    internal IReadOnlySet<Keys> FrameReleases => FrameKeyReleases;

    /// <summary>
    ///     Chronologically ordered KeyDown and TextInput events for this frame. Preserves the OS
    ///     WM_KEYDOWN → WM_CHAR ordering so the dispatcher can suppress TextInput when its
    ///     preceding KeyDown was consumed as a hotkey.
    /// </summary>
    internal ReadOnlySpan<OrderedKeyEvent> OrderedKeyboardEvents => OrderedBuffer.AsSpan(0, OrderedCount);
    #endregion
}

public enum OrderedKeyEventKind : byte
{
    KeyDown,
    TextInput
}

public readonly record struct OrderedKeyEvent(OrderedKeyEventKind Kind, Keys Key, char Character);