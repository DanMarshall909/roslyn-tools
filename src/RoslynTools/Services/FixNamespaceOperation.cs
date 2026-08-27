using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record FixNamespaceRequest(
    [property: JsonRequired] string File,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record FixNamespaceResult(string File, bool Changed);

internal sealed class FixNamespaceOperation
    : CommandApplicationPort<FixNamespaceRequest, MutationExecutionResult<FixNamespaceResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly FixNamespaceService _service;

    public FixNamespaceOperation(
        IWorkspaceSessionFactory sessions,
        FixNamespaceService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => FixNamespaceCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<FixNamespaceResult>, RtError>> ExecuteAsync(
        FixNamespaceRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var prepared = await _service.PrepareAsync(solution, Path.GetFullPath(request.File));
                cancellationToken.ThrowIfCancellationRequested();
                var value = new FixNamespaceResult(request.File, prepared.Changed);
                if (!prepared.Changed)
                    return Result<MutationExecutionResult<FixNamespaceResult>, RtError>.Ok(
                        MutationExecutionResult<FixNamespaceResult>.NoOp(
                            value,
                            prepared.Message is null
                                ? []
                                : [new MutationDiagnostic(
                                    MutationDiagnosticSeverity.Information,
                                    prepared.Message)]));
                var set = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, set)
                    : await _executor.ApplyAsync(value, set, cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<FixNamespaceResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<FixNamespaceResult>, RtError>.Err(
                    RtError.NotFound(exception.Message, new Dictionary<string, string> { ["file"] = request.File }));
            }
        }
    }
}

internal static class FixNamespaceCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "fix-namespace",
        true,
        false,
        "refactor",
        Aliases: ["fns"],
        WhenToUse:
        "Correcting a file's namespace declaration to match its folder path after moving files, or when a file was created in the wrong location. Also updates any using directives in other files that imported the old namespace.",
        Examples:
        [
            "rt refactor fix-namespace --file src/Services/BookingService.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor fns --file src/RoslynTools/Commands/RenameCommand.cs --dry-run",
            "rt refactor fix-namespace --file src/Domain/Order.cs --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(FixNamespaceRequest),
        typeof(MutationExecutionResult<FixNamespaceResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
