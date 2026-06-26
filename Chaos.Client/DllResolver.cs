#region
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#endregion

namespace Chaos.Client;

// Handle native resolution of various cross-platform libraries needed (like sdl / sdl_mixer)
internal static class DllResolver
{
    private static string RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;
    private static string Platform => RuntimeIdentifier.Split('-')[0];

    private static readonly Dictionary<string, List<string>> MacLibraries = new()
    {
        {
            "SDL2_mixer", [
                              "libSDL2_mixer-2.0.0.dylib",
                              "libSDL2_mixer.dylib"
                          ]
        },
        {
            "SDL2", [
                        "libSDL2-2.0.0.dylib",
                        "libSDL2.dylib"
                    ]
        }
    };

    private static readonly Dictionary<string, List<string>> LinuxLibraries = new()
    {
        {
            "SDL2_mixer", [
                              "libSDL2_mixer-2.0.so.0",
                              "libSDL2_mixer.so.0",
                              "libSDL2_mixer.so"
                          ]
        },
        {
            "SDL2", [
                        "libSDL2-2.0.so.0",
                        "libSDL2.so.0",
                        "libSDL2.so"
                    ]
        }
    };

    private static readonly Dictionary<string, List<string>> WinLibraries = new()
    {
        {
            "SDL2_mixer", ["SDL2_mixer.dll"]
        },
        {
            "SDL2", ["SDL2.dll"]
        }
    };

    private static Dictionary<string, List<string>> Candidates
        => OperatingSystem.IsMacOS()
            ? MacLibraries
            : OperatingSystem.IsLinux()
                ? LinuxLibraries
                : WinLibraries;

    // Run before anything else happens, which guarantees the resolution is done before anything tries
    // to pinvoke it
    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(DllResolver).Assembly, ImportResolver);

        // MonoGame loads SDL2 itself via its own FuncLoader, which uses raw dlopen/LoadLibrary. On
        // Linux/macOS dlopen does NOT search this app's runtimes/{rid}/native folder (only .NET's
        // native resolution does, and the resolver above only covers THIS assembly's P/Invokes, not
        // MonoGame's). So a portable (non-RID) publish can't locate libSDL2 and crashes on startup.
        // Pre-load it by full path here: once resident, MonoGame's later dlopen-by-soname returns the
        // already-loaded handle without touching the filesystem. No-op on Windows (DLL search dir
        // already covers runtimes/win-x64/native); harmless if the system has its own libSDL2.
        if (!OperatingSystem.IsWindows() && Candidates.TryGetValue("SDL2", out var sdl))
            TryLoadLibrary(sdl, out _);
    }

    public static nint ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        nint handle;

        if (Candidates.TryGetValue(libraryName, out var libs))
            if (TryLoadLibrary(libs, out handle))
                return handle;

        // Fall back to library name resolution as a last chance attempt
        NativeLibrary.TryLoad(libraryName, out handle);

        return handle;
    }

    private static bool TryLoadLibrary(List<string> libraryNames, out nint handle)
    {
        handle = nint.Zero;

        foreach (var libraryName in libraryNames)
        {
            // Try arch specific first, then native
            var archSpecific = Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                RuntimeIdentifier,
                "native",
                libraryName);

            var independent = Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                Platform,
                "native",
                libraryName);

            if (NativeLibrary.TryLoad(archSpecific, out handle) || NativeLibrary.TryLoad(independent, out handle))
                return true;
        }

        return false;
    }
}