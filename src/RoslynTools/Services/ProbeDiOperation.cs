using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record ProbeDiRequest(
    [property: JsonRequired] string Entrypoint,
    string? Solution = null);

internal sealed class ProbeDiOperation(
    IWorkspaceSessionFactory sessionFactory,
    ProbeDiService probeDi)
    : CommandApplicationPort<ProbeDiRequest, Results.ProbeDiResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly ProbeDiService _probeDi = probeDi;

    public override ApplicationCommandDefinition Definition => ProbeDiCommandDefinition.Definition;

    public override async Task<Result<Results.ProbeDiResult, RtError>> ExecuteAsync(
        ProbeDiRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Entrypoint))
            return Result<Results.ProbeDiResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "An entrypoint in Type.Method form is required."));

        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                var registrations = await _probeDi.ProbeAsync(solution, request.Entrypoint);
                cancellationToken.ThrowIfCancellationRequested();
                var root = solution.FilePath is null ? null : Path.GetDirectoryName(solution.FilePath);
                return Result<Results.ProbeDiResult, RtError>.Ok(
                    Results.ProbeDiResult.From(registrations, root, solution.FilePath));
            }
            catch (ArgumentException exception)
            {
                return Result<Results.ProbeDiResult, RtError>.Err(new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Result<Results.ProbeDiResult, RtError>.Err(new RtError(RtErrorKind.NotFound, exception.Message));
            }
        }
    }
}

internal static class ProbeDiCommandDefinition
{
    internal const string Name = "probe_di";
    internal static RtCommandInfo Info { get; } = new(
        "probe-di", false, true, "analyze",
        WhenToUse:
        "Auditing assembly-scanning dependency injection after a successful build. This explicitly executes only the selected registration method and never boots the application host.",
        Examples:
        [
            "rt analyze probe-di --entrypoint Bootstrap.RegisterServices --solution MyApp.slnx"
        ],
        SeeAlso: ["add-dynamic-dependency", "find-unused"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);
    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info, typeof(ProbeDiRequest), typeof(Results.ProbeDiResult), true,
        CommandSafety.ReadOnly, CommandExecutionPlacement.InProcess, []);
}
