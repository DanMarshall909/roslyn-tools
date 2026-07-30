using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
///     Shared outline application operation. It owns target dispatch and validation so CLI
///     and MCP cannot drift between file, directory, glob, namespace, and regex behavior.
/// </summary>
internal sealed class OutlineOperation
    : CommandApplicationPort<OutlineRequest, List<object>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly OutlineService _outline;

    public OutlineOperation(IWorkspaceSessionFactory sessionFactory, OutlineService outline)
    {
        _sessionFactory = sessionFactory;
        _outline = outline;
    }

    public override string CommandName => "outline";

    public override async Task<List<object>> ExecuteAsync(
        OutlineRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Target is null && request.Regex is null)
            throw new ArgumentException(
                "Specify a target (file, directory, glob, or namespace) or regex pattern.");

        var (workspace, solution) =
            await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Regex is not null)
                return await _outline.OutlineRegexAsync(
                    solution,
                    request.Regex,
                    request.Summary,
                    request.WithLocations);

            var target = request.Target!;
            if (target.EndsWith(".*", StringComparison.Ordinal)
                && !target.Contains('/')
                && !target.Contains('\\'))
                return await _outline.OutlineNamespaceAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations);

            if (target.Contains('*') || target.Contains('?'))
                return await _outline.OutlineGlobAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations);

            if (Directory.Exists(target))
                return await _outline.OutlineDirectoryAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations);

            if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || Path.Exists(target))
                return await _outline.OutlineFileAsync(
                    solution,
                    target,
                    request.Summary,
                    request.WithLocations);

            return await _outline.OutlineNamespaceAsync(
                solution,
                target,
                request.Summary,
                request.WithLocations);
        }
    }
}
