using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Prepares safe symbol deletions without rendering or writing changes.</summary>
internal sealed class SafeDeleteService(ISymbolResolutionService symbols) : IRtService
{
    private readonly ISymbolResolutionService _symbols = symbols;

    internal sealed record Preparation(
        Solution Solution,
        string Symbol,
        string Kind,
        string DeclarationPath,
        int IgnoredNameofReferenceCount,
        IReadOnlyList<SafeDeleteReferenceEvidence> References);

    internal async Task<Result<Preparation, RtError>> PrepareAsync(
        Solution solution,
        string symbol,
        string? kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = await ResolveSymbolAsync(solution, symbol, kind);
        return await resolved.Match(
            value => PrepareResolvedAsync(solution, value, cancellationToken),
            error => Task.FromResult(Result<Preparation, RtError>.Err(error)));
    }

    private static async Task<Result<Preparation, RtError>> PrepareResolvedAsync(
        Solution solution,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var referencedSymbols = await SymbolFinder.FindReferencesAsync(
            symbol,
            solution,
            cancellationToken: cancellationToken);
        var references = new List<SafeDeleteReferenceEvidence>();
        var ignoredNameofReferenceCount = 0;

        foreach (var referencedSymbol in referencedSymbols)
            foreach (var location in referencedSymbol.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var span = location.Location.SourceSpan;
                var filePath = location.Document.FilePath ?? string.Empty;

                var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault(reference =>
                    string.Equals(
                        reference.SyntaxTree.FilePath,
                        filePath,
                        StringComparison.OrdinalIgnoreCase)
                    && reference.Span.Contains(span));
                if (declaration is not null)
                    continue;

                var syntaxNode = location.Location.SourceTree is not null
                    ? (await location.Location.SourceTree.GetRootAsync(cancellationToken)).FindNode(span)
                    : null;

                if (syntaxNode is not null && IsInsideNameOf(syntaxNode))
                {
                    ignoredNameofReferenceCount++;
                    continue;
                }

                var text = await location.Document.GetTextAsync(cancellationToken);
                var line = text.Lines.GetLineFromPosition(span.Start);
                references.Add(new SafeDeleteReferenceEvidence(
                    filePath,
                    line.LineNumber + 1,
                    line.ToString().TrimStart()));
            }

        var orderedReferences = references
            .OrderBy(reference => reference.FilePath, StringComparer.Ordinal)
            .ThenBy(reference => reference.Line)
            .ThenBy(reference => reference.LineText, StringComparer.Ordinal)
            .ToArray();
        var syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        var declarationPath = syntaxReference?.SyntaxTree.FilePath ?? string.Empty;
        var symbolDisplay = symbol.ToDisplayString();
        var symbolKind = GetSymbolKind(symbol);

        if (orderedReferences.Length != 0)
            return Result<Preparation, RtError>.Ok(new Preparation(
                solution,
                symbolDisplay,
                symbolKind,
                declarationPath,
                ignoredNameofReferenceCount,
                orderedReferences));

        if (syntaxReference is null)
            return Result<Preparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"No declaring syntax found for '{symbol.Name}'.",
                new Dictionary<string, string> { ["symbol"] = symbolDisplay }));

        var declaringNode = await syntaxReference.GetSyntaxAsync(cancellationToken);
        var nodeToRemove = FindDeclarationNode(declaringNode);
        if (nodeToRemove is null)
            return Result<Preparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Cannot remove '{symbol.Name}': unsupported syntax node type ({declaringNode.GetType().Name}).",
                new Dictionary<string, string>
                {
                    ["symbol"] = symbolDisplay,
                    ["syntaxNodeType"] = declaringNode.GetType().Name
                }));

        var document = solution.Projects
            .SelectMany(project => project.Documents)
            .FirstOrDefault(candidate => candidate.FilePath is not null
                                         && string.Equals(
                                             candidate.FilePath,
                                             declaringNode.SyntaxTree.FilePath,
                                             StringComparison.OrdinalIgnoreCase));
        if (document is null)
            return Result<Preparation, RtError>.Err(RtError.NotFound(
                $"Cannot find document for '{symbol.Name}'.",
                new Dictionary<string, string>
                {
                    ["symbol"] = symbolDisplay,
                    ["declarationPath"] = declarationPath
                }));

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken);
        editor.RemoveNode(nodeToRemove);
        var changedSolution = editor.GetChangedDocument().Project.Solution;
        return Result<Preparation, RtError>.Ok(new Preparation(
            changedSolution,
            symbolDisplay,
            symbolKind,
            document.FilePath ?? document.Name,
            ignoredNameofReferenceCount,
            []));
    }

    private async Task<Result<ISymbol, RtError>> ResolveSymbolAsync(
        Solution solution,
        string symbol,
        string? kind)
    {
        if (symbol.Contains('.'))
            return await _symbols.TryFindSymbolByFqnAsync(solution, symbol, kind);

        if (kind is not null)
            return await _symbols.TryFindSymbolAsync(solution, symbol, kind);

        var typeResult = await _symbols.TryFindSymbolAsync(solution, symbol, "type");
        return await typeResult.Match(
            value => Task.FromResult(Result<ISymbol, RtError>.Ok(value)),
            error => error.Kind == RtErrorKind.NotFound
                ? _symbols.TryFindSymbolAsync(solution, symbol, null)
                : Task.FromResult(Result<ISymbol, RtError>.Err(error)));
    }

    private static string GetSymbolKind(ISymbol symbol)
    {
        return symbol.Kind switch
        {
            SymbolKind.NamedType => "type",
            SymbolKind.Method => "method",
            SymbolKind.Property => "property",
            SymbolKind.Field => "field",
            SymbolKind.Event => "event",
            _ => symbol.Kind.ToString().ToLowerInvariant()
        };
    }

    internal static SyntaxNode? FindDeclarationNode(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax => node,
            InterfaceDeclarationSyntax => node,
            StructDeclarationSyntax => node,
            RecordDeclarationSyntax => node,
            EnumDeclarationSyntax => node,
            DelegateDeclarationSyntax => node,
            MethodDeclarationSyntax => node,
            PropertyDeclarationSyntax => node,
            FieldDeclarationSyntax => node,
            EventDeclarationSyntax => node,
            _ => null
        };
    }

    internal static bool IsInsideNameOf(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax invocation
                && invocation.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == "nameof")
                return true;

            if (current is StatementSyntax or MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }

        return false;
    }
}
