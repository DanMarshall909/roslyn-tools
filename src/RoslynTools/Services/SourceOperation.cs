using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for retrieving a symbol's source declaration.</summary>
internal sealed record SourceRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null);

/// <summary>Shared source operation used by host adapters.</summary>
internal sealed class SourceOperation
    : CommandApplicationPort<SourceRequest, List<Dictionary<string, object?>>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly InspectService _inspect;
    private readonly ISymbolResolutionService _symbols;

    public SourceOperation(
        IWorkspaceSessionFactory sessionFactory,
        InspectService inspect,
        ISymbolResolutionService? symbols = null)
    {
        _sessionFactory = sessionFactory;
        _inspect = inspect;
        _symbols = symbols ?? new SymbolResolutionService();
    }

    public override string CommandName => "source";

    public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
        SourceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        var symbol = await _symbols.FindSymbolByFqnAsync(solution, request.Symbol, request.Kind);
        cancellationToken.ThrowIfCancellationRequested();
        var sources = await _inspect.GetSourceAsync(symbol);
        foreach (var source in sources)
            if (source.TryGetValue("source", out var value) && value is string text)
                source["source"] = RemoveDeclarationIndentation(text);

        return sources;
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
