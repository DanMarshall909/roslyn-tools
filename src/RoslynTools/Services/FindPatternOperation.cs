using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for structural pattern matching.</summary>
internal sealed record FindPatternRequest(
    [property: JsonRequired] string File,
    [property: JsonPropertyName("start-line")] int StartLine,
    [property: JsonPropertyName("end-line")] int EndLine,
    string? Solution = null,
    string? Scope = null);

/// <summary>Structured matches returned by <see cref="FindPatternOperation" />.</summary>
internal sealed record FindPatternResult(List<PatternMatch> Matches);

/// <summary>Shared structural-pattern operation used by CLI and protocol adapters.</summary>
internal sealed class FindPatternOperation
    : CommandApplicationPort<FindPatternRequest, FindPatternResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;

    public FindPatternOperation(IWorkspaceSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public override string CommandName => "find_pattern";

    public override async Task<FindPatternResult> ExecuteAsync(
        FindPatternRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = await PatternMatcher.FindMatchesAsync(
            solution, request.File, request.StartLine, request.EndLine, request.Scope);
        return new FindPatternResult(matches);
    }
}
