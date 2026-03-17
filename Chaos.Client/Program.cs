#region
using System.Runtime.CompilerServices;
using Chaos.Client;
#endregion

RuntimeHelpers.RunClassConstructor(typeof(GlobalSettings).TypeHandle);

using var game = new ChaosGame();
game.Run();