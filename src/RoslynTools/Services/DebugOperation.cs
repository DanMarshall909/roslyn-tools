using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Models;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for recorded LLM trace analysis.</summary>
internal sealed record DebugRequest(
    [property: JsonRequired] string Trace,
    int From = 1,
    int Limit = 100,
    [property: JsonPropertyName("break")] string[]? Breakpoints = null,
    [property: JsonPropertyName("assert")] string[]? Assertions = null,
    [property: JsonPropertyName("break-only")] bool BreakOnly = false);

/// <summary>
/// Shared trace-analysis operation. It returns the typed report or an actionable routine
/// error; hosts own binding, rendering, and exit-code projection.
/// </summary>
internal sealed class DebugOperation : ICommandApplicationPort, IRtService
{
    private static readonly JsonSerializerOptions ErrorSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Func<
        string,
        int,
        int,
        IReadOnlyList<string>,
        IReadOnlyList<string>,
        bool,
        Result<DebugReport, RtError>> _analyze = DebugTraceAnalyzerService.Analyze;

    public string CommandName => "debug";
    public Type RequestType => typeof(DebugRequest);

    internal Task<Result<DebugReport, RtError>> ExecuteAsync(
        DebugRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_analyze(
            request.Trace,
            request.From,
            request.Limit,
            request.Breakpoints ?? [],
            request.Assertions ?? [],
            request.BreakOnly));
    }

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not DebugRequest typedRequest)
            throw new ArgumentException("Invalid debug request.", nameof(request));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        return result.Match(
            CommandPortResult.Success,
            error => CommandPortResult.Failure(JsonSerializer.Serialize(error, ErrorSerializerOptions)));
    }
}
