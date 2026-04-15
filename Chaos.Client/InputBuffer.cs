#region
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client;

/// <summary>
///     Static, process-global input buffer. Captures keyboard, text, mouse button, and
///     mouse wheel events from SDL via a single <c>SDL_AddEventWatch</c> callback so that
///     every discrete event is preserved in its true OS post order and carries the modifier
///     state that was live at the moment it fired. Also tracks the live cursor position
///     (refreshed from <c>SDL_GetMouseState</c> on every <see cref="Update" />) and the
///     per-window button-held flags.
///     <para>
///         Lifecycle: call <see cref="Initialize" /> once at startup (installs the SDL
///         watcher), then <see cref="Update" /> at the start of every frame (drains the
///         accumulated events into the frame snapshot and refreshes the cursor position),
///         and <see cref="Shutdown" /> on application exit (removes the watcher). Any code
///         can read the static query surface: <see cref="MouseX" /> / <see cref="MouseY" />,
///         <see cref="IsLeftButtonHeld" /> / <see cref="IsRightButtonHeld" />,
///         <see cref="IsKeyHeld" /> / <see cref="WasKeyPressed" /> / <see cref="WasKeyReleased" />,
///         <see cref="TextInput" />, and the chronologically-ordered <see cref="Events" /> stream.
///     </para>
/// </summary>
public static class InputBuffer
{
    //─────────────────────────────────────────────────────────────────────────────
    //  live state (held / tracked across frames, updated by the SDL watcher)
    //─────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<Keys> HeldKeys = [];
    private static int RawMouseX;
    private static int RawMouseY;
    private static float VirtualScale = 1f;

    //─────────────────────────────────────────────────────────────────────────────
    //  accumulation buffer (filled by the watcher between Update() calls)
    //─────────────────────────────────────────────────────────────────────────────

    //authoritative chronological event stream — keyboard, text, mouse button, and
    //mouse wheel events interleaved in true OS post order. the dispatcher walks it
    //each frame, and Update() scans it once to populate the query-style frame
    //snapshot (FrameKeyPresses/FrameKeyReleases/TextBuffer).
    private static readonly List<BufferedInputEvent> PendingEvents = [];

    //─────────────────────────────────────────────────────────────────────────────
    //  frame snapshot (frozen at the start of each Update())
    //─────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<Keys> FrameKeyPresses = [];
    private static readonly HashSet<Keys> FrameKeyReleases = [];
    private static BufferedInputEvent[] EventBuffer = [];
    private static int EventCount;
    private static char[] TextBuffer = [];
    private static int TextCount;
    private static bool WasInactive;

    //─────────────────────────────────────────────────────────────────────────────
    //  unmanaged callback lifetime
    //─────────────────────────────────────────────────────────────────────────────

    //the SDL event watcher delegate — must be held in a static field so the GC doesn't
    //collect it while SDL still has the function pointer. SdlEventWatchPtr is also kept
    //so Shutdown() can pass the exact same pointer to SDL_DelEventWatch.
    private static Sdl.EventWatchCallback? SdlEventWatch;
    private static nint SdlEventWatchPtr;
    private static bool Initialized;

    //─────────────────────────────────────────────────────────────────────────────
    //  public query surface
    //─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Current cursor X in virtual coordinates (640×480). Refreshed from
    ///     <c>SDL_GetMouseState</c> at the end of every <see cref="Update" /> after
    ///     <c>SDL_PumpEvents</c>, so it reflects the true end-of-frame cursor position
    ///     even when a macro fires its trailing move mid-frame.
    /// </summary>
    public static int MouseX => ToVirtual(RawMouseX);

    /// <summary>
    ///     Current cursor Y in virtual coordinates (640×480). See <see cref="MouseX" />.
    /// </summary>
    public static int MouseY => ToVirtual(RawMouseY);

    //single point where raw window pixels → virtual 640×480 coords. called from the
    //MouseX/MouseY getters and from the per-event coordinate capture in the SDL
    //watcher — must always use the same divisor so polled and event positions agree.
    private static int ToVirtual(int raw) => (int)(raw / VirtualScale);

    /// <summary>
    ///     True while the left mouse button is held down. Flipped per-event by the SDL
    ///     watcher — a click in another application never sets this to <c>true</c>,
    ///     unlike MonoGame's <c>Mouse.GetState().LeftButton</c> which reports global state.
    /// </summary>
    public static bool IsLeftButtonHeld { get; private set; }

