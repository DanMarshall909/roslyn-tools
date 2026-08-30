using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record AddXmlDocRequest(
    [property: JsonRequired, JsonPropertyName("from-json")] string FromJson,
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false,
    [property: JsonPropertyName("working-directory")] string? WorkingDirectory = null);

internal sealed record AddXmlDocResult(
    string FromJson,
    int Applied,
    int Skipped,
    IReadOnlyList<string> SkippedSymbols);

internal sealed class AddXmlDocOperation
    : CommandApplicationPort<AddXmlDocRequest, MutationExecutionResult<AddXmlDocResult>>, IRtService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PreparedChangeSetExecutor _executor;
    private readonly AddXmlDocService _service;
    private readonly IWorkspaceSessionFactory _sessions;

    public AddXmlDocOperation(
        IWorkspaceSessionFactory sessions,
        AddXmlDocService service,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _service = service;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition => AddXmlDocCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<AddXmlDocResult>, RtError>> ExecuteAsync(
        AddXmlDocRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var basePath = request.WorkingDirectory ?? Directory.GetCurrentDirectory();
        var path = Path.GetFullPath(request.FromJson, basePath);
        if (!File.Exists(path))
            return Result<MutationExecutionResult<AddXmlDocResult>, RtError>.Err(
                RtError.NotFound($"JSON file not found: {path}"));

        Dictionary<string, string>? docs;
        try
        {
            await using var stream = File.OpenRead(path);
            docs = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            return Result<MutationExecutionResult<AddXmlDocResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Malformed JSON: {exception.Message}",
                new Dictionary<string, string> { ["path"] = path }));
        }
        catch (IOException exception)
        {
            return Result<MutationExecutionResult<AddXmlDocResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                exception.Message,
                new Dictionary<string, string> { ["path"] = path }));
        }

        if (docs is null || docs.Count == 0)
            return Result<MutationExecutionResult<AddXmlDocResult>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "No entries found in the JSON file.",
                new Dictionary<string, string> { ["path"] = path }));

        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            var prepared = await _service.PrepareAsync(solution, docs, cancellationToken);
            var diagnostics = prepared.SkippedSymbols
                .Select(symbol => new MutationDiagnostic(
                    MutationDiagnosticSeverity.Warning,
                    $"Symbol not found, skipping: {symbol}",
                    Context: new Dictionary<string, string> { ["symbol"] = symbol }))
                .ToArray();
            var value = new AddXmlDocResult(
                path,
                prepared.Applied,
                prepared.SkippedSymbols.Count,
                prepared.SkippedSymbols);

            if (prepared.Applied == 0)
                return Result<MutationExecutionResult<AddXmlDocResult>, RtError>.Ok(
                    MutationExecutionResult<AddXmlDocResult>.NoOp(value, diagnostics));

            var changeSet = new PreparedChangeSet(solution, prepared.Solution);
            var execution = request.DryRun
                ? _executor.Preview(value, changeSet, diagnostics)
                : await _executor.ApplyAsync(
                    value,
                    changeSet,
                    diagnostics,
                    cancellationToken);
            return Result<MutationExecutionResult<AddXmlDocResult>, RtError>.Ok(execution);
        }
    }
}

internal static class AddXmlDocCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "add-xml-doc",
        true,
        false,
        "refactor",
        Aliases: ["xmldoc"],
        WhenToUse:
        "Use this instead of manual Edit when bulk-inserting XML <summary> doc comments from an LLM-generated JSON map. " +
        "The JSON file must be a flat object mapping fully-qualified type/member names to summary strings. " +
        "Skips symbols that already have a doc comment. Pair with `rt inspect find-undocumented` to generate the input list.",
        Examples:
        [
            "rt refactor add-xml-doc --from-json docs.json --solution RoslynTools.slnx --dry-run",
            "rt refactor xmldoc --from-json docs.json --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(AddXmlDocRequest),
        typeof(MutationExecutionResult<AddXmlDocResult>),
        true,
        CommandSafety.Mutation,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
