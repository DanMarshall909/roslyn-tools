using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record AddGlobalImportRequest(
    [property: JsonRequired] string File,
    [property: JsonRequired] string Namespace,
    string? Solution = null,
    bool DryRun = true);

internal sealed record AddGlobalImportResult(
    string File,
    string Namespace,
    bool Changed);

internal sealed class AddGlobalImportOperation
    : CommandApplicationPort<AddGlobalImportRequest, MutationExecutionResult<AddGlobalImportResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ImportDirectiveService _service;
    private readonly PreparedChangeSetExecutor _changeSetExecutor;

    public AddGlobalImportOperation(
        IWorkspaceSessionFactory sessions,
        ImportDirectiveService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _changeSetExecutor = new PreparedChangeSetExecutor(
            new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => AddGlobalImportCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<AddGlobalImportResult>, RtError>> ExecuteAsync(
        AddGlobalImportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var prepared = await _service.PrepareGlobalUsingAsync(
                    solution,
                    request.File,
                    request.Namespace);
                cancellationToken.ThrowIfCancellationRequested();

                if (!prepared.Changed)
                {
                    var value = new AddGlobalImportResult(request.File, request.Namespace, false);
                    return Result<MutationExecutionResult<AddGlobalImportResult>, RtError>.Ok(
                        MutationExecutionResult<AddGlobalImportResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                prepared.Message ?? "The global using already exists.")]));
                }

                var result = new AddGlobalImportResult(request.File, request.Namespace, true);
                var changeSet = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _changeSetExecutor.Preview(result, changeSet)
                    : await _changeSetExecutor.ApplyAsync(
                        result,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<AddGlobalImportResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<AddGlobalImportResult>, RtError>.Err(
                    RtError.NotFound(exception.Message, new Dictionary<string, string>
                    {
                        ["file"] = request.File
                    }));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                return Result<MutationExecutionResult<AddGlobalImportResult>, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    exception.Message,
                    new Dictionary<string, string>
                    {
                        ["file"] = request.File,
                        ["namespace"] = request.Namespace
                    }));
            }
        }
    }
}

internal static class AddGlobalImportCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "add-global-import",
        true,
        false,
        "refactor",
        Aliases: ["agi"],
        WhenToUse:
        "Use this instead of manual Edit when adding a C# `global using` directive to an existing imports file such as GlobalUsings.cs because a namespace is intentionally available project-wide.",
        Examples:
        [
            "rt refactor add-global-import --file tests/GlobalUsings.cs --namespace Xunit --solution RoslynTools.slnx",
            "rt refactor agi --file src/App/GlobalUsings.cs --namespace Microsoft.Extensions.Logging --dry-run --solution C:\\code\\app\\App.sln",
            "rt refactor add-global-import --file src/App/GlobalUsings.cs --namespace System.Net.Http.Json --solution C:\\code\\app\\App.sln"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(AddGlobalImportRequest),
        typeof(MutationExecutionResult<AddGlobalImportResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