    /// <summary>
    ///     True while the right mouse button is held down. Same per-window semantics as
    ///     <see cref="IsLeftButtonHeld" />.
    /// </summary>
    public static bool IsRightButtonHeld { get; private set; }

    /// <summary>
    ///     Returns true if the key is currently held down (event-tracked, not polled).
    /// </summary>
    public static bool IsKeyHeld(Keys key) => HeldKeys.Contains(key);

    /// <summary>
    ///     Returns true if the key had a rising edge (was pressed) during this frame. OS
    ///     key-repeat events are filtered out — only the initial press fires.
    /// </summary>
    public static bool WasKeyPressed(Keys key) => FrameKeyPresses.Contains(key);

    /// <summary>
    ///     Returns true if the key had a falling edge (was released) during this frame.
    /// </summary>
    public static bool WasKeyReleased(Keys key) => FrameKeyReleases.Contains(key);

    /// <summary>
    ///     Characters typed during this frame (from TextInput events). Includes OS
    ///     key-repeat characters.
    /// </summary>
    public static ReadOnlySpan<char> TextInput => TextBuffer.AsSpan(0, TextCount);

    /// <summary>
    ///     Chronologically ordered input events for this frame. Keyboard, text, mouse
    ///     button, and mouse wheel events interleaved in the exact OS post order captured
    ///     by the SDL watcher. Consumers walk this stream and dispatch each event in
    ///     sequence so that rapid macros which mix multiple input kinds fire in their
    ///     original order rather than being reordered to all-of-type-A-then-all-of-type-B.
    /// </summary>
    public static ReadOnlySpan<BufferedInputEvent> Events => EventBuffer.AsSpan(0, EventCount);

    /// <summary>
    ///     Sets the virtual-to-raw scale factor used by <see cref="MouseX" /> /
    ///     <see cref="MouseY" /> and by the per-click coordinate capture in the SDL watcher.
    ///     Called by <c>ChaosGame</c> whenever the window size changes.
    /// </summary>
    public static void SetVirtualScale(float scale) => VirtualScale = scale;

    //─────────────────────────────────────────────────────────────────────────────
    //  lifecycle
    //─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Installs the SDL event watcher. Call once at startup, before the first
    ///     <see cref="Update" />. Idempotent — repeat calls are no-ops.
    /// </summary>
    public static void Initialize()
    {
        if (Initialized)
            return;

        SdlEventWatch = OnSdlEvent;
        SdlEventWatchPtr = Marshal.GetFunctionPointerForDelegate(SdlEventWatch);
        Sdl.SDL_AddEventWatch(SdlEventWatchPtr, nint.Zero);
        Initialized = true;
    }

    /// <summary>
    ///     Removes the SDL event watcher. Call once at application exit.
    /// </summary>
    public static void Shutdown()
    {
        if (!Initialized)
            return;

        Sdl.SDL_DelEventWatch(SdlEventWatchPtr, nint.Zero);
        SdlEventWatchPtr = nint.Zero;
        SdlEventWatch = null;
        Initialized = false;
    }

