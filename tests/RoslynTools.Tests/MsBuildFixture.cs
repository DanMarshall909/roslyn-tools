using System.Runtime.CompilerServices;

namespace RoslynTools.Tests;

/// <summary>
///     Registers MSBuild once per test run. Must be referenced via
///     [assembly: TestFramework] or ICollectionFixture to ensure it
///     runs before any test that loads Roslyn workspace types.
/// </summary>
public static class MsBuildFixture
{
    private static readonly object Lock = new();
    private static bool _registered;

    [ModuleInitializer]
    internal static void Initialize() => Ensure();

    public static void Ensure()
    {
        lock (Lock)
        {
            if (_registered) return;
            MsBuildBootstrap.Register();
            _registered = true;
        }
    }
}
