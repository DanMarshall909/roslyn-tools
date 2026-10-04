using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ExtractClassRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Source class containing the members to extract")]
    string Type,
    [property: JsonRequired]
    [property: CommandPropertyDescription("Member names to extract")]
    string[] Members,
    [property: JsonRequired]
    [property: JsonPropertyName("new-type")]
    [property: CommandPropertyDescription("Name for the extracted class")]
    string NewType,
    [property: JsonPropertyName("static")]
    [property: CommandPropertyDescription("Create a static class without constructor injection")]
    bool IsStatic = false,
    [property: JsonPropertyName("output-dir")]
    [property: CommandPropertyDescription("Directory for the new class file")]
    string? OutputDir = null,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true)
{
    internal ExtractClassRequest(
        string Type,
        string Members,
        string NewType,
        bool IsStatic = false,
        string? OutputDir = null,
        string? Solution = null,
        bool DryRun = true)
        : this(
            Type,
            Members.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            NewType,
            IsStatic,
            OutputDir,
            Solution,
            DryRun)
    {
    }
}

internal sealed record ExtractClassResult(
    string Type,
    IReadOnlyList<string> Members,
    string NewType,
    bool IsStatic,
    string NewFilePath);

internal sealed class ExtractClassOperation
    : CommandApplicationPort<ExtractClassRequest, MutationExecutionResult<ExtractClassResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly ExtractClassService _service;

    public ExtractClassOperation(
        IWorkspaceSessionFactory sessions,
        ExtractClassService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ExtractClassCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ExtractClassResult>, RtError>> ExecuteAsync(
        ExtractClassRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var preparationResult = await _service.PrepareAsync(
                solution,
                request.Type,
                request.Members,
                request.NewType,
                request.IsStatic,
                request.OutputDir,
                cancellationToken);
            ExtractClassService.Preparation? preparation = null;
            RtError? error = null;
            preparationResult.Match(value => preparation = value, failure => error = failure);
            if (error is not null)
                return Result<MutationExecutionResult<ExtractClassResult>, RtError>.Err(error);

            cancellationToken.ThrowIfCancellationRequested();
            var value = new ExtractClassResult(
                preparation!.Type,
                preparation.Members,
                preparation.NewType,
                preparation.IsStatic,
                preparation.NewFilePath);
            var changeSet = new PreparedChangeSet(solution, preparation.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet)
                : await _executor.ApplyAsync(
                    value,
                    changeSet,
                    cancellationToken: cancellationToken);
            return Result<MutationExecutionResult<ExtractClassResult>, RtError>.Ok(execution);
        }
    }
}

internal static class ExtractClassCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "extract-class",
        true,
        false,
        "refactor",
        Aliases: ["ec"],
        WhenToUse:
        "Use this instead of manual Edit when splitting a large or oversized class by moving selected named members into a new dedicated class file. When --static is not set, the command also wires the new class into the source class as a constructor-injected dependency, making it immediately usable without manual plumbing.",
        Examples:
        [
            "rt refactor extract-class --type AdminController --members ExportCsv,ImportCsv --new-type CsvController --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ec --type ReportService --members BuildReport,FormatReport --new-type ReportFormatter --static --dry-run",
            "rt refactor extract-class --type ProductService --members ValidateProduct --new-type ProductValidator --solution RoslynTools.slnx --output-dir src/Validators"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ExtractClassRequest),
        typeof(MutationExecutionResult<ExtractClassResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
