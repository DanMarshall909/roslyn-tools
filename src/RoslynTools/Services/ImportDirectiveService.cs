using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ImportDirectiveInfo(
    string Kind,
    string Name,
    string FilePath,
    int Line,
    bool IsGlobal,
    bool IsStatic,
    string? Alias);

internal static class ImportDirectiveService
{
    internal static async Task<IReadOnlyList<ImportDirectiveInfo>> AnalyzeAsync(
        Solution solution,
        string kind)
    {
        var results = new List<ImportDirectiveInfo>();
        var rtIgnore = RtIgnore.ForSolution(solution);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (rtIgnore.IsIgnored(document.FilePath, "list-imports") || IsGeneratedOrIntermediate(document.FilePath))
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
                {
                    var isGlobal = directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword);
                    var isStatic = directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
                    var alias = directive.Alias?.Name.Identifier.ValueText;

                    if (!MatchesKind(kind, isGlobal, isStatic, alias))
                        continue;

                    var line = directive.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    results.Add(new ImportDirectiveInfo(
                        Kind: GetKind(isGlobal, isStatic, alias),
                        Name: directive.Name?.ToString() ?? string.Empty,
                        FilePath: document.FilePath ?? document.Name,
                        Line: line,
                        IsGlobal: isGlobal,
                        IsStatic: isStatic,
                        Alias: alias));
                }
            }
        }

        return results
            .OrderBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Line)
            .ToList();
    }

    internal static async Task AddStaticUsingAsync(
        Solution solution,
        string filePath,
        string typeName,
        bool dryRun)
    {
        var originalSolution = solution;
        var document = FindDocument(solution, filePath);
        var root = await GetCompilationUnitAsync(document);

        if (root.Usings.Any(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
            && u.Name?.ToString() == typeName))
        {
            RtLog.Info("Static using '{TypeName}' already exists in {FilePath}.", typeName, document.FilePath);
            return;
        }

        var directive = ParseUsingDirective($"using static {typeName};");

        var newRoot = AddUsingDirective(root, directive);
        solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    internal static async Task AddGlobalUsingAsync(
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
            RtLog.Info("Global using '{NamespaceName}' already exists in {FilePath}.", namespaceName, document.FilePath);
            return;
        }

        var directive = ParseUsingDirective($"global using {namespaceName};");

        var newRoot = AddUsingDirective(root, directive);
        solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private static Document FindDocument(Solution solution, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"File '{filePath}' not found in solution.");
    }

    private static async Task<CompilationUnitSyntax> GetCompilationUnitAsync(Document document)
    {
        var root = await document.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException($"Cannot read syntax root for '{document.FilePath}'.");

        return root as CompilationUnitSyntax
            ?? throw new InvalidOperationException($"Document '{document.FilePath}' is not a C# compilation unit.");
    }

    private static CompilationUnitSyntax AddUsingDirective(
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

    private static UsingDirectiveSyntax ParseUsingDirective(string text) =>
        SyntaxFactory.ParseCompilationUnit(text + Environment.NewLine).Usings.Single();

    private static bool MatchesKind(string kind, bool isGlobal, bool isStatic, string? alias) =>
        kind.ToLowerInvariant() switch
        {
            "all" => true,
            "global" => isGlobal,
            "static" => isStatic,
            "alias" => alias is not null,
            _ => throw new ArgumentException("Kind must be one of: all, global, static, alias.", nameof(kind)),
        };

    private static string GetKind(bool isGlobal, bool isStatic, string? alias)
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

    private static bool IsGeneratedOrIntermediate(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }
}
