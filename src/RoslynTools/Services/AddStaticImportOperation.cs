using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record AddStaticImportRequest(
    [property: JsonRequired] string File,
    [property: JsonRequired] string Type,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false,
    bool Simplify = false,
    [property: JsonPropertyName("simplify-all-static-imports")]
    bool SimplifyAllStaticImports = false);

internal sealed record AddStaticImportResult(
    string File,
    string Type,
    bool Changed);

internal sealed class AddStaticImportOperation
    : CommandApplicationPort<AddStaticImportRequest, MutationExecutionResult<AddStaticImportResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly ImportDirectiveService _service;
    private readonly IWorkspaceSessionFactory _sessions;

    public AddStaticImportOperation(
        IWorkspaceSessionFactory sessions,
        ImportDirectiveService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => AddStaticImportCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<AddStaticImportResult>, RtError>> ExecuteAsync(
        AddStaticImportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var prepared = await _service.PrepareStaticUsingAsync(
                    solution,
                    request.File,
                    request.Type,
                    request.Simplify,
                    request.SimplifyAllStaticImports);
                cancellationToken.ThrowIfCancellationRequested();
                var value = new AddStaticImportResult(
                    request.File,
                    request.Type,
                    prepared.Changed);
                if (!prepared.Changed)
                    return Result<MutationExecutionResult<AddStaticImportResult>, RtError>.Ok(
                        MutationExecutionResult<AddStaticImportResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                prepared.Message ?? "No static import changes were needed.")]));

                var changes = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changes)
                    : await _executor.ApplyAsync(
                        value,
                        changes,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<AddStaticImportResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<AddStaticImportResult>, RtError>.Err(
                    RtError.NotFound(exception.Message, new Dictionary<string, string>
                    {
                        ["file"] = request.File
                    }));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                return Result<MutationExecutionResult<AddStaticImportResult>, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    exception.Message));
            }
        }
    }
}

internal static class AddStaticImportCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "add-static-import",
        true,
        false,
        "refactor",
        Aliases: ["asi"],
        WhenToUse:
        "Use this instead of manual Edit when adding a C# `using static` directive to a specific file so static members can be referenced without qualifying the containing type. Prefer a narrow file target over solution-wide static imports. Pass --simplify to also strip the now-redundant `<Type>.` qualifiers from the file body in one pass.",
        Examples:
        [
            "rt refactor add-static-import --file tests/MathTests.cs --type Xunit.Assert --solution C:\\code\\app\\App.sln",
            "rt refactor asi --file src/App/Program.cs --type System.Console --dry-run --solution RoslynTools.slnx",
            "rt refactor add-static-import --file src/Commands/FooCommand.cs --type RoslynTools.Core.RtLog --solution RoslynTools.slnx",
            "rt refactor asi --file src/App/Program.cs --type System.Console --simplify --solution RoslynTools.slnx",
            "rt refactor asi --file src/App/Program.cs --type System.Console --simplify-all-static-imports --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(AddStaticImportRequest),
        typeof(MutationExecutionResult<AddStaticImportResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
