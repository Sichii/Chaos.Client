#region
using System.Reflection;
using System.Runtime.CompilerServices;
using Chaos.Client.Data;
#endregion

namespace Chaos.Client;

/// <summary>
///     Forces dependent assemblies to load into the AppDomain so that reflection-based scanning (e.g. LoadImplementations)
///     can discover their types. Call <see cref="EnsureLoaded" /> once at startup before any scanning occurs.
/// </summary>
public static class AssemblyLoader
{
    private static readonly string[] RequiredAssemblies = ["Chaos.Networking"];

    public static void EnsureLoaded()
    {
        foreach (var name in RequiredAssemblies)
            Assembly.Load(name);

        RuntimeHelpers.RunClassConstructor(typeof(DataContext).TypeHandle);
    }
}