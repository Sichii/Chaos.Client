#region
using System.Runtime.InteropServices;
using Chaos.Client.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client;

/// <summary>
///     Buffers keyboard and mouse input using a single SDL event watcher so that discrete key presses and clicks are
///     never lost during frame rate drops and always preserve their true chronological ordering. Call <see cref="Update" />
///     at the start of each frame, then read the snapshot via the query methods.
/// </summary>
public sealed class InputBuffer : IDisposable
{

    private readonly Game Game;
    private readonly HashSet<Keys> HeldKeys = [];

    //accumulation buffers — filled by the SDL event watcher between update() calls
    private readonly List<Keys> PendingPresses = [];
    private readonly List<Keys> PendingReleases = [];
    private readonly List<char> PendingText = [];
    private readonly List<OrderedKeyEvent> PendingOrdered = [];
    private MouseState CurrentMouse;

    //mouse button accumulation — filled by SDL event watcher between update() calls
    private readonly List<BufferedMouseButtonEvent> PendingMouseButtonEvents = [];

    //frame snapshot — frozen at the start of each update()
    private readonly HashSet<Keys> FrameKeyPresses = [];
    private readonly HashSet<Keys> FrameKeyReleases = [];
    private BufferedMouseButtonEvent[] MouseButtonEventBuffer = [];
    private int MouseButtonEventCount;
    private MouseState PreviousMouse;
    private char[] TextBuffer = [];
    private int TextCount;
    private bool WasInactive;
    private OrderedKeyEvent[] OrderedBuffer = [];
    private int OrderedCount;

    //prevent GC of the unmanaged callback delegate
    private readonly Sdl.EventWatchCallback SdlEventWatch;
    private readonly nint SdlEventWatchPtr;

    //virtual resolution transform — raw window coords → virtual 640×480 coords
    private float VirtualScale = 1f;

    public InputBuffer(Game game)
    {
        Game = game;

        SdlEventWatch = OnSdlEvent;
        SdlEventWatchPtr = Marshal.GetFunctionPointerForDelegate(SdlEventWatch);
        Sdl.SDL_AddEventWatch(SdlEventWatchPtr, nint.Zero);
    }

    /// <inheritdoc />
    public void Dispose() => Sdl.SDL_DelEventWatch(SdlEventWatchPtr, nint.Zero);

    //all keyboard, text-input, and mouse-button events funnel through this single
    //watcher callback. SDL fires it synchronously during SDL_PumpEvents on the main
    //thread, in the exact order the OS posted events — that shared ordering is what
    //lets the dispatcher later reconstruct per-event modifier state and preserve the
    //true temporal relationship between keyboard and mouse input.
    private int OnSdlEvent(nint userdata, nint sdlEvent)
    {
        var eventType = (uint)Marshal.ReadInt32(sdlEvent);

        switch (eventType)
        {
            case Sdl.KEYDOWN:
            case Sdl.KEYUP:
                HandleKeyEvent(sdlEvent, isDown: eventType == Sdl.KEYDOWN);

                break;

            case Sdl.TEXTINPUT:
                HandleTextInputEvent(sdlEvent);

                break;

            case Sdl.MOUSEBUTTONDOWN:
            case Sdl.MOUSEBUTTONUP:
                HandleMouseButtonEvent(sdlEvent, isPress: eventType == Sdl.MOUSEBUTTONDOWN);

                break;
        }

        return 1;
    }

    private void HandleKeyEvent(nint sdlEvent, bool isDown)
    {
        var scancode = Marshal.ReadInt32(sdlEvent, Sdl.KEYBOARDEVENT_SCANCODE_OFFSET);
        var key = TranslateScancode(scancode);

        if (key == Keys.None)
            return;

        if (isDown)
        {
            HeldKeys.Add(key);
            PendingPresses.Add(key);
            PendingOrdered.Add(new OrderedKeyEvent(OrderedKeyEventKind.KeyDown, key, '\0'));
        } else
        {
            HeldKeys.Remove(key);
            PendingReleases.Add(key);
            PendingOrdered.Add(new OrderedKeyEvent(OrderedKeyEventKind.KeyUp, key, '\0'));
        }
    }

    private void HandleTextInputEvent(nint sdlEvent)
    {
        //SDL delivers text as a UTF-8 null-terminated string inline in the event struct.
        //For ASCII input (the common case for Dark Ages) it's one byte per character,
        //but IME composition can deliver multi-character strings in a single event.
        var textPtr = sdlEvent + Sdl.TEXTINPUTEVENT_TEXT_OFFSET;
        var text = Marshal.PtrToStringUTF8(textPtr);

        if (string.IsNullOrEmpty(text))
            return;

        foreach (var ch in text)
        {
            PendingText.Add(ch);
            PendingOrdered.Add(new OrderedKeyEvent(OrderedKeyEventKind.TextInput, default, ch));
        }
    }

