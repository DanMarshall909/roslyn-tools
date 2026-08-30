using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Services;

internal sealed record FindRefsRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null);

internal sealed record FindReferenceOperationResult(
    string FilePath,
    int Line,
    string LineText);

internal sealed record FindRefsOperationResult(
    string SymbolName,
    string FullyQualifiedName,
    IReadOnlyList<FindReferenceOperationResult> References,
    [property: JsonPropertyName("_suggestions")]
    IReadOnlyList<ToolSuggestion>? Suggestions);

/// <summary>Shared find-references operation used by CLI and MCP.</summary>
internal sealed class FindRefsOperation
    : CommandApplicationPort<FindRefsRequest, FindRefsOperationResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindRefsService _findRefs;

    public FindRefsOperation(
        IWorkspaceSessionFactory sessionFactory,
        FindRefsService findRefs)
    {
        _sessionFactory = sessionFactory;
        _findRefs = findRefs;
    }

    public override ApplicationCommandDefinition Definition => FindRefsCommandDefinition.Definition;

    public override async Task<Result<FindRefsOperationResult, RtError>> ExecuteAsync(
        FindRefsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _findRefs.FindAsync(solution, request.Symbol, request.Kind);
                cancellationToken.ThrowIfCancellationRequested();
                return Result<FindRefsOperationResult, RtError>.Ok(new FindRefsOperationResult(
                    result.SymbolName,
                    result.FullyQualifiedName,
                    result.References.Select(static reference => new FindReferenceOperationResult(
                        reference.FilePath,
                        reference.Line,
                        reference.LineText)).ToList(),
                    SuggestionHelper.ForFindRefs(result.FullyQualifiedName, request.Solution)));
            }
            catch (InvalidOperationException exception)
            {
                var error = exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? RtError.NotFound(exception.Message, new Dictionary<string, string>
                    {
                        ["symbol"] = request.Symbol
                    })
                    : new RtError(
                        RtErrorKind.InvalidInput,
                        exception.Message,
                        new Dictionary<string, string> { ["symbol"] = request.Symbol });
                return Result<FindRefsOperationResult, RtError>.Err(error);
            }
        }
    }
}

internal static class FindRefsCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "find-refs",
        false,
        false,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Finding all references to any symbol (type, method, property, field, namespace) across the solution using Roslyn's semantic analysis. Prefer this over grep because it resolves overloads and type inference correctly and skips string literals/comments.",
        Examples:
        [
            "rt inspect find-refs --symbol BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol CreateBooking --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol IEmailService --kind type --solution RoslynTools.slnx"
        ],
        SeeAlso: ["find-callers", "find-impl", "rename"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(FindRefsRequest),
        typeof(FindRefsOperationResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
