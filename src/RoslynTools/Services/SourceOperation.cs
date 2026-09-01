using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for retrieving a symbol's source declaration.</summary>
internal sealed record SourceRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null,
    [property: CommandPropertyDescription(
        "Include one-hop source declarations for each listed related symbol")]
    bool IncludeRelated = false,
    [property: CommandPropertyDescription("Maximum related symbols to list (1-100)")]
    int RelatedLimit = 10);

/// <summary>One source declaration returned for a resolved symbol.</summary>
internal sealed record SourceDeclaration(
    [property: JsonPropertyName("fqn")] string FullyQualifiedName,
    [property: JsonPropertyName("file")] string FilePath,
    [property: JsonPropertyName("lineFrom")] int LineFrom,
    [property: JsonPropertyName("lineTo")] int LineTo,
    [property: JsonPropertyName("source")] string Text);

/// <summary>A symbol referenced by the requested declaration or body.</summary>
internal sealed record RelatedSourceSymbol(
    [property: JsonPropertyName("fqn")] string FullyQualifiedName,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("declarations")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceDeclaration>? Declarations);

/// <summary>The requested symbol content followed by its bounded related-symbol list.</summary>
internal sealed record SourceResult(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("declarations")] IReadOnlyList<SourceDeclaration> Declarations,
    [property: JsonPropertyName("relatedSymbolsTruncated")] bool RelatedSymbolsTruncated,
    [property: JsonPropertyName("relatedSymbols")] IReadOnlyList<RelatedSourceSymbol> RelatedSymbols);

/// <summary>Shared source operation used by host adapters.</summary>
internal sealed class SourceOperation
    : CommandApplicationPort<SourceRequest, SourceResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly InspectService _inspect;
    private readonly ISymbolCandidateService _symbols;

    public SourceOperation(
        IWorkspaceSessionFactory sessionFactory,
        InspectService inspect,
        ISymbolCandidateService symbols)
    {
        _sessionFactory = sessionFactory;
        _inspect = inspect;
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
    }

    public override ApplicationCommandDefinition Definition => SourceApplicationDefinition.Definition;

    public override async Task<Result<SourceResult, RtError>> ExecuteAsync(
        SourceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return Result<SourceResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A symbol identity or name is required."));
        if (request.RelatedLimit <= 0 || request.RelatedLimit > Definition.Product.Limits.MaximumItems)
            return Result<SourceResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Related limit must be between 1 and {Definition.Product.Limits.MaximumItems}."));

        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await _symbols.ResolveAsync(
                solution,
                request.Symbol,
                request.Kind,
                cancellationToken);
            if (resolved.IsErr)
                return resolved.Map(_ => default(SourceResult)!);

            var symbol = resolved.Match(value => value, _ => default!);
            cancellationToken.ThrowIfCancellationRequested();
            var declarations = NormalizeDeclarations(await _inspect.GetSourceAsync(symbol));
            var related = await _inspect.GetRelatedSymbolsAsync(
                solution,
                symbol,
                request.RelatedLimit + 1,
                cancellationToken);
            var relatedSymbolsTruncated = related.Count > request.RelatedLimit;
            var relatedResults = new List<RelatedSourceSymbol>();
            foreach (var relatedSymbol in related.Take(request.RelatedLimit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<SourceDeclaration>? relatedDeclarations = null;
                if (request.IncludeRelated)
                    relatedDeclarations = NormalizeDeclarations(
                        await _inspect.GetSourceAsync(relatedSymbol));

                relatedResults.Add(new RelatedSourceSymbol(
                    relatedSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    relatedSymbol.Kind.ToString(),
                    relatedDeclarations));
            }

            return Result<SourceResult, RtError>.Ok(new SourceResult(
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                declarations,
                relatedSymbolsTruncated,
                relatedResults));
        }
    }

    private static List<SourceDeclaration> NormalizeDeclarations(
        IEnumerable<SourceDeclaration> declarations) => declarations
        .Select(source => source with { Text = RemoveDeclarationIndentation(source.Text) })
        .ToList();

    private static string RemoveDeclarationIndentation(string source)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var firstContentLine = lines.FirstOrDefault(line => line.Length > 0);
        if (firstContentLine is null)
            return normalized;

        var indentationLength = firstContentLine.TakeWhile(char.IsWhiteSpace).Count();
        if (indentationLength == 0)
            return normalized;

        var indentation = firstContentLine[..indentationLength];
        return string.Join(
            Environment.NewLine,
            lines.Select(line => line.StartsWith(indentation, StringComparison.Ordinal)
                ? line[indentation.Length..]
                : line));
    }
}
