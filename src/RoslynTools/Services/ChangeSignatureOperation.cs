using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

[JsonConverter(typeof(ChangeSignatureOperationConverter))]
internal enum ChangeSignatureMode
{
    [JsonStringEnumMemberName("rename-param")]
    RenameParam,
    [JsonStringEnumMemberName("reorder")]
    Reorder,
    [JsonStringEnumMemberName("add-param")]
    AddParam,
    [JsonStringEnumMemberName("remove-param")]
    RemoveParam
}

internal sealed class ChangeSignatureOperationConverter
    : JsonStringEnumConverter<ChangeSignatureMode>
{
    public ChangeSignatureOperationConverter() : base(allowIntegerValues: false)
    {
    }
}

internal sealed record ChangeSignatureRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Select rename-param, reorder, add-param, or remove-param.")]
    ChangeSignatureMode Operation,
    [property: JsonRequired] string MethodSpec,
    string? OldName = null,
    string? NewName = null,
    string[]? Order = null,
    string? ParamSpec = null,
    string? After = null,
    string? ParamName = null,
    bool Force = false,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record ChangeSignatureResult(
    [property: JsonConverter(typeof(ChangeSignatureResultModeConverter))] ChangeSignatureMode Mode,
    string MethodSpec,
    bool Changed);

internal sealed class ChangeSignatureResultModeConverter : JsonConverter<ChangeSignatureMode>
{
    public override ChangeSignatureMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (Enum.TryParse<ChangeSignatureMode>(reader.GetString(), out var mode)
            && Enum.IsDefined(mode))
            return mode;

        throw new JsonException("Invalid change-signature result mode.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ChangeSignatureMode value,
        JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

internal sealed class ChangeSignatureOperation
    : CommandApplicationPort<ChangeSignatureRequest,
        MutationExecutionResult<ChangeSignatureResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly ChangeSignatureService _service;
    private readonly IWorkspaceSessionFactory _sessions;

    public ChangeSignatureOperation(
        IWorkspaceSessionFactory sessions,
        ChangeSignatureService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ChangeSignatureCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ChangeSignatureResult>, RtError>> ExecuteAsync(
        ChangeSignatureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Validate(request) is { } validationError)
            return Result<MutationExecutionResult<ChangeSignatureResult>, RtError>.Err(validationError);

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var changed = request.Operation switch
                {
                    ChangeSignatureMode.RenameParam => await _service.RenameParamAsync(
                        solution,
                        request.MethodSpec,
                        request.OldName!,
                        request.NewName!),
                    ChangeSignatureMode.Reorder => await _service.ReorderParamsAsync(
                        solution,
                        request.MethodSpec,
                        request.Order!),
                    ChangeSignatureMode.AddParam => await _service.AddParamAsync(
                        solution,
                        request.MethodSpec,
                        request.ParamSpec!,
                        request.After),
                    ChangeSignatureMode.RemoveParam => await _service.RemoveParamAsync(
                        solution,
                        request.MethodSpec,
                        request.ParamName!,
                        request.Force),
                    _ => throw new InvalidOperationException(
                        $"Unsupported change-signature operation '{request.Operation}'.")
                };
                cancellationToken.ThrowIfCancellationRequested();
                var value = new ChangeSignatureResult(
                    request.Operation,
                    request.MethodSpec,
                    changed.GetChanges(solution).GetProjectChanges().Any());
                if (!value.Changed)
                    return Result<MutationExecutionResult<ChangeSignatureResult>, RtError>.Ok(
                        MutationExecutionResult<ChangeSignatureResult>.NoOp(value));

                var changeSet = new PreparedChangeSet(solution, changed);
                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(
                        value,
                        changeSet,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<ChangeSignatureResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ChangeSignatureResult>, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    exception.Message,
                    new Dictionary<string, string> { ["method"] = request.MethodSpec }));
            }
        }
    }

    private static RtError? Validate(ChangeSignatureRequest request)
    {
        return request.Operation switch
        {
            ChangeSignatureMode.RenameParam when string.IsNullOrWhiteSpace(request.OldName)
                                                 || string.IsNullOrWhiteSpace(request.NewName) =>
                Invalid("rename-param requires old-name and new-name."),
            ChangeSignatureMode.Reorder when request.Order is null || request.Order.Length == 0 =>
                Invalid("reorder requires a non-empty order."),
            ChangeSignatureMode.AddParam when string.IsNullOrWhiteSpace(request.ParamSpec) =>
                Invalid("add-param requires param-spec."),
            ChangeSignatureMode.RemoveParam when string.IsNullOrWhiteSpace(request.ParamName) =>
                Invalid("remove-param requires param-name."),
            _ when !Enum.IsDefined(request.Operation) =>
                Invalid($"Unsupported change-signature operation '{request.Operation}'."),
            _ => null
        };
    }

    private static RtError Invalid(string message) => new(RtErrorKind.InvalidInput, message);
}