    /// <summary>
    ///     Freezes all buffered input for this frame. Call once at the start of each
    ///     game update before any consumer reads the query surface or the event stream.
    /// </summary>
    /// <param name="isActive">Whether the game window currently has focus. When <c>false</c>, all buffered input is dropped.</param>
    public static void Update(bool isActive)
    {
        //drain the OS event queue → our watcher fires for every event posted since
        //MonoGame's start-of-tick pump, SDL's internal cursor state advances to its
        //real current position, and any wheel notches arriving mid-frame are captured.
        //without this a macro that fires move→click→move→click→move in one frame would
        //leave the cursor's last-known position stuck at the penultimate move until
        //the next pump.
        Sdl.SDL_PumpEvents();

        EventCount = 0;
        TextCount = 0;
        FrameKeyPresses.Clear();
        FrameKeyReleases.Clear();

        if (!isActive)
        {
            //window not focused — discard buffered input and report nothing
            PendingEvents.Clear();
            HeldKeys.Clear();
            IsLeftButtonHeld = false;
            IsRightButtonHeld = false;

            //keep the cursor position current so the custom cursor still draws in the
            //right spot while another window has focus.
            _ = Sdl.SDL_GetMouseState(out RawMouseX, out RawMouseY);
            WasInactive = true;

            return;
        }

        //suppress focus-click: drop any mouse button events that queued during
        //activation so the focus-click doesn't trigger a UI interaction, and clear
        //button held flags so a press that straddles activation doesn't leave the
        //dispatcher thinking a button is stuck down. keyboard events are preserved
        //so held hotkeys remain responsive.
        if (WasInactive)
        {
            WasInactive = false;
            PendingEvents.RemoveAll(static e => e.Kind == BufferedInputKind.MouseButton);
            IsLeftButtonHeld = false;
            IsRightButtonHeld = false;
        }

        //freeze the unified event stream and derive the query-style frame snapshot
        //in one pass. FrameKeyPresses/FrameKeyReleases/TextBuffer exist only so that
        //the O(1) accessors (WasKeyPressed etc.) don't have to scan Events each call.
        var pendingCount = PendingEvents.Count;

        if (pendingCount > 0)
        {
            if (EventBuffer.Length < pendingCount)
                EventBuffer = new BufferedInputEvent[Math.Max(pendingCount, 16)];

            //TextBuffer is sized to the total pending count which is always ≥ text
            //event count — conservative but only grows on spikes.
            if (TextBuffer.Length < pendingCount)
                TextBuffer = new char[Math.Max(pendingCount, 16)];

            for (var i = 0; i < pendingCount; i++)
            {
                var evt = PendingEvents[i];
                EventBuffer[EventCount++] = evt;

                switch (evt.Kind)
                {
                    case BufferedInputKind.KeyDown:
                        FrameKeyPresses.Add(evt.Key);

                        break;
                    case BufferedInputKind.KeyUp:
                        FrameKeyReleases.Add(evt.Key);

                        break;
                    case BufferedInputKind.TextInput:
                        TextBuffer[TextCount++] = evt.Character;

                        break;
                }
            }

            PendingEvents.Clear();
        }

        //read the latest cursor position from SDL's internal state. the pump at the
        //top of this method guarantees this reflects every OS event up to now, so
        //MouseX / MouseY always show the true end-state of the cursor this frame.
        _ = Sdl.SDL_GetMouseState(out RawMouseX, out RawMouseY);
    }

    //─────────────────────────────────────────────────────────────────────────────
    //  SDL event watcher
    //─────────────────────────────────────────────────────────────────────────────

    //all keyboard, text-input, mouse-button and mouse-wheel events funnel through
    //this single watcher callback. SDL fires it synchronously during SDL_PumpEvents
    //on the main thread, in the exact order the OS posted events — that shared
    //ordering is what lets the dispatcher later reconstruct per-event modifier state
    //and preserve the true temporal relationship between keyboard and mouse input.
    private static int OnSdlEvent(nint userdata, nint sdlEvent)
    {
        var eventType = (uint)Marshal.ReadInt32(sdlEvent);

        switch (eventType)
        {
            case Sdl.KEYDOWN:
            case Sdl.KEYUP:
                HandleKeyEvent(sdlEvent, eventType == Sdl.KEYDOWN);

                break;

            case Sdl.TEXTINPUT:
                HandleTextInputEvent(sdlEvent);

                break;

            case Sdl.MOUSEBUTTONDOWN:
            case Sdl.MOUSEBUTTONUP:
                HandleMouseButtonEvent(sdlEvent, eventType == Sdl.MOUSEBUTTONDOWN);

                break;

            case Sdl.MOUSEWHEEL:
                HandleMouseWheelEvent(sdlEvent);

                break;
        }

        return 1;
    }

    private static void HandleKeyEvent(nint sdlEvent, bool isDown)
    {
        var scancode = Marshal.ReadInt32(sdlEvent, Sdl.KEYBOARDEVENT_SCANCODE_OFFSET);
        var key = TranslateScancode(scancode);

        if (key == Keys.None)
            return;

        if (isDown)
        {
            HeldKeys.Add(key);
            PendingEvents.Add(BufferedInputEvent.ForKeyDown(key));
        } else
        {
            HeldKeys.Remove(key);
            PendingEvents.Add(BufferedInputEvent.ForKeyUp(key));
        }
    }

