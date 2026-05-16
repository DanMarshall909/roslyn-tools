using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Narrow dependencies in the solution to reduce complexity.
/// </summary>
internal sealed class NarrowDependencyCommand : IRtCommand
{
    private NarrowDependencyCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "narrow-dependency",
        true,
        false,
        "refactor",
        Aliases: ["nd"],
        WhenToUse:
        "Interface-segregation refactoring: extracts a named subset of an interface's members into a new narrower interface and rewrites consumers that only use that subset to depend on the narrower type. Consumers that require members outside the subset are left unchanged and reported. DI registration updates are reported as a manual follow-up.",
        Examples:
        [
            "rt refactor narrow-dependency IWideFacade --members Send --new-name ISender --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor nd IEmailService --members Send,SendBulk --new-name IEmailSender --solution RoslynTools.slnx --dry-run",
            "rt refactor narrow-dependency ProjectX.Core.IRepository --members GetById,GetAll --new-name IReadOnlyRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ]
    );

    public static Command Build()
    {
        var interfaceArg = new Argument<string>("interface", "Source interface to extract members from");
        var membersOpt = new Option<string[]>("--members", "Comma-separated or repeated member names to extract")
        { IsRequired = true, AllowMultipleArgumentsPerToken = true };
        var newNameOpt = CommandOptionFactory.CreateStringOption("--new-name", "Name for the new narrower interface");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("narrow-dependency",
            "Extract a member subset into a new interface and narrow consumers that only use that subset");
        cmd.AddAlias("nd");
        cmd.Add(interfaceArg);
        cmd.AddAll(membersOpt, newNameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (interfaceSpec, members, newName, solution, dryRun) =>
        {
            // members may arrive as a single comma-separated string or multiple tokens
            var expanded = members
                .SelectMany(m => m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray();

            await ExecuteAsync(interfaceSpec, expanded, newName, solution, dryRun);
        }, interfaceArg, membersOpt, newNameOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Asynchronously narrows dependencies using provided parameters.
    /// </summary>
    internal static async Task ExecuteAsync(
        string sourceInterface,
        string[] members,
        string newInterfaceName,
        string? solutionPath,
        bool dryRun)
    {
        var (workspace, sln) = await RtServiceProvider.Get<WorkspaceLoader>().LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await RtServiceProvider.Get<NarrowDependencyService>()
                .ExecuteAsync(sln, sourceInterface, members, newInterfaceName, dryRun);
        }
    }
}