    private void HandleMouseButtonEvent(nint sdlEvent, bool isPress)
    {
        var sdlButton = Marshal.ReadByte(sdlEvent, Sdl.MOUSEBUTTONEVENT_BUTTON_OFFSET);

        var mouseButton = sdlButton switch
        {
            Sdl.BUTTON_LEFT  => MouseButton.Left,
            Sdl.BUTTON_RIGHT => MouseButton.Right,
            _                => (MouseButton)(-1)
        };

        if ((int)mouseButton < 0)
            return;

        //capture click position at the exact moment of the event in raw window pixels,
        //then translate to virtual coordinates using the same divisor the polled
        //cursor uses. Using per-event coordinates means that a click which lands while
        //the cursor is in flight (turbo-click during fast movement, or a drag release)
        //reports its true position rather than the frame-end cursor position.
        var rawX = Marshal.ReadInt32(sdlEvent, Sdl.MOUSEBUTTONEVENT_X_OFFSET);
        var rawY = Marshal.ReadInt32(sdlEvent, Sdl.MOUSEBUTTONEVENT_Y_OFFSET);
        var virtualX = (int)(rawX / VirtualScale);
        var virtualY = (int)(rawY / VirtualScale);

        //capture modifier state at the exact moment of the event. SDL maintains its
        //own running modifier state; SDL_GetModState() reads it synchronously from
        //within the watcher callback on the same thread SDL updates it on, so the
        //value reflects what was held when the OS posted this button event.
        var mods = TranslateSdlMods(Sdl.SDL_GetModState());

        PendingMouseButtonEvents.Add(new BufferedMouseButtonEvent(mouseButton, isPress, virtualX, virtualY, mods));
    }

    private static KeyModifiers TranslateSdlMods(uint sdlMods)
    {
        var mods = KeyModifiers.None;

        if ((sdlMods & (Sdl.KMOD_LSHIFT | Sdl.KMOD_RSHIFT)) != 0)
            mods |= KeyModifiers.Shift;

        if ((sdlMods & (Sdl.KMOD_LCTRL | Sdl.KMOD_RCTRL)) != 0)
            mods |= KeyModifiers.Ctrl;

        if ((sdlMods & (Sdl.KMOD_LALT | Sdl.KMOD_RALT)) != 0)
            mods |= KeyModifiers.Alt;

        return mods;
    }

    //maps SDL_Scancode values to MonoGame Keys. Numpad digits are normalized to the
    //main number row and numpad Enter to main Enter so hotkeys don't care which one
    //the user hits. Scancodes are physical-key positions, so hotkey behavior is
    //stable across keyboard layouts.
    private static Keys TranslateScancode(int scancode) => scancode switch
    {
        4  => Keys.A,
        5  => Keys.B,
        6  => Keys.C,
        7  => Keys.D,
        8  => Keys.E,
        9  => Keys.F,
        10 => Keys.G,
        11 => Keys.H,
        12 => Keys.I,
        13 => Keys.J,
        14 => Keys.K,
        15 => Keys.L,
        16 => Keys.M,
        17 => Keys.N,
        18 => Keys.O,
        19 => Keys.P,
        20 => Keys.Q,
        21 => Keys.R,
        22 => Keys.S,
        23 => Keys.T,
        24 => Keys.U,
        25 => Keys.V,
        26 => Keys.W,
        27 => Keys.X,
        28 => Keys.Y,
        29 => Keys.Z,
        30 => Keys.D1,
        31 => Keys.D2,
        32 => Keys.D3,
        33 => Keys.D4,
        34 => Keys.D5,
        35 => Keys.D6,
        36 => Keys.D7,
        37 => Keys.D8,
        38 => Keys.D9,
        39 => Keys.D0,
        40 => Keys.Enter,
        41 => Keys.Escape,
        42 => Keys.Back,
        43 => Keys.Tab,
        44 => Keys.Space,
        45 => Keys.OemMinus,
        46 => Keys.OemPlus,
        47 => Keys.OemOpenBrackets,
        48 => Keys.OemCloseBrackets,
        49 => Keys.OemPipe,
        51 => Keys.OemSemicolon,
        52 => Keys.OemQuotes,
        53 => Keys.OemTilde,
        54 => Keys.OemComma,
        55 => Keys.OemPeriod,
        56 => Keys.OemQuestion,
        57 => Keys.CapsLock,
        58 => Keys.F1,
        59 => Keys.F2,
        60 => Keys.F3,
        61 => Keys.F4,
        62 => Keys.F5,
        63 => Keys.F6,
        64 => Keys.F7,
        65 => Keys.F8,
        66 => Keys.F9,
        67 => Keys.F10,
        68 => Keys.F11,
        69 => Keys.F12,
        70 => Keys.PrintScreen,
        71 => Keys.Scroll,
        72 => Keys.Pause,
        73 => Keys.Insert,
        74 => Keys.Home,
        75 => Keys.PageUp,
        76 => Keys.Delete,
        77 => Keys.End,
        78 => Keys.PageDown,
        79 => Keys.Right,
        80 => Keys.Left,
        81 => Keys.Down,
        82 => Keys.Up,
        83 => Keys.NumLock,
        84 => Keys.Divide,
        85 => Keys.Multiply,
        86 => Keys.Subtract,
        87 => Keys.Add,
        88 => Keys.Enter,
        89 => Keys.D1,
        90 => Keys.D2,
        91 => Keys.D3,
        92 => Keys.D4,
        93 => Keys.D5,
        94 => Keys.D6,
        95 => Keys.D7,
        96 => Keys.D8,
        97 => Keys.D9,
        98 => Keys.D0,
        99 => Keys.Decimal,
        224 => Keys.LeftControl,
        225 => Keys.LeftShift,
        226 => Keys.LeftAlt,
        227 => Keys.LeftWindows,
        228 => Keys.RightControl,
        229 => Keys.RightShift,
        230 => Keys.RightAlt,
        231 => Keys.RightWindows,
        _   => Keys.None
    };

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
            ClearMouseButtonEvents();

