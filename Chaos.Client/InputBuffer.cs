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
    private const float DOUBLE_CLICK_MS = 300f;
    private readonly Game Game;
    private readonly HashSet<Keys> HeldKeys = [];

    // Accumulation buffers — filled by window events between Update() calls
    private readonly List<Keys> PendingPresses = [];
    private readonly List<Keys> PendingReleases = [];
    private readonly List<char> PendingText = [];
    private readonly GameWindow Window;
    private MouseState CurrentMouse;

    // Frame snapshot — frozen at the start of each Update()
    private HashSet<Keys> FramePresses = [];
    private HashSet<Keys> FrameReleases = [];
    private float LastLeftPressMs;
    private float LastRightPressMs;
    private bool LeftDoubleClicked;
    private MouseState PreviousMouse;
    private bool RightDoubleClicked;
    private char[] TextBuffer = [];
    private int TextCount;

    // Virtual resolution transform — raw window coords → virtual 640×480 coords
    private float VirtualScale = 1f;

    /// <summary>
    ///     When true, all input queries return empty/false. Used to let modal popups capture input while other controls
    ///     continue updating (animations, cooldowns).
    /// </summary>
    public bool Suppressed { get; set; }

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
        HeldKeys.Add(e.Key);
        PendingPresses.Add(e.Key);
    }

    private void OnKeyUp(object? sender, InputKeyEventArgs e)
    {
        HeldKeys.Remove(e.Key);
        PendingReleases.Add(e.Key);
    }

    private void OnTextInput(object? sender, TextInputEventArgs e) => PendingText.Add(e.Character);

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
            // Window not focused — discard buffered input and report nothing
            PendingPresses.Clear();
            PendingReleases.Clear();
            PendingText.Clear();
            HeldKeys.Clear();
            FramePresses.Clear();
            FrameReleases.Clear();
            TextCount = 0;
            PreviousMouse = CurrentMouse;
            CurrentMouse = Mouse.GetState();
            LeftDoubleClicked = false;
            RightDoubleClicked = false;
            LastLeftPressMs = 0;
            LastRightPressMs = 0;

            return;
        }

        FramePresses.Clear();

        foreach (var key in PendingPresses)
            FramePresses.Add(key);

        FrameReleases.Clear();

        foreach (var key in PendingReleases)
            FrameReleases.Add(key);

        TextCount = PendingText.Count;

        if (TextCount > 0)
        {
            if (TextBuffer.Length < TextCount)
                TextBuffer = new char[Math.Max(TextCount, 16)];

            for (var i = 0; i < TextCount; i++)
                TextBuffer[i] = PendingText[i];
        }

        PendingPresses.Clear();
        PendingReleases.Clear();
        PendingText.Clear();

        PreviousMouse = CurrentMouse;
        CurrentMouse = Mouse.GetState();

        // Double-click detection
        var now = (float)gameTime.TotalGameTime.TotalMilliseconds;

        LeftDoubleClicked = false;

        if (WasLeftButtonPressed)
        {
            LeftDoubleClicked = (now - LastLeftPressMs) < DOUBLE_CLICK_MS;
            LastLeftPressMs = now;
        }

        RightDoubleClicked = false;

        if (WasRightButtonPressed)
        {
            RightDoubleClicked = (now - LastRightPressMs) < DOUBLE_CLICK_MS;
            LastRightPressMs = now;
        }
    }

    #region Keyboard
    /// <summary>
    ///     Returns true if the key is currently held down (event-tracked, not polled).
    /// </summary>
    public bool IsKeyHeld(Keys key) => !Suppressed && HeldKeys.Contains(key);

    /// <summary>
    ///     Returns true if the key had a rising edge (was pressed) during this frame. Key-repeat events from the OS are
    ///     filtered out — only the initial press fires.
    /// </summary>
    public bool WasKeyPressed(Keys key) => !Suppressed && FramePresses.Contains(key);

    /// <summary>
    ///     Returns true if the key had a falling edge (was released) during this frame.
    /// </summary>
    public bool WasKeyReleased(Keys key) => !Suppressed && FrameReleases.Contains(key);

    /// <summary>
    ///     Characters typed during this frame (from TextInput events). Includes key-repeat characters from the OS.
    /// </summary>
    public ReadOnlySpan<char> TextInput => Suppressed ? ReadOnlySpan<char>.Empty : TextBuffer.AsSpan(0, TextCount);
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
            if (Suppressed)
                return 0;

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
        => !Suppressed && (CurrentMouse.LeftButton == ButtonState.Pressed) && (PreviousMouse.LeftButton == ButtonState.Released);

    /// <summary>
    ///     Returns true if the left mouse button was double-clicked (two presses within 300ms). Consumers should additionally
    ///     verify the same logical target was clicked.
    /// </summary>
    public bool WasLeftButtonDoubleClicked => !Suppressed && LeftDoubleClicked;

    /// <summary>
    ///     Returns true if the left mouse button was released this frame (falling edge).
    /// </summary>
    public bool WasLeftButtonReleased
        => !Suppressed && (CurrentMouse.LeftButton == ButtonState.Released) && (PreviousMouse.LeftButton == ButtonState.Pressed);

    /// <summary>
    ///     Returns true if the right mouse button was pressed this frame (rising edge).
    /// </summary>
    public bool WasRightButtonPressed
        => !Suppressed && (CurrentMouse.RightButton == ButtonState.Pressed) && (PreviousMouse.RightButton == ButtonState.Released);

    /// <summary>
    ///     Returns true if the right mouse button was double-clicked (two presses within 300ms). Consumers should additionally
    ///     verify the same logical target was clicked.
    /// </summary>
    public bool WasRightButtonDoubleClicked => !Suppressed && RightDoubleClicked;

    /// <summary>
    ///     Returns true if the left mouse button is currently held down.
    /// </summary>
    public bool IsLeftButtonHeld => !Suppressed && (CurrentMouse.LeftButton == ButtonState.Pressed);

    /// <summary>
    ///     Returns true if the right mouse button is currently held down.
    /// </summary>
    public bool IsRightButtonHeld => !Suppressed && (CurrentMouse.RightButton == ButtonState.Pressed);
    #endregion
}