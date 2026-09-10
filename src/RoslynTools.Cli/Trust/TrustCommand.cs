using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Trust;

/// <summary>User-authoritative CLI surface for the preview persistent repository trust registry.</summary>
internal static class TrustCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static Command Build(
        TrustAddOperation add,
        TrustListOperation list,
        TrustStatusOperation status,
        TrustRemoveOperation remove)
    {
        var command = new Command(
            "trust",
            "Manage preview user-scoped trust for reviewed Git repositories");
        command.AddCommand(BuildRepositoryCommand(
            "add",
            "Persist trust for this Git common-directory identity",
            async (repository, context) => await RenderAsync(
                add.ExecuteAsync(new TrustAddRequest(repository, Apply: true), context.GetCancellationToken()),
                context)));
        command.AddCommand(BuildList(list));
        command.AddCommand(BuildRepositoryCommand(
            "status",
            "Show trust and registered-worktree Git-object continuity status",
            async (repository, context) => await RenderAsync(
                status.ExecuteAsync(new TrustStatusRequest(repository), context.GetCancellationToken()),
                context)));
        command.AddCommand(BuildRepositoryCommand(
            "remove",
            "Remove persistent trust for this Git common-directory identity",
            async (repository, context) => await RenderAsync(
                remove.ExecuteAsync(new TrustRemoveRequest(repository, Apply: true), context.GetCancellationToken()),
                context)));
        return command;
    }

    private static Command BuildRepositoryCommand(
        string name,
        string description,
        Func<string, InvocationContext, Task> handler)
    {
        var repositoryArg = new Argument<string>(
            "repository",
            "Path to a registered Git worktree, or a repository id for removal");
        var command = new Command(name, description);
        command.AddArgument(repositoryArg);
        command.SetHandler(async context => await handler(
            context.ParseResult.GetValueForArgument(repositoryArg),
            context));
        return command;
    }

    private static Command BuildList(TrustListOperation operation)
    {
        var command = new Command("list", "List persistent repository-trust records");
        command.SetHandler(async context =>
        {
            var result = await operation.ExecuteAsync(
                new TrustListRequest(),
                context.GetCancellationToken());
            result.Match(
                value => RenderList(value.Repositories),
                error => RtErrorPresenter.Surface(context, error));
        });
        return command;
    }

    private static async Task RenderAsync(
        Task<Result<RepositoryTrustStatus, RtError>> resultTask,
        InvocationContext context)
    {
        var result = await resultTask;
        result.Match(RenderStatus, error => RtErrorPresenter.Surface(context, error));
    }

    private static void RenderList(IReadOnlyList<RepositoryTrustStatus> statuses)
    {
        if (RtContext.Format == OutputFormat.Json)
        {
            RtConsole.WriteLine(RtOutputContext.Result, JsonSerializer.Serialize(
                new TrustListResult(statuses),
                JsonOptions));
            return;
        }

        if (statuses.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "No repositories are persistently trusted.");
            return;
        }

        foreach (var status in statuses)
            RenderStatus(status);
    }

    private static void RenderStatus(RepositoryTrustStatus status)
    {
        if (RtContext.Format == OutputFormat.Json)
        {
            RtConsole.WriteLine(
                RtOutputContext.Result,
                JsonSerializer.Serialize(status, JsonOptions));
            return;
        }

        RtConsole.WriteLine(
            RtOutputContext.Result,
            $"{status.State}: {status.WorktreeRoot} ({status.RepositoryId})");
        if (!string.IsNullOrWhiteSpace(status.Reason))
            RtConsole.WriteLine(RtOutputContext.Diagnostic, status.Reason);
    }
}
