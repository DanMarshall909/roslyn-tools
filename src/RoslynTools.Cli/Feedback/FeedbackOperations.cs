using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Feedback;

internal sealed record FeedbackAddRequest(
    [property: JsonRequired] string? Description = null,
    string? SampleCode = null,
    string? ExpectedResult = null,
    string? CommandLine = null);

internal sealed record FeedbackAddResult(string Id, string Status);

internal sealed class FeedbackAddOperation
    : CommandApplicationPort<FeedbackAddRequest, FeedbackAddResult>, IRtService
{
    private readonly Func<string, string?, string?, string?, FeedbackEntry> _add;

    public FeedbackAddOperation()
        : this(FeedbackStore.AddUserFeedback)
    {
    }

    internal FeedbackAddOperation(
        Func<string, string?, string?, string?, FeedbackEntry> add)
    {
        _add = add;
    }

    public override string CommandName => "feedback_add";

    public override Task<FeedbackAddResult> ExecuteAsync(
        FeedbackAddRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Description is required.");

        var entry = _add(
            request.Description,
            request.SampleCode,
            request.ExpectedResult,
            request.CommandLine);
        return Task.FromResult(new FeedbackAddResult(entry.Id, entry.Status));
    }
}

internal sealed record FeedbackListRequest(string? Status = "pending");

internal sealed class FeedbackListOperation
    : CommandApplicationPort<FeedbackListRequest, List<FeedbackEntry>>, IRtService
{
    private readonly Func<string, List<FeedbackEntry>> _list;

    public FeedbackListOperation()
        : this(FeedbackStore.ListByStatus)
    {
    }

    internal FeedbackListOperation(Func<string, List<FeedbackEntry>> list)
    {
        _list = list;
    }

    public override string CommandName => "feedback_list";

    public override Task<List<FeedbackEntry>> ExecuteAsync(
        FeedbackListRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_list(request.Status ?? "pending"));
    }
}

internal sealed record FeedbackProcessRequest(
    [property: JsonRequired] string? Id = null,
    [property: JsonRequired] string? Resolution = null);

internal sealed record FeedbackProcessResult(
    string Id,
    string Status,
    string ProcessedAt,
    string Resolution);

internal sealed class FeedbackProcessOperation
    : CommandApplicationPort<FeedbackProcessRequest, FeedbackProcessResult>, IRtService
{
    private readonly Func<string, string, FeedbackEntry> _process;

    public FeedbackProcessOperation()
        : this(FeedbackStore.MarkProcessed)
    {
    }

    internal FeedbackProcessOperation(Func<string, string, FeedbackEntry> process)
    {
        _process = process;
    }

    public override string CommandName => "feedback_process";

    public override Task<FeedbackProcessResult> ExecuteAsync(
        FeedbackProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Id))
            throw new ArgumentException("Feedback ID is required.");
        if (string.IsNullOrWhiteSpace(request.Resolution))
            throw new ArgumentException("Resolution is required.");

        var entry = _process(request.Id, request.Resolution);
        return Task.FromResult(new FeedbackProcessResult(
            entry.Id,
            entry.Status,
            entry.ProcessedAt!,
            entry.Resolution!));
    }
}
