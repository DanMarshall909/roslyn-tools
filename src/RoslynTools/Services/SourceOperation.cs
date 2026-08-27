using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for retrieving a symbol's source declaration.</summary>
internal sealed record SourceRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null);

/// <summary>One source declaration returned for a resolved symbol.</summary>
internal sealed record SourceDeclaration(
    [property: JsonPropertyName("fqn")] string FullyQualifiedName,
    [property: JsonPropertyName("file")] string FilePath,
    [property: JsonPropertyName("lineFrom")] int LineFrom,
    [property: JsonPropertyName("lineTo")] int LineTo,
    [property: JsonPropertyName("source")] string Text);

/// <summary>Shared source operation used by host adapters.</summary>
internal sealed class SourceOperation
    : CommandApplicationPort<SourceRequest, IReadOnlyList<SourceDeclaration>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly InspectService _inspect;
    private readonly ISymbolResolutionService _symbols;

    public SourceOperation(
        IWorkspaceSessionFactory sessionFactory,
        InspectService inspect,
        ISymbolResolutionService symbols)
    {
        _sessionFactory = sessionFactory;
        _inspect = inspect;
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
    }

    public override ApplicationCommandDefinition Definition => SourceApplicationDefinition.Definition;

    public override async Task<Result<IReadOnlyList<SourceDeclaration>, RtError>> ExecuteAsync(
        SourceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await _symbols.TryFindSymbolByFqnAsync(
                solution,
                request.Symbol,
                request.Kind);
            if (resolved.IsErr)
                return resolved.Map(_ => default(IReadOnlyList<SourceDeclaration>)!);

            var symbol = resolved.Match(value => value, _ => default!);
            cancellationToken.ThrowIfCancellationRequested();
            var sources = await _inspect.GetSourceAsync(symbol);
            return Result<IReadOnlyList<SourceDeclaration>, RtError>.Ok(sources
                .Select(source => source with { Text = RemoveDeclarationIndentation(source.Text) })
                .ToList());
        }
    }

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
