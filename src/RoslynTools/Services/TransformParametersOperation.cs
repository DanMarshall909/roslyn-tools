using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record TransformParametersRequest(
    [property: JsonRequired] string Method,
    [property: JsonPropertyName("new-type")] string? NewType = null,
    [property: JsonPropertyName("existing-type")] string? ExistingType = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record TransformParametersResult(
    string Method,
    string ParameterType,
    bool ReusedExistingType);

internal sealed class TransformParametersOperation
    : CommandApplicationPort<TransformParametersRequest,
        MutationExecutionResult<TransformParametersResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly TransformParametersService _service;
    private readonly IWorkspaceSessionFactory _sessions;

    public TransformParametersOperation(
        IWorkspaceSessionFactory sessions,
        TransformParametersService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => TransformParametersCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<TransformParametersResult>, RtError>> ExecuteAsync(
        TransformParametersRequest request,
        CancellationToken cancellationToken = default)
    {
        if ((request.NewType is null) == (request.ExistingType is null))
            return Result<MutationExecutionResult<TransformParametersResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                request.NewType is null
                    ? "You must specify either --new-type (to generate a new record) or --existing-type (to reuse an existing type)."
                    : "--new-type and --existing-type are mutually exclusive. Specify only one."));

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var prepared = await _service.PrepareAsync(
                    solution,
                    request.Method,
                    request.NewType,
                    request.ExistingType,
                    cancellationToken);
                var value = new TransformParametersResult(
                    request.Method,
                    request.ExistingType ?? request.NewType!,
                    request.ExistingType is not null);
                var changeSet = new PreparedChangeSet(solution, prepared);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<TransformParametersResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<TransformParametersResult>, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    exception.Message,
                    new Dictionary<string, string> { ["method"] = request.Method }));
            }
        }
    }
}

internal static class TransformParametersCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "transform-parameters",
        true,
        false,
        "refactor",
        Aliases: ["tp"],
        WhenToUse:
        "Introduce a Parameter Object: extract a method's parameters into a new record and update all call sites. Useful when a method has 3+ parameters that logically belong together.",
        Examples:
        [
            "rt refactor transform-parameters --method OrderService.PlaceOrder --new-type PlaceOrderArgs --solution MySolution.sln",
            "rt refactor tp --method BookingService.Create --new-type CreateBookingArgs --dry-run",
            "rt refactor transform-parameters --method Calculator.Add --existing-type MyApp.AddArgs --solution RoslynTools.slnx --dry-run"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(TransformParametersRequest),
        typeof(MutationExecutionResult<TransformParametersResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
