using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock: every public type and member under <c>src/RoslynTools/Core/</c>
///     must carry an XML doc comment. Core is the public surface that command implementations
///     and external consumers depend on; undocumented public API is a regression.
///     Driven by rt's own <see cref="FindUndocumentedService"/> so the lock and the production
///     analyser cannot drift.
/// </summary>
public sealed class PublicCoreIsDocumented : IClassFixture<PublicCoreIsDocumented.Fixture>
{
    private readonly Fixture _fixture;

    public PublicCoreIsDocumented(Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Every public type and member under src/RoslynTools/Core must have XML docs")]
    public void public_core_has_no_undocumented_symbols()
    {
        var corePathFragment = Path.Combine("src", "RoslynTools", "Core") + Path.DirectorySeparatorChar;

        var violations = _fixture.UndocumentedPublicSymbols
            .Where(s => s.FilePath.Contains(corePathFragment, StringComparison.OrdinalIgnoreCase))
            .Select(s => $"  {s.Kind} {s.Fqn} ({s.FilePath}:{s.Line})")
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Every public symbol in src/RoslynTools/Core must carry an XML <summary>. "
            + "Use `rt refactor add-xml-doc` or write the docs by hand.\n\nUndocumented:\n"
            + string.Join("\n", violations));
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private Microsoft.CodeAnalysis.Workspace? _workspace;
        internal IReadOnlyList<UndocumentedSymbol> UndocumentedPublicSymbols { get; private set; } = [];

        public async Task InitializeAsync()
        {
            MsBuildFixture.Ensure();
            (_workspace, var solution) = await WorkspaceLoader.LoadAsync(FindSolutionPath());

            var accessibilityKinds = FindUndocumentedService.ParseAccessibilityLevels(["public"]);
            var service = new FindUndocumentedService();
            UndocumentedPublicSymbols = await service.AnalyzeAsync(
                solution, includeMembers: true, scope: null, accessibilityKinds);
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
}
