using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record AnalyzeInterfaceRequest(
    [property: JsonRequired] string Interface,
    string? Solution = null,
    string? Scope = null,
    [property: JsonPropertyName("include-tests")] bool IncludeTests = false,
    bool Suggest = false);

internal sealed class AnalyzeInterfaceOperation(
    IWorkspaceSessionFactory sessionFactory,
    AnalyzeInterfaceService analyzeInterface)
    : CommandApplicationPort<AnalyzeInterfaceRequest, Models.AnalyzeInterfaceReport>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly AnalyzeInterfaceService _analyzeInterface = analyzeInterface;

    public override ApplicationCommandDefinition Definition => AnalyzeInterfaceCommandDefinition.Definition;

    public override async Task<Result<Models.AnalyzeInterfaceReport, RtError>> ExecuteAsync(
        AnalyzeInterfaceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Interface))
            return Result<Models.AnalyzeInterfaceReport, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "An interface symbol is required."));

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return Result<Models.AnalyzeInterfaceReport, RtError>.Ok(await _analyzeInterface.AnalyzeAsync(
                    solution,
                    request.Interface,
                    new Models.AnalyzeInterfaceOptions(request.Scope, request.IncludeTests, request.Suggest)));
            }
            catch (InvalidOperationException exception)
            {
                var kind = exception.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)
                    ? RtErrorKind.AmbiguousMatch
                    : exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                        || exception.Message.Contains("No symbol", StringComparison.OrdinalIgnoreCase)
                        ? RtErrorKind.NotFound
                        : RtErrorKind.InvalidInput;
                return Result<Models.AnalyzeInterfaceReport, RtError>.Err(new RtError(
                    kind,
                    exception.Message,
                    new Dictionary<string, string> { ["interface"] = request.Interface }));
            }
        }
    }
}

internal static class AnalyzeInterfaceCommandDefinition
{
    internal const string Name = "analyze_interface";
    internal static RtCommandInfo Info { get; } = new(
        "analyze-interface", false, true, "analyze",
        "Use this instead of Read or raw reference dumps when grouping interface consumers by the exact interface members they use so interface-segregation work can be driven by evidence. Use --scope to focus on one namespace or project, and --json for automation-friendly output.",
        [
            "rt analyze analyze-interface ProjectX.Core.IEmailSender --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze analyze-interface IEmailSender --scope ProjectX.Core --solution RoslynTools.slnx",
            "rt analyze analyze-interface ProjectX.Core.IEmailSender --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);
    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info, typeof(AnalyzeInterfaceRequest), typeof(Models.AnalyzeInterfaceReport), true,
        CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, []);
}
