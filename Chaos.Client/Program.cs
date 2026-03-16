#region
using System.Text;
using Chaos.Client;
#endregion

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
AssemblyLoader.EnsureLoaded();

using var game = new ChaosGame();
game.Run();