internal static class ChangeSignatureCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "change-signature",
        true,
        false,
        "refactor",
        "Use this instead of manual Edit when changing a method signature and updating affected call sites semantically. " +
        "MCP examples: {\"operation\":\"rename-param\",\"methodSpec\":\"MyApp.Service.Run\",\"oldName\":\"old\",\"newName\":\"renamed\"}; " +
        "{\"operation\":\"reorder\",\"methodSpec\":\"MyApp.Service.Run\",\"order\":[\"1\",\"0\"]}; " +
        "{\"operation\":\"add-param\",\"methodSpec\":\"MyApp.Service.Run\",\"paramSpec\":\"int added = 0\"}; " +
        "{\"operation\":\"remove-param\",\"methodSpec\":\"MyApp.Service.Run\",\"paramName\":\"old\"}.",
        [
            "rt refactor change-signature rename-param \"MyApp.Services.OrderService.PlaceOrder\" oldName newName --solution MyApp.sln",
            "rt refactor change-signature reorder \"MyApp.Services.OrderService.PlaceOrder\" \"2,0,1\" --solution MyApp.sln",
            "rt refactor change-signature add-param \"MyApp.Services.OrderService.PlaceOrder\" \"int timeout = 30\" --after orderId --solution MyApp.sln",
            "rt refactor change-signature remove-param \"MyApp.Services.OrderService.PlaceOrder\" timeout --solution MyApp.sln"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ChangeSignatureRequest),
        typeof(MutationExecutionResult<ChangeSignatureResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        [],
        [
            new CommandRequestVariant(
                nameof(ChangeSignatureRequest.Operation),
                "rename-param",
                DiscriminatorRequired: true,
                [nameof(ChangeSignatureRequest.OldName), nameof(ChangeSignatureRequest.NewName)],
                [
                    nameof(ChangeSignatureRequest.Order), nameof(ChangeSignatureRequest.ParamSpec),
                    nameof(ChangeSignatureRequest.After), nameof(ChangeSignatureRequest.ParamName),
                    nameof(ChangeSignatureRequest.Force)
                ]),
            new CommandRequestVariant(
                nameof(ChangeSignatureRequest.Operation),
                "reorder",
                DiscriminatorRequired: true,
                [nameof(ChangeSignatureRequest.Order)],
                [
                    nameof(ChangeSignatureRequest.OldName), nameof(ChangeSignatureRequest.NewName),
                    nameof(ChangeSignatureRequest.ParamSpec), nameof(ChangeSignatureRequest.After),
                    nameof(ChangeSignatureRequest.ParamName), nameof(ChangeSignatureRequest.Force)
                ]),
            new CommandRequestVariant(
                nameof(ChangeSignatureRequest.Operation),
                "add-param",
                DiscriminatorRequired: true,
                [nameof(ChangeSignatureRequest.ParamSpec)],
                [
                    nameof(ChangeSignatureRequest.OldName), nameof(ChangeSignatureRequest.NewName),
                    nameof(ChangeSignatureRequest.Order), nameof(ChangeSignatureRequest.ParamName),
                    nameof(ChangeSignatureRequest.Force)
                ]),
            new CommandRequestVariant(
                nameof(ChangeSignatureRequest.Operation),
                "remove-param",
                DiscriminatorRequired: true,
                [nameof(ChangeSignatureRequest.ParamName)],
                [
                    nameof(ChangeSignatureRequest.OldName), nameof(ChangeSignatureRequest.NewName),
                    nameof(ChangeSignatureRequest.Order), nameof(ChangeSignatureRequest.ParamSpec),
                    nameof(ChangeSignatureRequest.After)
                ])]);
}
