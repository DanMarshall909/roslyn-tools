using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record RemovedUsingDirective(
    string File,
    int Line,
    string Directive);

internal sealed record RemoveUnusedUsingsPreparation(
    Solution Solution,
    IReadOnlyList<RemovedUsingDirective> RemovedUsings);

/// <summary>Prepares semantic removal of compiler-confirmed unnecessary using directives.</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service participates in dependency injection and operation composition.")]
internal sealed class RemoveUnusedUsingsService : IRtService
{
    private const string UnnecessaryUsingDiagnosticId = "CS8019";
    private const string CommandName = "remove-unused-usings";

    internal async Task<RemoveUnusedUsingsPreparation> PrepareAsync(
        Solution solution,
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        var documents = SelectDocuments(solution, filePath);
        var removedUsings = new List<RemovedUsingDirective>();
        var changedSolution = solution;

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
                                ?? throw new InvalidOperationException(
                                    $"Could not load a semantic model for '{document.FilePath ?? document.Name}'.");
            var root = await document.GetSyntaxRootAsync(cancellationToken)
                       ?? throw new InvalidOperationException(
                           $"Could not read syntax for '{document.FilePath ?? document.Name}'.");
            var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
                .Where(diagnostic => diagnostic.Id == UnnecessaryUsingDiagnosticId)
                .ToArray();
            if (diagnostics.Length == 0)
                continue;

            var unnecessaryUsings = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Where(usingDirective => diagnostics.Any(diagnostic =>
                    diagnostic.Location.SourceTree == usingDirective.SyntaxTree
                    && diagnostic.Location.SourceSpan.IntersectsWith(usingDirective.Span)))
                .ToArray();
            if (unnecessaryUsings.Length == 0)
                continue;

            var displayPath = document.FilePath ?? document.Name;
            removedUsings.AddRange(unnecessaryUsings.Select(usingDirective =>
                new RemovedUsingDirective(
                    displayPath,
                    usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    usingDirective.ToString())));

            var changedRoot = root.RemoveNodes(
                unnecessaryUsings,
                SyntaxRemoveOptions.KeepExteriorTrivia);
            if (changedRoot is null)
                throw new InvalidOperationException($"Could not update '{displayPath}'.");

            changedSolution = changedSolution.WithDocumentSyntaxRoot(document.Id, changedRoot);
        }

        return new RemoveUnusedUsingsPreparation(
            changedSolution,
            removedUsings
                .OrderBy(entry => entry.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Line)
                .ToArray());
    }

    private static Document[] SelectDocuments(Solution solution, string? filePath)
    {
        var allDocuments = solution.Projects
            .SelectMany(project => project.Documents)
            .ToArray();
        var rtIgnore = RtIgnore.ForSolution(solution);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var fullPath = Path.GetFullPath(filePath);
            var document = allDocuments.FirstOrDefault(candidate =>
                candidate.FilePath is not null
                && string.Equals(
                    Path.GetFullPath(candidate.FilePath),
                    fullPath,
                    PathComparison));
            if (document is null)
                throw new InvalidOperationException($"File '{filePath}' was not found in the solution.");
            if (FilePathFilters.IsGeneratedOrIntermediate(document.FilePath))
                throw new ArgumentException(
                    $"Generated or intermediate file '{filePath}' cannot be changed.",
                    nameof(filePath));

            return rtIgnore.IsIgnored(document.FilePath, CommandName)
                ? []
                : [document];
        }

        return allDocuments
            .Where(document => !FilePathFilters.IsGeneratedOrIntermediate(document.FilePath))
            .Where(document => !rtIgnore.IsIgnored(document.FilePath, CommandName))
            .OrderBy(document => document.FilePath ?? document.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