    private static void HandleTextInputEvent(nint sdlEvent)
    {
        //SDL delivers text as a UTF-8 null-terminated string inline in the event struct.
        //For ASCII input (the common case for Dark Ages) it's one byte per character,
        //but IME composition can deliver multi-character strings in a single event.
        var textPtr = sdlEvent + Sdl.TEXTINPUTEVENT_TEXT_OFFSET;
        var text = Marshal.PtrToStringUTF8(textPtr);

        if (string.IsNullOrEmpty(text))
            return;

        foreach (var ch in text)
            PendingEvents.Add(BufferedInputEvent.ForTextInput(ch));
    }

    private static void HandleMouseButtonEvent(nint sdlEvent, bool isPress)
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

        //flip held flags so IsLeftButtonHeld / IsRightButtonHeld report per-window
        //state. SDL only delivers button events for our window, so a click in another
        //application never sets these to true — which was the whole reason the
        //pre-refactor Mouse.GetState() path needed a mouseOutsideClient guard.
        if (mouseButton == MouseButton.Left)
            IsLeftButtonHeld = isPress;
        else if (mouseButton == MouseButton.Right)
            IsRightButtonHeld = isPress;

        //capture click position at the exact moment of the event in raw window pixels,
        //then translate to virtual coordinates via ToVirtual so polled and event
        //positions always agree. using per-event coordinates means that a click which
        //lands while the cursor is in flight (turbo-click during fast movement, or a
        //drag release) reports its true position rather than the frame-end cursor
        //position.
        var rawX = Marshal.ReadInt32(sdlEvent, Sdl.MOUSEBUTTONEVENT_X_OFFSET);
        var rawY = Marshal.ReadInt32(sdlEvent, Sdl.MOUSEBUTTONEVENT_Y_OFFSET);
        var virtualX = ToVirtual(rawX);
        var virtualY = ToVirtual(rawY);

        //capture modifier state at the exact moment of the event. SDL maintains its
        //own running modifier state; SDL_GetModState() reads it synchronously from
        //within the watcher callback on the same thread SDL updates it on, so the
        //value reflects what was held when the OS posted this button event.
        var mods = TranslateSdlMods(Sdl.SDL_GetModState());

