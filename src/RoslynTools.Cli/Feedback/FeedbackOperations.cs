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
