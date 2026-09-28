using System.CommandLine;
using System.CommandLine.Invocation;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>User-authoritative CLI surface for the preview persistent repository trust registry.</summary>
internal sealed class TrustCommand(
    TrustAddOperation add,
    TrustListOperation list,
    TrustStatusOperation status,
    TrustRemoveOperation remove) : IControlPlaneCommand, IStructuredOutputCommand
{
    private readonly TrustAddOperation _add = add;
    private readonly TrustListOperation _list = list;
    private readonly TrustStatusOperation _status = status;
    private readonly TrustRemoveOperation _remove = remove;

    public static RtCommandInfo Info => HostCommandMetadata.Trust;

    public Command Build()
    {
        var command = new Command(
            "trust",
            "Manage preview user-scoped trust for reviewed Git repositories");
        command.AddCommand(BuildRepositoryCommand(
            "add",
            "Persist trust for this Git common-directory identity",
            async (repository, context) => await RenderAsync(
                _add.ExecuteAsync(new TrustAddRequest(repository, Apply: true), context.GetCancellationToken()),
                context)));
        command.AddCommand(BuildList(_list));
        command.AddCommand(BuildRepositoryCommand(
            "status",
            "Show trust and registered-worktree Git-object continuity status",
            async (repository, context) => await RenderAsync(
                _status.ExecuteAsync(new TrustStatusRequest(repository), context.GetCancellationToken()),
                context)));
        command.AddCommand(BuildRepositoryCommand(
            "remove",
            "Remove persistent trust for this Git common-directory identity",
            async (repository, context) => await RenderAsync(
                _remove.ExecuteAsync(new TrustRemoveRequest(repository, Apply: true), context.GetCancellationToken()),
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
            RtResultRenderer.Write(RtResult<TrustListResult>.Ok(new TrustListResult(statuses)));
            return;
        }

        if (statuses.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "No repositories are persistently trusted.");
            // Stryker disable once Statement : Falling through only iterates the already-proven empty collection.
            return;
        }

        foreach (var status in statuses)
            RenderStatus(status);
    }

    private static void RenderStatus(RepositoryTrustStatus status)
    {
        if (RtContext.Format == OutputFormat.Json)
        {
            RtResultRenderer.Write(RtResult<RepositoryTrustStatus>.Ok(status));
            return;
        }

        RtConsole.WriteLine(
            RtOutputContext.Result,
            $"{status.State}: {status.WorktreeRoot} ({status.RepositoryId})");
        if (!string.IsNullOrWhiteSpace(status.Reason))
            RtConsole.WriteLine(RtOutputContext.Diagnostic, status.Reason);
    }
}
