using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ExtractStringConstantRequest(
    [property: JsonRequired] string File,
    [property: JsonRequired] int Line,
    [property: JsonRequired] string Name,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false);

internal sealed record ExtractStringConstantResult(string File, int Line, string Name, bool Changed);

internal sealed class ExtractStringConstantOperation
    : CommandApplicationPort<ExtractStringConstantRequest,
        MutationExecutionResult<ExtractStringConstantResult>>, IRtService
{
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;
    private readonly MagicStringService _service;

    public ExtractStringConstantOperation(
        IWorkspaceSessionFactory sessions,
        MagicStringService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => ExtractStringConstantCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ExtractStringConstantResult>, RtError>> ExecuteAsync(
        ExtractStringConstantRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var prepared = await _service.PrepareExtractConstantAsync(
                    solution,
                    request.File,
                    request.Line,
                    request.Name);
                cancellationToken.ThrowIfCancellationRequested();
                var value = new ExtractStringConstantResult(request.File, request.Line, request.Name, prepared.Changed);
                var changes = new PreparedChangeSet(solution, prepared.Solution);
                var execution = request.DryRun
                    ? _executor.Preview(value, changes)
                    : await _executor.ApplyAsync(
                        value,
                        changes,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<ExtractStringConstantResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ExtractStringConstantResult>, RtError>.Err(
                    new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
        }
    }
}

internal static class ExtractStringConstantCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "extract-string-constant",
        true,
        false,
        "refactor",
        Aliases: ["esc"],
        WhenToUse:
        "Replacing a repeated string literal inside one containing type with a named private const string. Use find-magic-strings first to locate the file and line.",
        Examples:
        [
            "rt refactor extract-string-constant --file src/App/Foo.cs --line 42 --name DefaultStatus --solution C:\\code\\app\\App.sln",
            "rt refactor esc --file tests/FooTests.cs --line 18 --name ExpectedMessage --dry-run --solution RoslynTools.slnx",
            "rt refactor extract-string-constant --file src/Domain/Order.cs --line 27 --name SubmittedStatus --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ExtractStringConstantRequest),
        typeof(MutationExecutionResult<ExtractStringConstantResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
