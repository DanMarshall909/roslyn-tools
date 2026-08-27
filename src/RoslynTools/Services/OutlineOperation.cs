using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
///     Shared outline application operation. It owns target dispatch and validation so CLI
///     and MCP cannot drift between file, directory, glob, namespace, and regex behavior.
/// </summary>
internal sealed class OutlineOperation
    : CommandApplicationPort<OutlineRequest, OutlineResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly OutlineService _outline;

    public OutlineOperation(IWorkspaceSessionFactory sessionFactory, OutlineService outline)
    {
        _sessionFactory = sessionFactory;
        _outline = outline;
    }

    public override ApplicationCommandDefinition Definition => OutlineCommandDefinition.Definition;

    public override async Task<Result<OutlineResult, RtError>> ExecuteAsync(
        OutlineRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Target is null && request.Regex is null)
            return Result<OutlineResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "Specify a target (file, directory, glob, or namespace) or regex pattern."));

        var (workspace, solution) =
            await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Regex is not null)
                return Result<OutlineResult, RtError>.Ok(ToResult(await _outline.OutlineRegexAsync(
                    solution,
                    request.Regex,
                    request.Summary,
                    request.WithLocations), request.Summary));

            var target = request.Target!;
            if (target.EndsWith(".*", StringComparison.Ordinal)
                && !target.Contains('/')
                && !target.Contains('\\'))
                return Result<OutlineResult, RtError>.Ok(ToResult(await _outline.OutlineNamespaceAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations), request.Summary));

            if (target.Contains('*') || target.Contains('?'))
                return Result<OutlineResult, RtError>.Ok(ToResult(await _outline.OutlineGlobAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations), request.Summary));

            if (Directory.Exists(target))
                return Result<OutlineResult, RtError>.Ok(ToResult(await _outline.OutlineDirectoryAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations), request.Summary));

            if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || Path.Exists(target))
                return Result<OutlineResult, RtError>.Ok(ToResult(await _outline.OutlineFileAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations), request.Summary));

            return Result<OutlineResult, RtError>.Ok(ToResult(await _outline.OutlineNamespaceAsync(
                solution,
                target,
                request.Summary,
                request.WithLocations), request.Summary));
        }
    }

    private static OutlineResult ToResult(IReadOnlyList<object> items, bool summary)
    {
        if (!summary)
            return new OutlineResult(items.Cast<OutlineTypeResult>().ToList(), null);

        var summaries = items
            .Cast<IReadOnlyDictionary<string, object?>>()
            .Select(static item => new OutlineSummaryResult(
                (string)item["fqn"]!,
                (string)item["kind"]!,
                (int)item["lineFrom"]!,
                (int)item["lineTo"]!,
                item.TryGetValue("description", out var description) ? (string?)description : null))
            .ToList();
        return new OutlineResult(null, summaries);
    }
}
