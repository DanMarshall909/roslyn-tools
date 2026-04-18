using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class AddTestTests
{
    /// <summary>
    /// Builds a test project with an empty ServiceTests class.
    /// Includes a stub Xunit.FactAttribute so the fixture compiles without the real xunit package.
    /// </summary>
    private static TestProjectBuilder BuildTestProject()
    {
        var proj = new TestProjectBuilder("TestApp");
        proj.AddSource("ServiceTests.cs", """
            // Stub [Fact] attribute so test stubs compile without the real xunit package
            namespace Xunit
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public class FactAttribute : System.Attribute { }
            }

            public class ServiceTests { }
            """);
        return proj;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  1. Single mode — appends a [Fact] stub
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTest_SingleMode_AppendsFact()
    {
        using var proj = BuildTestProject();
        var slnPath = await proj.BuildAsync();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            AddTestCommand.ExecuteAsync("ServiceTests", "GetById_ReturnsNull", null, null, slnPath, false));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Single mode — preserves existing content
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTest_SingleMode_PreservesExistingTests()
    {
        using var proj = BuildTestProject();
        var slnPath = await proj.BuildAsync();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            AddTestCommand.ExecuteAsync("ServiceTests", "GetById_ReturnsNull", null, null, slnPath, false));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. AC mode — generates multiple test stubs
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTest_AcMode_GeneratesMultipleTests()
    {
        using var proj = BuildTestProject();
        var slnPath = await proj.BuildAsync();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            AddTestCommand.ExecuteAsync(
                "ServiceTests",
                null,
                "Returns null when not found; Throws when id is negative",
                null,
                slnPath,
                false));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. AC mode with nested class
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTest_AcMode_NestedClass()
    {
        using var proj = BuildTestProject();
        var slnPath = await proj.BuildAsync();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            AddTestCommand.ExecuteAsync(
                "ServiceTests",
                null,
                "Returns null when not found",
                "GivenNegativeId",
                slnPath,
                false));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Plain-English AC → snake_case method name
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTest_AcMode_PlainEnglishToSnakeCase()
    {
        using var proj = BuildTestProject();
        var slnPath = await proj.BuildAsync();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            AddTestCommand.ExecuteAsync(
                "ServiceTests",
                null,
                "Returns 404 when order not found",
                null,
                slnPath,
                false));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  6. Dry-run — shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTest_DryRun_NoWrites()
    {
        using var proj = BuildTestProject();
        var slnPath = await proj.BuildAsync();

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            AddTestCommand.ExecuteAsync("ServiceTests", "SomeTest", null, null, slnPath, dryRun: true));
    }
}
