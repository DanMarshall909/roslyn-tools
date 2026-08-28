using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for repeated string literal analysis.</summary>
internal sealed record FindMagicStringsRequest(
    int MinLength = 3,
    int MinOccurrences = 2,
    string? Solution = null);

/// <summary>
/// Typed repeated-string analysis result produced before a host selects its output format.
/// </summary>
internal sealed record FindMagicStringsResult(
    string? SolutionPath,
    IReadOnlyList<MagicStringGroup> Groups);

/// <summary>
/// Shared find-magic-strings operation. It owns workspace loading so host adapters only
/// bind their inputs and render this typed result.
/// </summary>
internal sealed class FindMagicStringsOperation
    : CommandApplicationPort<FindMagicStringsRequest, FindMagicStringsResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly MagicStringService _magicStrings;

    public FindMagicStringsOperation(
        IWorkspaceSessionFactory sessionFactory,
        MagicStringService magicStrings)
    {
        _sessionFactory = sessionFactory;
        _magicStrings = magicStrings;
    }

    public override ApplicationCommandDefinition Definition { get; } = new(
        FindMagicStringsCommandDefinition.Info,
        typeof(FindMagicStringsRequest),
        typeof(FindMagicStringsResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);

    public override async Task<Result<FindMagicStringsResult, RtError>> ExecuteAsync(
        FindMagicStringsRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groups = await _magicStrings.AnalyzeAsync(
                solution,
                request.MinLength,
                request.MinOccurrences);

            return Result<FindMagicStringsResult, RtError>.Ok(new FindMagicStringsResult(solution.FilePath, groups));
        }
    }
}
