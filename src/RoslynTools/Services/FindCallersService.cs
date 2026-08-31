using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

/// <summary>
/// Provides methods for finding callers of a given type symbol.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class FindCallersService : IRtService
{
    private readonly ILogger<FindCallersService> _logger;
    private readonly ISymbolCandidateService _symbols;

    public FindCallersService()
        : this(new RtConsoleLogger<FindCallersService>(), new SymbolCandidateService())
    {
    }

    public FindCallersService(
        ILogger<FindCallersService> logger,
        ISymbolCandidateService symbols)
    {
        _symbols = symbols;

        _logger = logger;
    }

    /// <summary>
    /// Async method to find callers based on a string and a boolean flag.
    /// </summary>
    internal async Task<Result<FindCallersResult, RtError>> FindAsync(
        Solution sln,
        string symbol,
        bool includeTests,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _symbols.ResolveAsync(sln, symbol, "method", cancellationToken);
        if (resolved.IsErr)
            return resolved.Map(_ => default(FindCallersResult)!);

        var methodSymbol = resolved.Match(found => found, _ => default!);

        _logger.RtInfo("Finding callers of '{Method}'...", methodSymbol.ToDisplayString());

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(
            methodSymbol,
            sln,
            cancellationToken: cancellationToken);
        var callers = new List<CallerResult>();

        foreach (var referencedSymbol in referencedSymbols)
            foreach (var loc in referencedSymbol.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var doc = loc.Document;

                if (!includeTests && IsTestProject(doc.Project.Name))
                    continue;

                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                    continue;

                var node = root.FindNode(loc.Location.SourceSpan);

                var invocation = node.AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault();

                if (invocation is null || IsNameofExpression(invocation))
                    continue;

                var lineSpan = loc.Location.GetLineSpan();
                var line = lineSpan.StartLinePosition.Line + 1;

                var containingMethod = invocation.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()
                    ?.Identifier.Text ?? "<unknown>";

                var text = await doc.GetTextAsync(cancellationToken);
                var sourceLine = text.Lines[line - 1].ToString().TrimStart();

                callers.Add(new CallerResult
                {
                    FilePath = doc.FilePath ?? string.Empty,
                    Line = line,
                    ContainingMethod = containingMethod,
                    SourceLine = sourceLine
                });
            }

        _logger.RtInfo("Found {Count} caller(s) of '{Name}'.", callers.Count, methodSymbol.Name);

        return Result<FindCallersResult, RtError>.Ok(new FindCallersResult
        {
            SymbolName = methodSymbol.Name,
            Callers = callers
        });
    }

    /// <summary>
    /// Checks if an invocation expression represents a nameof expression.
    /// </summary>
    internal bool IsNameofExpression(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is IdentifierNameSyntax id &&
               id.Identifier.ValueText == "nameof";
    }

    /// <summary>
    /// Determines if a given project is a test project.
    /// </summary>
    internal bool IsTestProject(string projectName)
    {
        return projectName.Contains("Test", StringComparison.OrdinalIgnoreCase);
    }
}
