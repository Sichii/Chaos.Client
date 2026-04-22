#region
using System.Runtime;
using System.Runtime.CompilerServices;
using Chaos.Client;
#endregion

//must run before ChaosGame constructs its GraphicsDeviceManager (which initializes SDL and creates the window)
Sdl.SDL_SetHint(Sdl.SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, "1");

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

RuntimeHelpers.RunClassConstructor(typeof(GlobalSettings).TypeHandle);

using var game = new ChaosGame();
game.Run();