using System.Text.Json;
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

/// <summary>
///     Shared semantic-diff operation. Source-mode selection, options, move suppression,
///     and both output projections live here rather than in either host adapter.
/// </summary>
internal sealed class SemanticDiffOperation : ICommandApplicationPort, IRtService
{
    private readonly GitSemanticDiffSourceProvider _gitSource;
    private readonly FileSemanticDiffSourceProvider _fileSource;
    private readonly SemanticDiffService _engine;
    private readonly SemanticDiffTextRenderer _renderer;

    public SemanticDiffOperation(
        GitSemanticDiffSourceProvider gitSource,
        FileSemanticDiffSourceProvider fileSource,
        SemanticDiffService engine,
        SemanticDiffTextRenderer renderer)
    {
        _gitSource = gitSource;
        _fileSource = fileSource;
        _engine = engine;
        _renderer = renderer;
    }

    public string CommandName => "semantic_diff";
    public Type RequestType => typeof(SemanticDiffOperationRequest);

    internal Task<SemanticDiffOutput> ExecuteAsync(
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
        return Task.FromResult(new SemanticDiffOutput(result.Deltas, text));
    }

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not SemanticDiffOperationRequest typedRequest)
            throw new ArgumentException("Invalid semantic-diff request.", nameof(request));

        var output = await ExecuteAsync(typedRequest, cancellationToken);
        using var json = JsonDocument.Parse(SemanticDiffJson.Serialize(output.Deltas));
        return CommandPortResult.Success(json.RootElement.Clone());
    }

    private (IReadOnlyList<SemanticDiffSource> Before, IReadOnlyList<SemanticDiffSource> After)
        LoadSources(SemanticDiffOperationRequest request, string[] files)
    {
        if (files.Length == 0)
        {
            var source = request.Directory is null
                ? _gitSource
                : new GitSemanticDiffSourceProvider(new GitClient(request.Directory));
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
