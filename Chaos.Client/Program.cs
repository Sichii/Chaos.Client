#region
using System.Runtime;
using System.Runtime.CompilerServices;
using Chaos.Client;
#endregion

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

RuntimeHelpers.RunClassConstructor(typeof(GlobalSettings).TypeHandle);

using var game = new ChaosGame();
game.Run();