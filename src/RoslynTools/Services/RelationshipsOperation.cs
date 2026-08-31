using System.Diagnostics;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal enum RelationshipMode
{
    References,
    Callers,
    Implementations
}

internal sealed record RelationshipsRequest(
    [property: JsonRequired] string Symbol,
    [property: JsonRequired] RelationshipMode Mode,
    string? Solution = null,
    string? Kind = null,
    bool IncludeTests = false,
    int Limit = 100);

internal sealed record RelationshipItem(
    string Relation,
    string Subject,
    string? TargetSymbol,
    string? TargetKind,
    string? Project,
    string? File,
    int? Line,
    string? Evidence);

internal sealed record RelationshipsResult(
    string ResolvedSubject,
    RelationshipMode Mode,
    IReadOnlyList<RelationshipItem> Items);

/// <summary>
///     Canonical public relationship surface for the three proven legacy engines.
///     Additional graph modes can extend this result without restoring one command per edge kind.
/// </summary>
internal sealed class RelationshipsOperation(
    IWorkspaceSessionFactory sessions,
    FindRefsService references,
    FindCallersService callers,
    FindImplementationsService implementations,
    ISymbolCandidateService symbols)
    : CommandApplicationPort<RelationshipsRequest, RtResultEnvelope<RelationshipsResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions = sessions;
    private readonly FindRefsService _references = references;
    private readonly FindCallersService _callers = callers;
    private readonly FindImplementationsService _implementations = implementations;
    private readonly ISymbolCandidateService _symbols = symbols;

    public override ApplicationCommandDefinition Definition => RelationshipsCommandDefinition.Definition;

    public override async Task<Result<RtResultEnvelope<RelationshipsResult>, RtError>> ExecuteAsync(
        RelationshipsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return Result<RtResultEnvelope<RelationshipsResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A symbol identity or name is required."));
        if (request.Limit <= 0 || request.Limit > Definition.Product.Limits.MaximumItems)
            return Result<RtResultEnvelope<RelationshipsResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Limit must be between 1 and {Definition.Product.Limits.MaximumItems}."));

        var stopwatch = Stopwatch.StartNew();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, cancellationToken);
            var queried = await QueryAsync(solution, request, cancellationToken);
            if (queried.IsErr)
                return queried.Map(_ => default(RtResultEnvelope<RelationshipsResult>)!);
            var queryResult = queried.Unwrap();
            var ordered = queryResult.Items
                .OrderBy(item => item.TargetSymbol, StringComparer.Ordinal)
                .ThenBy(item => item.File, StringComparer.Ordinal)
                .ThenBy(item => item.Line)
                .ToArray();
            var items = ordered.Take(request.Limit).ToArray();
            stopwatch.Stop();
            return Result<RtResultEnvelope<RelationshipsResult>, RtError>.Ok(CreateEnvelope(
                solution,
                completeness,
                queryResult.ResolvedSubject,
                request.Mode,
                items,
                ordered.Length,
                stopwatch.ElapsedMilliseconds));
        }
    }

    private async Task<Result<RelationshipsResult, RtError>> QueryAsync(
        Microsoft.CodeAnalysis.Solution solution,
        RelationshipsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return request.Mode switch
            {
                RelationshipMode.References => Result<RelationshipsResult, RtError>.Ok(
                    await QueryReferencesAsync(solution, request)),
                RelationshipMode.Callers => await QueryCallersAsync(solution, request),
                RelationshipMode.Implementations => await QueryImplementationsAsync(
                    solution,
                    request,
                    cancellationToken),
                _ => Result<RelationshipsResult, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    $"Unsupported relationship mode '{request.Mode}'."))
            };
        }
        catch (InvalidOperationException exception)
        {
            var kind = exception.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)
                ? RtErrorKind.AmbiguousMatch
                : exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || exception.Message.Contains("No symbol", StringComparison.OrdinalIgnoreCase)
                    ? RtErrorKind.NotFound
                    : RtErrorKind.InvalidInput;
            return Result<RelationshipsResult, RtError>.Err(new RtError(
                kind,
                exception.Message,
                new Dictionary<string, string> { ["symbol"] = request.Symbol }));
        }
    }

    private async Task<RelationshipsResult> QueryReferencesAsync(
        Microsoft.CodeAnalysis.Solution solution,
        RelationshipsRequest request)
    {
        var result = await _references.FindAsync(solution, request.Symbol, request.Kind);
        return new RelationshipsResult(
            result.FullyQualifiedName,
            request.Mode,
            result.References.Select(reference => new RelationshipItem(
                "Reference",
                result.FullyQualifiedName,
                null,
                null,
                null,
                reference.FilePath,
                reference.Line,
                reference.LineText)).ToArray());
    }

    private async Task<Result<RelationshipsResult, RtError>> QueryCallersAsync(
        Microsoft.CodeAnalysis.Solution solution,
        RelationshipsRequest request)
    {
        var result = await _callers.FindAsync(solution, request.Symbol, request.IncludeTests);
        return result.Map(value => new RelationshipsResult(
            value.SymbolName,
            request.Mode,
            value.Callers.Select(caller => new RelationshipItem(
                "Caller",
                value.SymbolName,
                caller.ContainingMethod,
                "Method",
                null,
                caller.FilePath,
                caller.Line,
                caller.SourceLine)).ToArray()));
    }

    private async Task<Result<RelationshipsResult, RtError>> QueryImplementationsAsync(
        Microsoft.CodeAnalysis.Solution solution,
        RelationshipsRequest request,
        CancellationToken cancellationToken)
    {
        var resolved = await _symbols.ResolveAsync(solution, request.Symbol, "type", cancellationToken);
        if (resolved.IsErr)
            return resolved.Map(_ => default(RelationshipsResult)!);
        if (resolved.Unwrap() is not Microsoft.CodeAnalysis.INamedTypeSymbol type)
            return Result<RelationshipsResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"'{request.Symbol}' is not a named type."));

        var subject = type.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.CSharpErrorMessageFormat);
        var result = await _implementations.FindAsync(solution, type);
        return Result<RelationshipsResult, RtError>.Ok(new RelationshipsResult(
            subject,
            request.Mode,
            result.Select(implementation =>
            {
                var location = implementation.Locations.Count == 0
                    ? null
                    : implementation.Locations[0];
                return new RelationshipItem(
                    "Implementation",
                    subject,
                    implementation.Fqn,
                    implementation.TypeKind,
                    implementation.Project,
                    location?.File,
                    location?.Line,
                    $"{implementation.Accessibility}; abstract={implementation.IsAbstract}");
            })
                .ToArray()));
    }

    private static RtResultEnvelope<RelationshipsResult> CreateEnvelope(
        Microsoft.CodeAnalysis.Solution solution,
        WorkspaceCompleteness completeness,
        string subject,
        RelationshipMode mode,
        RelationshipItem[] items,
        int total,
        long elapsedMilliseconds)
    {
        var truncated = items.Length < total;
        var limitations = new List<string>
        {
            "Source relationships cannot prove reflection, dependency-injection scanning, markup, generated, or external-consumer edges are absent."
        };
        if (completeness.State != WorkspaceCompletenessState.Complete)
            limitations.Add("The workspace is partial; edges from failed or omitted inputs may be absent.");
        if (truncated)
            limitations.Add("The result reached the item limit; narrow the symbol or mode because continuation is not yet revision-safe.");
        var solutionPath = solution.FilePath;
        return new RtResultEnvelope<RelationshipsResult>(
            "rt.result.v1",
            "relationships",
            completeness.State == WorkspaceCompletenessState.Complete ? "Succeeded" : "Partial",
            new RtSourceIdentity(solutionPath is null ? null : Path.GetDirectoryName(solutionPath), solutionPath, null),
            new RtWorkspaceProvenance(completeness.State.ToString(), solution.ProjectIds.Count, completeness.Diagnostics),
            new RelationshipsResult(subject, mode, items),
            new RtConfidence(
                completeness.State == WorkspaceCompletenessState.Complete ? "High" : "Limited",
                "Roslyn source-symbol relationship analysis",
                [$"Evaluated {mode} for '{subject}'."],
                limitations),
            completeness.Diagnostics.Select((message, index) =>
                new RtStructuredDiagnostic($"workspace.{index + 1}", "Error", message)).ToArray(),
            limitations,
            [],
            new RtResultSafety(["RepositoryRead"], false, false),
            new RtTiming(elapsedMilliseconds),
            new RtPaging(total, items.Length, truncated, null));
    }
}

internal static class RelationshipsCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "relationships",
        false,
        true,
        "inspect",
        "Use this when you need references, callers, or implementations for a resolved symbol through one bounded typed contract before planning a change.",
        [
            "rt inspect relationships references IEmailService --kind type --solution RoslynTools.slnx --json",
            "rt inspect relationships callers ExecuteAsync --include-tests --solution RoslynTools.slnx --json",
            "rt inspect relationships implementations IRtCommand --solution RoslynTools.slnx --json"
        ],
        SeeAlso: ["search-symbols", "describe", "source"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(RelationshipsRequest),
        typeof(RtResultEnvelope<RelationshipsResult>),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        []);
}
