using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Services;

[JsonConverter(typeof(JsonStringEnumConverter<AddTestMode>))]
internal enum AddTestMode
{
    Legacy,
    Command
}

internal sealed record AddTestRequest(
    [property: JsonRequired] AddTestMode Mode,
    string? ClassName = null,
    string? Name = null,
    string? AcceptanceCriteria = null,
    string? NestedClass = null,
    string? Command = null,
    string? Scenario = null,
    string? Outcome = null,
    string? Concern = null,
    string? BeforePath = null,
    string? AfterPath = null,
    string? FilePath = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record AddTestResult(
    AddTestMode Mode,
    string Target,
    string? Command);

internal sealed class AddTestOperation
    : CommandApplicationPort<AddTestRequest, MutationExecutionResult<AddTestResult>>, IRtService
{
    private readonly ICommandCatalog _catalog;
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly AddTestService _service;

    public AddTestOperation(
        IWorkspaceSessionFactory sessions,
        AddTestService service,
        ICommandCatalog catalog,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _catalog = catalog;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => AddTestCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<AddTestResult>, RtError>> ExecuteAsync(
        AddTestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Validate(request) is { } validationError)
            return Result<MutationExecutionResult<AddTestResult>, RtError>.Err(validationError);

        RtCommandInfo? commandInfo = null;
        if (request.Mode == AddTestMode.Command)
        {
            commandInfo = _catalog.All.FirstOrDefault(info =>
                info.Name == request.Command
                || (info.Aliases?.Contains(request.Command!, StringComparer.Ordinal) ?? false));
            if (commandInfo is null)
                return Result<MutationExecutionResult<AddTestResult>, RtError>.Err(
                    RtError.NotFound($"Command '{request.Command}' not found in registry."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var preparation = request.Mode == AddTestMode.Legacy
                ? await _service.PrepareLegacyAsync(
                    solution,
                    request.ClassName!,
                    request.Name,
                    request.AcceptanceCriteria,
                    request.NestedClass,
                    request.FilePath)
                : await AddTestService.PrepareCommandBasedAsync(
                    solution,
                    commandInfo!,
                    request.Scenario,
                    request.Outcome,
                    request.Concern!,
                    request.BeforePath,
                    request.AfterPath);

            if (preparation is null)
            {
                var target = request.Mode == AddTestMode.Legacy
                    ? $"Class '{request.ClassName}'"
                    : $"Test class for command '{request.Command}'";
                return Result<MutationExecutionResult<AddTestResult>, RtError>.Err(
                    RtError.NotFound($"{target} was not found in the solution."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var value = new AddTestResult(
                request.Mode,
                request.ClassName ?? preparation.TargetClass,
                request.Command);
            var changeSet = new PreparedChangeSet(solution, preparation.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet)
                : await _executor.ApplyAsync(value, changeSet, cancellationToken: cancellationToken);
            return Result<MutationExecutionResult<AddTestResult>, RtError>.Ok(execution);
        }
    }

    private static RtError? Validate(AddTestRequest request)
    {
        if (request.Mode == AddTestMode.Legacy)
        {
            if (string.IsNullOrWhiteSpace(request.ClassName))
                return Invalid("Legacy mode requires --class.");
            if (request.Name is null && request.AcceptanceCriteria is null)
                return Invalid("Legacy mode requires either --name or --acs.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Command))
            return Invalid("Command mode requires --command.");
        if (string.IsNullOrWhiteSpace(request.Concern))
            return Invalid("--concern is required with --command mode.");

        var hasScenarioOutcome = request.Scenario is not null || request.Outcome is not null;
        var hasBeforeAfter = request.BeforePath is not null || request.AfterPath is not null;
        if (!hasScenarioOutcome && !hasBeforeAfter)
            return Invalid(
                "Either --scenario and --outcome, or --before and --after is required with --command mode.");
        if (hasScenarioOutcome && hasBeforeAfter)
            return Invalid("--scenario/--outcome and --before/--after are mutually exclusive.");

        return null;
    }

    private static RtError Invalid(string message) => new(RtErrorKind.InvalidInput, message);
}

internal static class AddTestCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "add-test",
        true,
        false,
        "refactor",
        Aliases: ["at"],
        WhenToUse:
        "Use this instead of manual Edit when appending xUnit [Fact] stub methods to an existing test class. Two modes: legacy (--class with --name or --acs) adds to arbitrary test classes; command-based (--command with --scenario/--outcome or --before/--after) scaffolds organized test files by rt command category.",
        Examples:
        [
            "rt refactor add-test --class BookingServiceTests --name Returns_404_When_Not_Found --solution RoslynTools.slnx",
            "rt refactor add-test --class OrderTests --acs \"Returns total;Applies discount\" --nested-class WhenCalculatingTotal",
            "rt refactor add-test --command ExtractMethod --scenario \"source file as first occurrence\" --outcome \"call site updated\" --concern SourceFileCallSite --solution RoslynTools.slnx",
            "rt refactor add-test --command Rename --concern CrossFileRefs --before before.cs --after after.cs --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(AddTestRequest),
        typeof(MutationExecutionResult<AddTestResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
