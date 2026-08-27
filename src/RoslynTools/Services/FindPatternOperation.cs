using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for structural pattern matching.</summary>
internal sealed record FindPatternRequest(
    [property: JsonRequired] string File,
    [property: JsonRequired] int StartLine,
    [property: JsonRequired] int EndLine,
    string? Solution = null,
    string? Scope = null);

/// <summary>Structured matches returned by <see cref="FindPatternOperation" />.</summary>
internal sealed record FindPatternResult(IReadOnlyList<PatternMatch> Matches);

/// <summary>Shared structural-pattern operation used by CLI, daemon, and MCP adapters.</summary>
internal sealed class FindPatternOperation(IWorkspaceSessionFactory sessionFactory)
    : ICommandApplicationPort, IRtService
{
    private static readonly JsonSerializerOptions ErrorSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;

    public string CommandName => "find_pattern";
    public Type RequestType => typeof(FindPatternRequest);

    internal async Task<Result<FindPatternResult, RtError>> ExecuteAsync(
        FindPatternRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validationError = Validate(request);
        if (validationError is not null)
            return Result<FindPatternResult, RtError>.Err(validationError);

        var solution = await _sessionFactory.GetSolutionAsync(request.Solution);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var matches = await PatternMatcher.FindMatchesAsync(
                solution,
                request.File,
                request.StartLine,
                request.EndLine,
                request.Scope);
            cancellationToken.ThrowIfCancellationRequested();
            return Result<FindPatternResult, RtError>.Ok(new FindPatternResult(matches));
        }
        catch (InvalidOperationException exception)
        {
            var context = new Dictionary<string, string>
            {
                ["file"] = request.File,
                ["lineRange"] = $"{request.StartLine}-{request.EndLine}"
            };
            var error = exception.Message.StartsWith("File not found", StringComparison.Ordinal)
                ? RtError.NotFound(exception.Message, context)
                : new RtError(RtErrorKind.InvalidInput, exception.Message, context);
            return Result<FindPatternResult, RtError>.Err(error);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Result<FindPatternResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Pattern file path is invalid: {exception.Message}",
                new Dictionary<string, string> { ["file"] = request.File }));
        }
    }

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not FindPatternRequest typedRequest)
            throw new ArgumentException("Invalid find-pattern request.", nameof(request));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        return result.Match(
            CommandPortResult.Success,
            error => CommandPortResult.Failure(
                JsonSerializer.Serialize(error, ErrorSerializerOptions)));
    }

    private static RtError? Validate(FindPatternRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.File))
            return new RtError(RtErrorKind.InvalidInput, "A pattern file path is required.");

        if (request.StartLine < 1 || request.EndLine < request.StartLine)
        {
            return new RtError(
                RtErrorKind.InvalidInput,
                "The line range must be positive and end on or after the start line.",
                new Dictionary<string, string>
                {
                    ["startLine"] = request.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["endLine"] = request.EndLine.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }

        return null;
    }
}
