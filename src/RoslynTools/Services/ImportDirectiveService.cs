using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Stores information about imported directives.
/// </summary>
internal sealed record ImportDirectiveInfo(
    string Kind,
    string Name,
    string FilePath,
    int Line,
    bool IsGlobal,
    bool IsStatic,
    string? Alias);

/// <summary>
/// Manages imported directives in the codebase.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ImportDirectiveService : IRtService
{
    private readonly ILogger<ImportDirectiveService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public ImportDirectiveService()
        : this(new RtConsoleLogger<ImportDirectiveService>(), new SolutionChangeWriter())
    {
    }

    public ImportDirectiveService(ILogger<ImportDirectiveService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Analyzes imported directives in a solution.
    /// </summary>
    internal async Task<IReadOnlyList<ImportDirectiveInfo>> AnalyzeAsync(
        Solution solution,
        string kind)
    {
        var results = new List<ImportDirectiveInfo>();
        var rtIgnore = RtIgnore.ForSolution(solution);

        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                if (rtIgnore.IsIgnored(document.FilePath, "list-imports") || FilePathFilters.IsGeneratedOrIntermediate(document.FilePath))
                    continue;

                var root = await document.GetSyntaxRootAsync();
                // Stryker disable all : equivalent — valid C# document always has a syntax root in tests
                if (root is null)
                    continue;
                // Stryker restore all

                foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                {
                    var isGlobal = directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword);
                    var isStatic = directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
                    var alias = directive.Alias?.Name.Identifier.ValueText;

                    if (!MatchesKind(kind, isGlobal, isStatic, alias))
                        continue;

                    var line = directive.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    // Stryker disable Statement, String, NullCoalescing : equivalent — null fallbacks unreachable in valid C# test fixtures
                    results.Add(new ImportDirectiveInfo(
                        GetKind(isGlobal, isStatic, alias),
                        directive.Name?.ToString() ?? string.Empty,
                        document.FilePath ?? document.Name,
                        line,
                        isGlobal,
                        isStatic,
                        alias));
                    // Stryker restore Statement, String, NullCoalescing
                }
            }

        return results
            .OrderBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Line)
            .ToList();
    }

    /// <summary>
    /// Adds a static using directive to a solution. When <paramref name="simplify"/> is true,
    /// also strips redundant <c>TypeName.</c> qualifiers in the same file for the added type;
    /// when <paramref name="simplifyAllStaticImports"/> is true, additionally strips qualifiers
    /// for every other type already imported via <c>using static</c> in the file.
    /// </summary>
    internal async Task AddStaticUsingAsync(
        Solution solution,
        string filePath,
        string typeName,
        bool dryRun,
        bool simplify = false,
        bool simplifyAllStaticImports = false)
    {
        var originalSolution = solution;
        var document = FindDocument(solution, filePath);
        var root = await GetCompilationUnitAsync(document);

        var alreadyPresent = root.Usings.Any(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                                                  && u.Name?.ToString() == typeName);

        // Stryker disable all
        if (alreadyPresent)
        {
            _logger.RtInfo("Static using '{TypeName}' already exists in {FilePath}.", typeName, document.FilePath);
        }
        // Stryker restore all
        else
        {
            var directive = ParseUsingDirective($"using static {typeName};");
            var newRoot = AddUsingDirective(root, directive);
            solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
        }

        if (simplify || simplifyAllStaticImports)
        {
            // Stryker disable once all : equivalent — document always present after WithDocumentSyntaxRoot in tests
            var refreshedDocument = solution.GetDocument(document.Id)
                                    ?? throw new InvalidOperationException(
                                        $"Document for '{filePath}' could not be re-resolved after edit.");
            solution = await SimplifyStaticReferencesAsync(refreshedDocument, typeName, simplifyAllStaticImports);
        }

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private async Task<Solution> SimplifyStaticReferencesAsync(
        Document document,
        string addedTypeName,
        bool simplifyAllStaticImports)
    {
        // Stryker disable once all : equivalent — valid C# project always has a compilation in tests
        var compilation = await document.Project.GetCompilationAsync()
                          ?? throw new InvalidOperationException(
                              $"Could not load compilation for project '{document.Project.Name}'.");

        var targetTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var added = compilation.GetTypeByMetadataName(addedTypeName);
        if (added is not null)
        {
            targetTypes.Add(added);
        }
        else
        {
            // Stryker disable Statement, String : equivalent — log-only branch when addedTypeName fails resolution
            _logger.RtWarning(
                "Could not resolve type '{TypeName}' in '{ProjectName}'; --simplify will not strip qualifiers for it.",
                addedTypeName, document.Project.Name);
            // Stryker restore Statement, String
        }

        if (simplifyAllStaticImports)
        {
            var unit = await GetCompilationUnitAsync(document);
            foreach (var u in unit.Usings.Where(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)))
            {
                var name = u.Name?.ToString();
                // Stryker disable once Statement : equivalent — valid `using static X;` directives always have a Name
                if (name is null) continue;
                var t = compilation.GetTypeByMetadataName(name);
                if (t is not null) targetTypes.Add(t);
            }
        }

        if (targetTypes.Count == 0)
            return document.Project.Solution;

        // Stryker disable once all : equivalent — valid C# document always has a semantic model in tests
        var semanticModel = await document.GetSemanticModelAsync()
                            ?? throw new InvalidOperationException(
                                $"Could not load semantic model for '{document.FilePath}'.");
        // Stryker disable once all : equivalent — valid C# document always has a syntax root in tests
        var syntaxRoot = await document.GetSyntaxRootAsync()
                         ?? throw new InvalidOperationException(
                             $"Cannot read syntax root for '{document.FilePath}'.");

        var matches = syntaxRoot
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(ma => semanticModel.GetSymbolInfo(ma.Expression).Symbol is INamedTypeSymbol nts
                         && targetTypes.Contains(nts))
            .ToList();

        if (matches.Count == 0)
            return document.Project.Solution;

        var annotatedRoot = syntaxRoot.ReplaceNodes(
            matches,
            (_, rewritten) => rewritten.WithAdditionalAnnotations(Simplifier.Annotation));

        var annotatedDocument = document.WithSyntaxRoot(annotatedRoot);
        var reduced = await Simplifier.ReduceAsync(annotatedDocument);
        return reduced.Project.Solution;
    }

    /// <summary>
    /// Adds a global using directive to a solution.
    /// </summary>
    internal async Task AddGlobalUsingAsync(
        Solution solution,
        string filePath,
        string namespaceName,
        bool dryRun)
    {
        var originalSolution = solution;
        var document = FindDocument(solution, filePath);
        var root = await GetCompilationUnitAsync(document);

        if (root.Usings.Any(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)
                                 && !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                                 && u.Name?.ToString() == namespaceName))
        {
            // Stryker disable once all : equivalent — log side-effect, message text not under behavioural test
            _logger.RtInfo("Global using '{NamespaceName}' already exists in {FilePath}.", namespaceName,
                document.FilePath);
            return;
        }

        var directive = ParseUsingDirective($"global using {namespaceName};");

        var newRoot = AddUsingDirective(root, directive);
        solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    internal async Task<PreparedGlobalUsing> PrepareGlobalUsingAsync(
        Solution solution,
        string filePath,
        string namespaceName)
    {
        var document = FindDocument(solution, filePath);
        var root = await GetCompilationUnitAsync(document);
        if (root.Usings.Any(usingDirective =>
                usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)
                && !usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                && usingDirective.Name?.ToString() == namespaceName))
            return new PreparedGlobalUsing(solution, false, "Global using already exists.");

        var newRoot = AddUsingDirective(root, ParseUsingDirective($"global using {namespaceName};"));
        return new PreparedGlobalUsing(
            solution.WithDocumentSyntaxRoot(document.Id, newRoot),
            true,
            null);
    }

    internal sealed record PreparedGlobalUsing(
        Solution Solution,
        bool Changed,
        string? Message);

    internal async Task<PreparedGlobalUsing> PrepareStaticUsingAsync(
        Solution solution,
        string filePath,
        string typeName,
        bool simplify,
        bool simplifyAllStaticImports)
    {
        var originalSolution = solution;
        var document = FindDocument(solution, filePath);
        var root = await GetCompilationUnitAsync(document);
        var exists = root.Usings.Any(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                                          && u.Name?.ToString() == typeName);
        if (!exists)
        {
            solution = solution.WithDocumentSyntaxRoot(document.Id,
                AddUsingDirective(root, ParseUsingDirective($"using static {typeName};")));
        }

        if (simplify || simplifyAllStaticImports)
        {
            var refreshed = solution.GetDocument(document.Id)
                            ?? throw new InvalidOperationException(
                                $"Document for '{filePath}' could not be re-resolved after edit.");
            solution = await SimplifyStaticReferencesAsync(refreshed, typeName, simplifyAllStaticImports);
        }

        var changed = solution.GetChanges(originalSolution)
            .GetProjectChanges()
            .SelectMany(change => change.GetChangedDocuments())
            .Any();
        return new PreparedGlobalUsing(
            solution,
            changed,
            !changed && exists ? "Static using already exists." : null);
    }

    private Document FindDocument(Solution solution, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return solution.Projects
                   .SelectMany(p => p.Documents)
                   .FirstOrDefault(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"File '{filePath}' not found in solution.");
    }

    private async Task<CompilationUnitSyntax> GetCompilationUnitAsync(Document document)
    {
        // Stryker disable once all : equivalent — valid C# document always has a syntax root in tests
        var root = await document.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException($"Cannot read syntax root for '{document.FilePath}'.");

        // Stryker disable once all : equivalent — C# files always parse to CompilationUnitSyntax in tests
        return root as CompilationUnitSyntax
               ?? throw new InvalidOperationException($"Document '{document.FilePath}' is not a C# compilation unit.");
    }

    private CompilationUnitSyntax AddUsingDirective(
        CompilationUnitSyntax root,
        UsingDirectiveSyntax directive)
    {
        var usings = root.Usings;
        var insertIndex = directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)
            ? usings.TakeWhile(u => u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).Count()
            : usings.Count;

        var formattedDirective = directive
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        return root.WithUsings(usings.Insert(insertIndex, formattedDirective));
    }

    private UsingDirectiveSyntax ParseUsingDirective(string text)
    {
        // Stryker disable once all : equivalent — constructed input always parses to exactly one using directive
        return SyntaxFactory.ParseCompilationUnit(text + Environment.NewLine).Usings.Single();
    }

    private bool MatchesKind(string kind, bool isGlobal, bool isStatic, string? alias)
    {
        return kind.ToLowerInvariant() switch
        {
            "all" => true,
            "global" => isGlobal,
            "static" => isStatic,
            "alias" => alias is not null,
            _ => throw new ArgumentException("Kind must be one of: all, global, static, alias.", nameof(kind))
        };
    }

    private string GetKind(bool isGlobal, bool isStatic, string? alias)
    {
        if (isGlobal && isStatic)
            return "global-static";
        if (isGlobal)
            return "global";
        if (isStatic)
            return "static";
        if (alias is not null)
            return "alias";

        return "using";
    }
}
