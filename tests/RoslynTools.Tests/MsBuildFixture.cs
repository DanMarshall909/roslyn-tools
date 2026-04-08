namespace RoslynTools.Tests;

/// <summary>
/// Registers MSBuild once per test run. Must be referenced via
/// [assembly: TestFramework] or ICollectionFixture to ensure it
/// runs before any test that loads Roslyn workspace types.
/// </summary>
public sealed class MsBuildFixture
{
    private static readonly object Lock = new();
    private static bool _registered;

    public MsBuildFixture()
    {
        lock (Lock)
        {
            if (!_registered)
            {
                MsBuildBootstrap.Register();
                _registered = true;
            }
        }
    }
}

[CollectionDefinition("MSBuild")]
public class MsBuildCollection : ICollectionFixture<MsBuildFixture>
{
}
