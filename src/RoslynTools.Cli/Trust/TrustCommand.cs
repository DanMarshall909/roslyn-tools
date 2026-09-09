using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Cli.Trust;

/// <summary>Explicit user configuration command, outside repository analysis and refactoring discovery.</summary>
internal static class TrustCommand
{
    internal static Command Build(RepositoryTrustService service)
    {
        var command = new Command("trust", "Manage user-owned trusted roots without evaluating repositories");
        var addRoot = new Argument<string>("root", "Absolute existing directory to trust; approval permits executing its build inputs");
        var add = new Command("add", "Persist explicit approval for an exact root (linked worktrees require separate approval)") { addRoot };
        add.SetHandler(context => service.Add(context.ParseResult.GetValueForArgument(addRoot), context.GetCancellationToken())
            .Match(value => RtResultRenderer.Write(RtResult<RepositoryTrustChange>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error)));

        var removeRoot = new Argument<string>("root", "Exact absolute directory to revoke, even if it no longer exists");
        var remove = new Command("remove", "Revoke a saved root without deleting any repository files") { removeRoot };
        remove.SetHandler(context => service.Remove(context.ParseResult.GetValueForArgument(removeRoot), context.GetCancellationToken())
            .Match(value => RtResultRenderer.Write(RtResult<RepositoryTrustChange>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error)));

        var list = new Command("list", "List saved roots and the user-owned registry location");
        list.SetHandler(context => service.List()
            .Match(value => RtResultRenderer.Write(RtResult<RepositoryTrustListing>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error)));
        command.AddCommand(add);
        command.AddCommand(list);
        command.AddCommand(remove);
        return command;
    }
}
