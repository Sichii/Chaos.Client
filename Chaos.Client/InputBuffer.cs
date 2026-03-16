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
    private char[] FrameText = [];
    private MouseState PreviousMouse;

    // Virtual resolution transform — raw window coords → virtual 640×480 coords
    private float VirtualScale = 1f;

    public InputBuffer(GameWindow window)
    {
        Window = window;

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
        // HeldKeys.Add returns false on key-repeat — only buffer the initial press
        if (HeldKeys.Add(e.Key))
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
    public void Update()
    {
        FramePresses = [.. PendingPresses];
        FrameReleases = [.. PendingReleases];
        FrameText = [.. PendingText];

        PendingPresses.Clear();
        PendingReleases.Clear();
        PendingText.Clear();

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
    public bool WasKeyPressed(Keys key) => FramePresses.Contains(key);

    /// <summary>
    ///     Returns true if the key had a falling edge (was released) during this frame.
    /// </summary>
    public bool WasKeyReleased(Keys key) => FrameReleases.Contains(key);

    /// <summary>
    ///     Characters typed during this frame (from TextInput events). Includes key-repeat characters from the OS.
    /// </summary>
    public ReadOnlySpan<char> TextInput => FrameText;
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
    ///     Mouse scroll wheel delta since the previous frame.
    /// </summary>
    public int ScrollDelta => CurrentMouse.ScrollWheelValue - PreviousMouse.ScrollWheelValue;

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
    ///     Returns true if the left mouse button is currently held down.
    /// </summary>
    public bool IsLeftButtonHeld => CurrentMouse.LeftButton == ButtonState.Pressed;

    /// <summary>
    ///     Returns true if the right mouse button is currently held down.
    /// </summary>
    public bool IsRightButtonHeld => CurrentMouse.RightButton == ButtonState.Pressed;
    #endregion
}