using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for extracting a consecutive method-body statement range.</summary>
internal sealed record ExtractMethodRequest(
    [property: JsonRequired, CommandPropertyDescription("Path to the .cs file containing the statements")]
    string File,
    [property: JsonRequired, JsonPropertyName("new-name"), CommandPropertyDescription("Name for the extracted private method")]
    string NewName,
    [property: JsonRequired, JsonPropertyName("start-line"), CommandPropertyDescription("First statement line (1-based)")]
    int StartLine,
    [property: JsonRequired, JsonPropertyName("end-line"), CommandPropertyDescription("Last statement line (1-based, inclusive)")]
    int EndLine,
    [property: CommandPropertyDescription("Insert the new method after this member")]
    string? After = null,
    [property: CommandPropertyDescription("Insert the new method before this member")]
    string? Before = null,
    [property: CommandPropertyDescription("Path to the .sln/.slnx file")]
    string? Solution = null,
    [property: JsonPropertyName("dry-run"), CommandPropertyDescription("Preview without writing")]
    bool DryRun = true,
    [property: JsonPropertyName("replace-duplicates"), CommandPropertyDescription("Replace structural duplicates in the solution")]
    bool ReplaceDuplicates = false);

/// <summary>Details of the source range extracted by <see cref="ExtractMethodOperation" />.</summary>
internal sealed record ExtractMethodResult(
    string File,
    string NewName,
    int StartLine,
    int EndLine,
    bool ReplaceDuplicates,
    bool Changed);

/// <summary>Shared extract-method operation used by CLI, daemon RPC, and MCP adapters.</summary>
internal sealed class ExtractMethodOperation(
    IWorkspaceSessionFactory sessionFactory,
    ISolutionChangeWriter solutionChangeWriter,
    ILogger<ExtractMethodOperation>? logger = null)
    : CommandApplicationPort<ExtractMethodRequest, MutationExecutionResult<ExtractMethodResult>>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly ILogger<ExtractMethodOperation> _logger =
        logger ?? NullLogger<ExtractMethodOperation>.Instance;
    private readonly PreparedChangeSetExecutor _changeSetExecutor = new(
        new SolutionPreparedChangeSetWriter(solutionChangeWriter));

    public override ApplicationCommandDefinition Definition => ExtractMethodCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ExtractMethodResult>, RtError>> ExecuteAsync(
        ExtractMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validationError = Validate(request);
        if (validationError is not null)
            return Result<MutationExecutionResult<ExtractMethodResult>, RtError>.Err(validationError);

        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var changedSolution = await MethodExtractor.ExtractAsync(
                    solution,
                    request.File,
                    request.NewName,
                    request.StartLine,
                    request.EndLine,
                    request.After,
                    request.Before,
                    request.ReplaceDuplicates,
                    _logger);
                cancellationToken.ThrowIfCancellationRequested();

                var result = new ExtractMethodResult(
                    request.File,
                    request.NewName,
                    request.StartLine,
                    request.EndLine,
                    request.ReplaceDuplicates,
                    Changed: true);
                var changes = new PreparedChangeSet(solution, changedSolution);
                var execution = request.DryRun
                    ? _changeSetExecutor.Preview(result, changes)
                    : await _changeSetExecutor.ApplyAsync(
                        result,
                        changes,
                        cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<ExtractMethodResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ExtractMethodResult>, RtError>.Err(
                    CreateOperationError(request, exception));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Result<MutationExecutionResult<ExtractMethodResult>, RtError>.Err(
                    CreateOperationError(request, exception));
            }
        }
    }

    private static RtError? Validate(ExtractMethodRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.File))
            return new RtError(RtErrorKind.InvalidInput, "A source file path is required.");

        if (string.IsNullOrWhiteSpace(request.NewName))
            return new RtError(RtErrorKind.InvalidInput, "A new method name is required.");

        try
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(request.StartLine, 1, "startLine");
            ArgumentOutOfRangeException.ThrowIfLessThan(request.EndLine, 1, "endLine");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(
                request.StartLine,
                request.EndLine,
                "startLine");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return CreateOperationError(request, exception);
        }

        if (request.After is not null && request.Before is not null)
            return new RtError(RtErrorKind.InvalidInput, "--after and --before are mutually exclusive.",
                CreateContext(request));

        return null;
    }

    private static RtError CreateOperationError(ExtractMethodRequest request, Exception exception)
    {
        var context = CreateContext(request);
        return exception.Message.StartsWith("File not found", StringComparison.Ordinal)
            ? RtError.NotFound(exception.Message, context)
            : new RtError(RtErrorKind.InvalidInput, exception.Message, context);
    }

    private static Dictionary<string, string> CreateContext(ExtractMethodRequest request) => new()
    {
        ["file"] = request.File,
        ["newName"] = request.NewName,
        ["lineRange"] = $"{request.StartLine}-{request.EndLine}"
    };
}

internal static class ExtractMethodCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "extract-method",
        true,
        false,
        "refactor",
        Aliases: ["em"],
        WhenToUse:
        "Use this instead of manual Edit when pulling a consecutive range of statements out of a long method body into a new private method. Specify the line range from the source file; the command infers parameter and return types from context. Use --after/--before to control insertion point.",
        Examples:
        [
            "rt refactor extract-method src/Services/BookingService.cs ValidateBooking --start-line 42 --end-line 58 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor em src/Controllers/AdminController.cs BuildCsvRow --start-line 110 --end-line 125 --dry-run",
            "rt refactor extract-method src/RoslynTools/Commands/RenameCommand.cs BuildOptions --start-line 30 --end-line 45 --after Build --solution RoslynTools.slnx"
        ],
        KnownIssues:
        [
            "--replace-duplicates generic type parameterization (US-049): type constraints are NOT inferred. " +
            "When duplicates differ by user-defined generic type arguments (e.g. List<MyType> vs List<OtherType>), " +
            "the extracted method gets <T> but no 'where T : ...' constraint is emitted. " +
            "This means the generated method may not compile if the body calls type-specific members. " +
            "Deferred: constraint inference requires semantic analysis of all member access on T across all call sites.",

            "--replace-duplicates generic type parameterization: only user-defined type arguments (IdentifierNameSyntax) " +
            "are detected as varying. Predefined keyword types (int, string, bool, etc.) produce a PatternMatcher " +
            "structural mismatch so duplicates differing only in keyword types are not detected as generic candidates."
        ],
        SeeAlso: ["inline-method", "find-duplicates", "find-pattern", "transform-parameters"]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ExtractMethodRequest),
        typeof(MutationExecutionResult<ExtractMethodResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        []);
}
