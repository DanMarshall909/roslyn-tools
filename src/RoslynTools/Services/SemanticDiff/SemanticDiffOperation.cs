using RoslynTools.Core;
using RoslynTools.Services.SemanticDiff.Git;

namespace RoslynTools.Services.SemanticDiff;

internal sealed record SemanticDiffOperationRequest(
    string[]? Files = null,
    string? From = null,
    string? To = null,
    bool IgnoreAccessibility = false,
    bool DetectRenames = true,
    string? Directory = null);

/// <summary>Host-neutral semantic diff data and its precomputed text projection.</summary>
internal sealed record SemanticDiffOperationResult(
    IReadOnlyList<DeclarationDelta> Deltas,
    string Text);

/// <summary>
///     Shared semantic-diff operation. Source-mode selection, options, move suppression,
///     and both output projections live here rather than in either host adapter.
/// </summary>
internal sealed class SemanticDiffOperation(
    GitSemanticDiffSourceProvider gitSource,
    FileSemanticDiffSourceProvider fileSource,
    SemanticDiffService engine,
    ISemanticDiffTextRenderer renderer,
    IGitClientFactory gitClients) : CommandApplicationPort<SemanticDiffOperationRequest, SemanticDiffOperationResult>, IRtService
{
    private readonly GitSemanticDiffSourceProvider _gitSource = gitSource;
    private readonly FileSemanticDiffSourceProvider _fileSource = fileSource;
    private readonly SemanticDiffService _engine = engine;
    private readonly ISemanticDiffTextRenderer _renderer = renderer;
    private readonly IGitClientFactory _gitClients = gitClients;

    public override ApplicationCommandDefinition Definition { get; } = new(
        new RtCommandInfo("semantic-diff", false, true, "analyze", Aliases: ["sdiff"],
            WhenToUse: "Use this instead of git diff when you need a semantic C# diff between two git versions or two files on disc.", Examples: [],
            SupportedFormats: [Core.Output.OutputFormat.Text, Core.Output.OutputFormat.Json]),
        typeof(SemanticDiffOperationRequest), typeof(SemanticDiffOperationResult), true,
        CommandSafety.ReadOnly, CommandExecutionPlacement.InProcess, []);

    public override Task<Result<SemanticDiffOperationResult, RtError>> ExecuteAsync(
        SemanticDiffOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = request.Files ?? [];
        var twoFileMode = files.Length == 2;
        var (before, after) = LoadSources(request, files);
        var result = _engine.Compare(new SemanticDiffRequest(
            before,
            after,
            new SemanticDiffOptions(
                request.IgnoreAccessibility,
                request.DetectRenames)));

        if (twoFileMode)
            result = SuppressSpuriousMoves(result);

        var text = _renderer.Render(result, before, after);
        return Task.FromResult(Result<SemanticDiffOperationResult, RtError>.Ok(
            new SemanticDiffOperationResult(result.Deltas, text)));
    }

    private (IReadOnlyList<SemanticDiffSource> Before, IReadOnlyList<SemanticDiffSource> After)
        LoadSources(SemanticDiffOperationRequest request, string[] files)
    {
        if (files.Length == 0)
        {
            var source = request.Directory is null
                ? _gitSource
                : new GitSemanticDiffSourceProvider(_gitClients.Create(request.Directory));
            return source.Load(request.From, request.To);
        }

        if (files.Length != 2)
            throw new ArgumentException(
                $"semantic-diff takes exactly two files to compare on disc (got {files.Length}); "
                + "supply both, or none to diff git versions.");

        if (request.From is not null || request.To is not null)
            throw new ArgumentException(
                "semantic-diff cannot combine two files on disc with --from/--to.");

        if (request.Directory is not null)
            throw new ArgumentException(
                "semantic-diff cannot combine two files on disc with --directory.");

        return _fileSource.Load(files[0], files[1]);
    }

    private static SemanticDiffResult SuppressSpuriousMoves(SemanticDiffResult result)
    {
        var deltas = result.Deltas
            .Select(static delta =>
                delta.Status == DeclarationStatus.Unchanged
                && delta.OldPath != delta.NewPath
                    ? delta with { OldPath = delta.NewPath }
                    : delta)
            .ToList();
        return new SemanticDiffResult(deltas);
    }
}
