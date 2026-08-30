using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ReplaceNewRequest(
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Replacement,
    int? IfArgIndex = null,
    string? IfArgLiteral = null,
    bool DropMatchedArg = false,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record ReplaceNewResult(
    string Type,
    string Replacement,
    int Replacements);

internal sealed class ReplaceNewOperation
    : CommandApplicationPort<ReplaceNewRequest, MutationExecutionResult<ReplaceNewResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ReplaceNewService _service;

    public ReplaceNewOperation(
        IWorkspaceSessionFactory sessions,
        ReplaceNewService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ReplaceNewCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ReplaceNewResult>, RtError>> ExecuteAsync(
        ReplaceNewRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var prepared = await _service.PrepareAsync(
                solution,
                request.Type,
                request.Replacement,
                request.IfArgIndex,
                request.IfArgLiteral,
                request.DropMatchedArg,
                cancellationToken);
            var value = new ReplaceNewResult(
                request.Type,
                request.Replacement,
                prepared.Replacements);
            if (prepared.Replacements == 0)
                return Result<MutationExecutionResult<ReplaceNewResult>, RtError>.Ok(
                    MutationExecutionResult<ReplaceNewResult>.NoOp(
                        value,
                        [new MutationDiagnostic(
                            MutationDiagnosticSeverity.Information,
                            $"No constructor calls to {request.Type} found.")]));

            cancellationToken.ThrowIfCancellationRequested();
            var changeSet = new PreparedChangeSet(solution, prepared.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet)
                : await _executor.ApplyAsync(
                    value,
                    changeSet,
                    cancellationToken: cancellationToken);
            return Result<MutationExecutionResult<ReplaceNewResult>, RtError>.Ok(execution);
        }
    }
}

internal static class ReplaceNewCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "replace-new",
        true,
        false,
        "refactor",
        Aliases: ["rn"],
        WhenToUse:
        "Use this instead of manual Edit when replacing all constructor invocations of a type with a method call or different constructor, such as when consolidating option construction patterns into factory methods.",
        Examples:
        [
            "rt refactor replace-new --type \"Option<string>\" --with \"Factory.CreateStringOption\" --solution RoslynTools.slnx --dry-run",
            "rt refactor rn --type \"Option<string?>\" --with \"CreateFieldOption\" --if-arg 0 --if-arg-literal \"--field\" --solution RoslynTools.slnx",
            "rt refactor replace-new --type \"Option<string?>\" --with \"Factory.CreateSolutionOption\" --if-arg 0 --if-arg-literal \"--solution\" --drop-matched-arg --solution RoslynTools.slnx --dry-run"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ReplaceNewRequest),
        typeof(MutationExecutionResult<ReplaceNewResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