        PendingEvents.Add(BufferedInputEvent.ForMouseButton(mouseButton, isPress, virtualX, virtualY, mods));
    }

    //promotes each SDL_MOUSEWHEEL event to a first-class BufferedInputEvent so that
    //a macro sequence click→scroll→click→scroll preserves relative ordering against
    //the click events. SDL reports y in notches (±1 per detent; positive = scroll up).
    //horizontal wheel and the precise* float fields (SDL 2.0.18+) are intentionally
    //ignored — consumers only want integer vertical notches.
    private static void HandleMouseWheelEvent(nint sdlEvent)
    {
        var y = Marshal.ReadInt32(sdlEvent, Sdl.MOUSEWHEELEVENT_Y_OFFSET);

        if (y == 0)
            return;

        //wheel events don't carry a click position in SDL 2.0.x — use the live
        //tracked cursor position as the wheel target. this is usually accurate because
        //cursor movement between consecutive OS events within the same pump is rare.
        var mods = TranslateSdlMods(Sdl.SDL_GetModState());
        var virtualX = ToVirtual(RawMouseX);
        var virtualY = ToVirtual(RawMouseY);

        PendingEvents.Add(BufferedInputEvent.ForMouseWheel(y, virtualX, virtualY, mods));
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
        4   => Keys.A,
        5   => Keys.B,
        6   => Keys.C,
        7   => Keys.D,
        8   => Keys.E,
        9   => Keys.F,
        10  => Keys.G,
        11  => Keys.H,
        12  => Keys.I,
        13  => Keys.J,
        14  => Keys.K,
        15  => Keys.L,
        16  => Keys.M,
        17  => Keys.N,
        18  => Keys.O,
        19  => Keys.P,
        20  => Keys.Q,
        21  => Keys.R,
        22  => Keys.S,
        23  => Keys.T,
        24  => Keys.U,
        25  => Keys.V,
        26  => Keys.W,
        27  => Keys.X,
        28  => Keys.Y,
        29  => Keys.Z,
        30  => Keys.D1,
        31  => Keys.D2,
        32  => Keys.D3,
        33  => Keys.D4,
        34  => Keys.D5,
        35  => Keys.D6,
        36  => Keys.D7,
        37  => Keys.D8,
        38  => Keys.D9,
        39  => Keys.D0,
        40  => Keys.Enter,
        41  => Keys.Escape,
        42  => Keys.Back,
        43  => Keys.Tab,
        44  => Keys.Space,
        45  => Keys.OemMinus,
        46  => Keys.OemPlus,
        47  => Keys.OemOpenBrackets,
        48  => Keys.OemCloseBrackets,
        49  => Keys.OemPipe,
        51  => Keys.OemSemicolon,
        52  => Keys.OemQuotes,
        53  => Keys.OemTilde,
        54  => Keys.OemComma,
        55  => Keys.OemPeriod,
        56  => Keys.OemQuestion,
        57  => Keys.CapsLock,
        58  => Keys.F1,
        59  => Keys.F2,
        60  => Keys.F3,
        61  => Keys.F4,
        62  => Keys.F5,
        63  => Keys.F6,
        64  => Keys.F7,
        65  => Keys.F8,
        66  => Keys.F9,
        67  => Keys.F10,
        68  => Keys.F11,
        69  => Keys.F12,
        70  => Keys.PrintScreen,
        71  => Keys.Scroll,
        72  => Keys.Pause,
        73  => Keys.Insert,
        74  => Keys.Home,
        75  => Keys.PageUp,
        76  => Keys.Delete,
        77  => Keys.End,
        78  => Keys.PageDown,
        79  => Keys.Right,
        80  => Keys.Left,
        81  => Keys.Down,
        82  => Keys.Up,
        83  => Keys.NumLock,
        84  => Keys.Divide,
        85  => Keys.Multiply,
        86  => Keys.Subtract,
        87  => Keys.Add,
        88  => Keys.Enter,
        89  => Keys.D1,
        90  => Keys.D2,
        91  => Keys.D3,
        92  => Keys.D4,
        93  => Keys.D5,
        94  => Keys.D6,
        95  => Keys.D7,
        96  => Keys.D8,
        97  => Keys.D9,
        98  => Keys.D0,
        99  => Keys.Decimal,
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

}

public enum BufferedInputKind : byte
{
    KeyDown,
    KeyUp,
    TextInput,
    MouseButton,
    MouseWheel
}

/// <summary>
///     A single captured input event — keyboard (KeyDown/KeyUp/TextInput), mouse button
///     press/release, or mouse wheel notch. <see cref="Kind" /> selects which fields are
///     meaningful; unused fields carry default values. Consumers should walk
///     <see cref="InputBuffer.Events" /> in order and switch on <see cref="Kind" /> to
///     dispatch appropriately.
/// </summary>
public readonly record struct BufferedInputEvent(
    BufferedInputKind Kind,
    Keys Key,
    char Character,
    MouseButton Button,
    bool IsPress,
    int X,
    int Y,
    int WheelDelta,
    KeyModifiers Modifiers)
{
    public static BufferedInputEvent ForKeyDown(Keys key)
        => new(
            BufferedInputKind.KeyDown,
            key,
            '\0',
            default,
            false,
            0,
            0,
            0,
            KeyModifiers.None);

    public static BufferedInputEvent ForKeyUp(Keys key)
        => new(
            BufferedInputKind.KeyUp,
            key,
            '\0',
            default,
            false,
            0,
            0,
            0,
            KeyModifiers.None);

    public static BufferedInputEvent ForTextInput(char character)
        => new(
            BufferedInputKind.TextInput,
            default,
            character,
            default,
            false,
            0,
            0,
            0,
            KeyModifiers.None);

    public static BufferedInputEvent ForMouseButton(
        MouseButton button,
        bool isPress,
        int x,
        int y,
        KeyModifiers modifiers)
        => new(
            BufferedInputKind.MouseButton,
            default,
            '\0',
            button,
            isPress,
            x,
            y,
            0,
            modifiers);

    public static BufferedInputEvent ForMouseWheel(int delta, int x, int y, KeyModifiers modifiers)
        => new(
            BufferedInputKind.MouseWheel,
            default,
            '\0',
            default,
            false,
            x,
            y,
            delta,
            modifiers);
}