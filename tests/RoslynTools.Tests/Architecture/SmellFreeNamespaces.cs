using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Loads the real solution and runs SmellAnalyzer once; individual facts filter on file path.
///     Specific subdirectories are held to zero-smell — a clean ratchet that prevents regression
///     without requiring the whole codebase to be smell-free immediately.
/// </summary>
public sealed class SmellsFixture : IAsyncLifetime
{
    private Workspace? _workspace;
    internal IReadOnlyList<SmellResult> Smells { get; private set; } = [];

    public async Task InitializeAsync()
    {
        MsBuildFixture.Ensure();
        (_workspace, var solution) = await WorkspaceLoader.LoadAsync(FindSolutionPath());
        Smells = await SmellAnalyzer.AnalyzeAsync(solution,
            maxClassLines: 300, maxMethodLines: 40, maxParams: 7, scope: null);
    }

    public Task DisposeAsync()
    {
        _workspace?.Dispose();
        return Task.CompletedTask;
    }

    private static string FindSolutionPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "RoslynTools.slnx")))
                return Path.Combine(dir.FullName, "RoslynTools.slnx");

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }
}

public sealed class SmellFreeNamespaces : IClassFixture<SmellsFixture>
{
    private readonly SmellsFixture _fixture;

    public SmellFreeNamespaces(SmellsFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Core/Decisions namespace must have zero code smells")]
    public void decisions_namespace_has_zero_code_smells()
    {
        var violations = _fixture.Smells
            .Where(s => s.FilePath?.Contains(
                Path.Combine("Core", "Decisions"), StringComparison.OrdinalIgnoreCase) == true)
            .Select(s => $"  {s.SmellType}: {s.TypeName}.{s.MemberName} ({s.FilePath}:{s.Line})")
            .ToList();

        Assert.True(violations.Count == 0,
            "Core/Decisions must remain smell-free:\n" + string.Join("\n", violations));
    }

    [Fact(DisplayName = "RoslynTools.Models namespace must have zero code smells")]
    public void models_namespace_has_zero_code_smells()
    {
        var violations = _fixture.Smells
            .Where(s => s.FilePath?.Contains(
                Path.Combine("src", "RoslynTools", "Models"), StringComparison.OrdinalIgnoreCase) == true)
            .Select(s => $"  {s.SmellType}: {s.TypeName}.{s.MemberName} ({s.FilePath}:{s.Line})")
            .ToList();

        Assert.True(violations.Count == 0,
            "RoslynTools.Models must remain smell-free:\n" + string.Join("\n", violations));
    }
}
