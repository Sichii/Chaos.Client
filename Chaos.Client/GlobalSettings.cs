#region
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Chaos.Client.Data;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client;

/// <summary>
///     Forces dependent assemblies to load into the AppDomain so that reflection-based scanning (e.g. LoadImplementations)
///     can discover their types. Call <see cref="InitializeOthers" /> once at startup before any scanning occurs.
/// </summary>
public static class GlobalSettings
{
    private static readonly string[] RequiredAssemblies = ["Chaos.Networking"];
    public static readonly SamplerState Sampler = SamplerState.PointClamp;
    private static ushort ClientVersion => 741;
    public static string DataPath => @"C:\Users\Despe\Desktop\Unora\Unora"; //@"C:\Users\Despe\Desktop\Dark Ages";
    public static string LobbyHost => "127.0.0.1"; //"da0.kru.com";
    public static int LobbyPort => 4200; //2610;
    public static ushort ServerVersion => 741;

    static GlobalSettings() => InitializeOthers();

    private static void InitializeOthers()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DataContext.Initialize(
            ClientVersion,
            DataPath,
            LobbyHost,
            LobbyPort);

        foreach (var name in RequiredAssemblies)
            Assembly.Load(name);

        RuntimeHelpers.RunClassConstructor(typeof(DataContext).TypeHandle);
    }
}