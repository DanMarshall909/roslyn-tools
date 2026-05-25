using System.Security.Cryptography;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Architectural lock: every public type and member under <c>src/RoslynTools/Core/</c>
///     must carry an XML doc comment. Core is the public surface that command implementations
///     and external consumers depend on; undocumented public API is a regression.
///     Public method implementation changes must also be reviewed against those docs by updating
///     <c>docs/public-core-method-review-index.tsv</c>.
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

    [Fact(DisplayName = "Every public Core method implementation change must be doc-reviewed")]
    public void public_core_method_implementation_changes_are_reviewed()
    {
        var expectedEntries = PublicMethodReviewIndex.Read(FindReviewIndexPath());
        var currentEntries = _fixture.PublicCoreMethodReviewEntries;
        var currentBySymbol = currentEntries.ToDictionary(e => e.SymbolId, StringComparer.Ordinal);
        var expectedBySymbol = expectedEntries.ToDictionary(e => e.SymbolId, StringComparer.Ordinal);

        var missing = currentEntries
            .Where(e => !expectedBySymbol.ContainsKey(e.SymbolId))
            .OrderBy(e => e.SymbolId, StringComparer.Ordinal)
            .ToList();

        var stale = currentEntries
            .Where(e => expectedBySymbol.TryGetValue(e.SymbolId, out var expected) && expected.Hash != e.Hash)
            .OrderBy(e => e.SymbolId, StringComparer.Ordinal)
            .ToList();

        var unreviewed = expectedEntries
            .Where(e => currentBySymbol.ContainsKey(e.SymbolId) && !e.Reviewed)
            .OrderBy(e => e.SymbolId, StringComparer.Ordinal)
            .ToList();

        var removed = expectedEntries
            .Where(e => !currentBySymbol.ContainsKey(e.SymbolId))
            .OrderBy(e => e.SymbolId, StringComparer.Ordinal)
            .ToList();

        var failures = new List<string>();
        failures.AddRange(missing.Select(e => "Missing review entry:\n  " + e.ToIndexLine()));
        failures.AddRange(stale.Select(e =>
            "Changed implementation; review the XML docs and replace the existing index line with:\n  "
            + e.ToIndexLine()));
        failures.AddRange(unreviewed.Select(e =>
            "Review flag is not set; after reviewing the public XML docs, set reviewed=true for:\n  "
            + e.ToIndexLine()));
        failures.AddRange(removed.Select(e =>
            "Stale review entry for a removed or non-public method; remove this index line:\n  "
            + e.ToIndexLine()));

        Assert.True(
            failures.Count == 0,
            "Public method implementation changes under src/RoslynTools/Core must be reviewed against "
            + "their XML documentation. Update docs/public-core-method-review-index.tsv only after "
            + "checking that each affected method's public docs still describe its behaviour, side effects, "
            + "dry-run semantics, errors, and return value.\n\n"
            + string.Join("\n\n", failures));
    }

    private static string FindReviewIndexPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "public-core-method-review-index.tsv");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "Could not locate docs/public-core-method-review-index.tsv. Create it with lines in this format: "
            + "sha256<TAB>reviewed=true<TAB>symbol-id<TAB>repo-relative-path.");
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private Microsoft.CodeAnalysis.Workspace? _workspace;
        internal IReadOnlyList<UndocumentedSymbol> UndocumentedPublicSymbols { get; private set; } = [];
        internal IReadOnlyList<PublicMethodReviewEntry> PublicCoreMethodReviewEntries { get; private set; } = [];

        public async Task InitializeAsync()
        {
            MsBuildFixture.Ensure();
            var solutionPath = FindSolutionPath();
            (_workspace, var solution) = await WorkspaceLoader.LoadAsync(solutionPath);

            var accessibilityKinds = FindUndocumentedService.ParseAccessibilityLevels(["public"]);
            var service = new FindUndocumentedService();
            UndocumentedPublicSymbols = await service.AnalyzeAsync(
                solution, includeMembers: true, scope: null, accessibilityKinds);

            PublicCoreMethodReviewEntries = await PublicMethodReviewIndex.BuildCurrentAsync(
                solution,
                Path.GetDirectoryName(solutionPath)!);
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

    internal sealed record PublicMethodReviewEntry(
        string Hash,
        bool Reviewed,
        string SymbolId,
        string RepoRelativePath,
        int Line)
    {
        public string ToIndexLine()
        {
            return string.Join(
                '\t',
                Hash,
                "reviewed=true",
                SymbolId,
                $"{RepoRelativePath}:{Line}");
        }
    }

    private static class PublicMethodReviewIndex
    {
        public static List<PublicMethodReviewEntry> Read(string path)
        {
            return File.ReadAllLines(path)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(x => !string.IsNullOrWhiteSpace(x.Line) && !x.Line.StartsWith('#'))
                .Select(Parse)
                .ToList();
        }

        public static async Task<IReadOnlyList<PublicMethodReviewEntry>> BuildCurrentAsync(
            Solution solution,
            string repoRoot)
        {
            var entries = new List<PublicMethodReviewEntry>();
            var corePathFragment = Path.Combine("src", "RoslynTools", "Core") + Path.DirectorySeparatorChar;

            foreach (var project in solution.Projects)
                foreach (var document in project.Documents)
                {
                    if (document.FilePath is null
                        || !document.FilePath.Contains(corePathFragment, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var root = await document.GetSyntaxRootAsync();
                    var model = await document.GetSemanticModelAsync();
                    if (root is null || model is null)
                        continue;

                    foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
                    {
                        if (!HasImplementation(method))
                            continue;

                        if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol
                            || symbol.DeclaredAccessibility != Accessibility.Public
                            || symbol.IsImplicitlyDeclared)
                        {
                            continue;
                        }

                        var symbolId = DocumentationCommentId.CreateDeclarationId(symbol);
                        if (symbolId is null)
                            continue;

                        var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var repoRelativePath = Path.GetRelativePath(repoRoot, document.FilePath)
                            .Replace(Path.DirectorySeparatorChar, '/');

                        entries.Add(new PublicMethodReviewEntry(
                            HashMethod(method),
                            Reviewed: true,
                            symbolId,
                            repoRelativePath,
                            line));
                    }
                }

            return entries
                .OrderBy(e => e.SymbolId, StringComparer.Ordinal)
                .ToList();
        }

        private static PublicMethodReviewEntry Parse((string Line, int Number) item)
        {
            var parts = item.Line.Split('\t');
            if (parts.Length != 4)
                throw new InvalidOperationException(
                    $"Invalid docs/public-core-method-review-index.tsv line {item.Number}: expected 4 tab-separated fields.");

            var locationParts = parts[3].Split(':');
            if (locationParts.Length < 2 || !int.TryParse(locationParts[^1], out var line))
                throw new InvalidOperationException(
                    $"Invalid docs/public-core-method-review-index.tsv line {item.Number}: final field must end with :<line>.");

            return new PublicMethodReviewEntry(
                parts[0],
                parts[1].Equals("reviewed=true", StringComparison.Ordinal),
                parts[2],
                string.Join(':', locationParts[..^1]),
                line);
        }

        private static bool HasImplementation(BaseMethodDeclarationSyntax method)
        {
            return method.Body is not null || method.ExpressionBody is not null;
        }

        private static string HashMethod(BaseMethodDeclarationSyntax method)
        {
            var reviewText = method
                .WithLeadingTrivia()
                .WithTrailingTrivia()
                .NormalizeWhitespace()
                .ToFullString();

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reviewText)))
                .ToLowerInvariant();
        }
    }
}