            //update position (so MouseX/MouseY stay current) but set both states
            //identical so no button edges fire
            CurrentMouse = Mouse.GetState();
            PreviousMouse = CurrentMouse;
            WasInactive = true;

            return;
        }

        //suppress focus-click: when regaining focus, sync PreviousMouse to CurrentMouse
        //so no spurious button edges fire on the activation frame
        if (WasInactive)
        {
            WasInactive = false;
            CurrentMouse = Mouse.GetState();
            PreviousMouse = CurrentMouse;
            ClearMouseButtonEvents();
        }

        //suppress mouse clicks when cursor is outside the client area — Mouse.GetState()
        //reports global button state, so clicking another window shows as Pressed even
        //though the click didn't target our window. Keyboard input still processes so the
        //focused window receives hotkeys regardless of cursor position.
        var raw = Mouse.GetState();
        var clientWidth = (int)(ChaosGame.VIRTUAL_WIDTH * VirtualScale);
        var clientHeight = (int)(ChaosGame.VIRTUAL_HEIGHT * VirtualScale);
        var mouseOutsideClient = (raw.X < 0) || (raw.X >= clientWidth) || (raw.Y < 0) || (raw.Y >= clientHeight);

        if (mouseOutsideClient)
        {
            ClearMouseButtonEvents();
        }
        else
        {
            //freeze mouse button events from SDL watcher
            MouseButtonEventCount = PendingMouseButtonEvents.Count;

            if (MouseButtonEventCount > 0)
            {
                if (MouseButtonEventBuffer.Length < MouseButtonEventCount)
                    MouseButtonEventBuffer = new BufferedMouseButtonEvent[Math.Max(MouseButtonEventCount, 16)];

                for (var i = 0; i < MouseButtonEventCount; i++)
                    MouseButtonEventBuffer[i] = PendingMouseButtonEvents[i];

                PendingMouseButtonEvents.Clear();
            }
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

        if (mouseOutsideClient)
        {
            //set both states identical so no button edges fire while cursor is outside
            CurrentMouse = raw;
            PreviousMouse = CurrentMouse;
        }
        else
        {
            PreviousMouse = CurrentMouse;
            CurrentMouse = Mouse.GetState();
        }
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
    ///     Chronologically ordered mouse button press/release events for this frame (event-driven
    ///     via SDL watcher, so rapid clicks from turbo buttons are never lost between polls).
    /// </summary>
    public ReadOnlySpan<BufferedMouseButtonEvent> MouseButtonEvents
        => MouseButtonEventBuffer.AsSpan(0, MouseButtonEventCount);

    /// <summary>
    ///     Returns true if the left mouse button is currently held down.
    /// </summary>
    public bool IsLeftButtonHeld => CurrentMouse.LeftButton == ButtonState.Pressed;

    /// <summary>
    ///     Returns true if the right mouse button is currently held down.
    /// </summary>
    public bool IsRightButtonHeld => CurrentMouse.RightButton == ButtonState.Pressed;
    #endregion

    private void ClearMouseButtonEvents()
    {
        PendingMouseButtonEvents.Clear();
        MouseButtonEventCount = 0;
    }

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
    KeyUp,
    TextInput
}

public readonly record struct OrderedKeyEvent(OrderedKeyEventKind Kind, Keys Key, char Character);

public readonly record struct BufferedMouseButtonEvent(MouseButton Button, bool IsPress, int X, int Y, KeyModifiers Modifiers);