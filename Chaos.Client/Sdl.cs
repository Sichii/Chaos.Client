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
    public const uint MOUSEBUTTONDOWN = 0x401;
    public const uint MOUSEBUTTONUP = 0x402;
    public const byte BUTTON_LEFT = 1;
    public const byte BUTTON_RIGHT = 3;
    public const int MOUSEBUTTONEVENT_BUTTON_OFFSET = 16;

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
    public static partial int SDL_GetWindowDisplayIndex(nint window);

    [LibraryImport("SDL2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SDL_GetDisplayBounds(int displayIndex, out SdlRect rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct SdlRect
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }
}
