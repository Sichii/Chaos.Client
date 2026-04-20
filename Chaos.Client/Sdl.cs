#region
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#endregion

namespace Chaos.Client;

/// <summary>
///     Centralized SDL2 P/Invoke declarations used by the client.
/// </summary>
internal static partial class Sdl
{
    //SDL event types we subscribe to via SDL_AddEventWatch
    public const uint KEYDOWN = 0x300;
    public const uint KEYUP = 0x301;
    public const uint TEXTINPUT = 0x303;
    public const uint MOUSEBUTTONDOWN = 0x401;
    public const uint MOUSEBUTTONUP = 0x402;
    public const uint MOUSEWHEEL = 0x403;

    public const byte BUTTON_LEFT = 1;
    public const byte BUTTON_RIGHT = 3;

    //SDL_MouseButtonEvent field offsets:
    //  type(4) + timestamp(4) + windowID(4) + which(4) = 16 → button(1)
    //  + state(1) + clicks(1) + padding(1) = 20 → x(4)
    //  + x(4) = 24 → y(4)
    public const int MOUSEBUTTONEVENT_BUTTON_OFFSET = 16;
    public const int MOUSEBUTTONEVENT_X_OFFSET = 20;
    public const int MOUSEBUTTONEVENT_Y_OFFSET = 24;

    //SDL_MouseWheelEvent field offsets:
    //  type(4) + timestamp(4) + windowID(4) + which(4) = 16 → x(4) at 16, y(4) at 20
    //SDL reports y as notches (typically ±1 per detent; positive = scroll up).
    public const int MOUSEWHEELEVENT_Y_OFFSET = 20;

    //SDL_KeyboardEvent field offsets:
    //  type(4) + timestamp(4) + windowID(4) = 12 → state(1) + repeat(1) + pad(2) = 16
    //  → SDL_Keysym { scancode(4) at 16, sym(4) at 20, mod(2) at 24, unused(4) }
    public const int KEYBOARDEVENT_REPEAT_OFFSET = 13;
    public const int KEYBOARDEVENT_SCANCODE_OFFSET = 16;

    //SDL_TextInputEvent field offsets:
    //  type(4) + timestamp(4) + windowID(4) = 12 → text[32] UTF-8 null-terminated
    public const int TEXTINPUTEVENT_TEXT_OFFSET = 12;

    //SDL_Keymod bitmask values returned by SDL_GetModState()
    public const uint KMOD_LSHIFT = 0x0001;
    public const uint KMOD_RSHIFT = 0x0002;
    public const uint KMOD_LCTRL = 0x0040;
    public const uint KMOD_RCTRL = 0x0080;
    public const uint KMOD_LALT = 0x0100;
    public const uint KMOD_RALT = 0x0200;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int EventWatchCallback(nint userdata, nint sdlEvent);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_AddEventWatch(nint filter, nint userdata);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_DelEventWatch(nint filter, nint userdata);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint SDL_GetModState();

    //forces SDL to drain the OS event queue and update its internal input state.
    //safe to call multiple times per frame — each OS event is only processed once.
    //InputBuffer.Update() calls this so that any events which arrived after
    //MonoGame's start-of-tick pump (e.g. a macro's trailing mouse move posted
    //mid-frame) fire our watcher and update the cursor position SDL_GetMouseState
    //returns.
    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_PumpEvents();

    //reads SDL's internal cursor state. x/y are window-relative to the focused
    //window (our game, when it has focus). the return value is a button bitmask
    //testable via SDL_BUTTON(n) — InputBuffer discards it because it tracks button
    //state per-event via the SDL watcher instead.
    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint SDL_GetMouseState(out int x, out int y);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SDL_GetWindowDisplayIndex(nint window);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SDL_GetDisplayBounds(int displayIndex, out SdlRect rect);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial uint SDL_GetWindowFlags(nint window);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_RestoreWindow(nint window);

    //SDL_WindowFlags bits reported by SDL_GetWindowFlags (consumed by ChaosGame for maximize detection)
    public const uint SDL_WINDOW_MAXIMIZED = 0x00000080;

    //SDL_Init subsystem flag for audio (consumed by SoundSystem during mixer bring-up)
    public const uint SDL_INIT_AUDIO = 0x00000010;

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SDL_InitSubSystem(uint flags);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_QuitSubSystem(uint flags);

    //SDL_GetError returns a UTF-8 C string owned by SDL; copy with Marshal.PtrToStringUTF8 on demand
    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SDL_GetError();

    //SDL_RWFromConstMem wraps a read-only byte buffer as an SDL_RWops stream for Mix_LoadWAV_RW. The buffer must
    //stay pinned for the lifetime of the RWops handle. We pass it straight into Mix_LoadWAV_RW with freesrc=1 so
    //SDL closes the RWops after loading — the caller's buffer can then be freed (SDL has copied the PCM data out).
    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint SDL_RWFromConstMem(nint mem, int size);

    /// <summary>Copies the current SDL error string for diagnostics — returns empty if SDL has no pending error.</summary>
    public static string GetError()
    {
        var ptr = SDL_GetError();

        return ptr == nint.Zero ? string.Empty : (Marshal.PtrToStringUTF8(ptr) ?? string.Empty);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SdlRect
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }
}
