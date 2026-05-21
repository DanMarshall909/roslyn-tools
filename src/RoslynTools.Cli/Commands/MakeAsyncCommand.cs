using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a method to be asynchronous
/// </summary>
internal sealed class MakeAsyncCommand : IRtCommand
{
    private MakeAsyncCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "make-async",
        true,
        false,
        "refactor",
        Aliases: ["ma"],
        WhenToUse:
        "Converting a synchronous method to async Task<T> and updating its direct call sites with await, without manually hunting every invocation. Use --no-rename to skip the Async suffix when the call sites already have naming that makes async clear.",
        Examples:
        [
            "rt refactor make-async --type BookingService --method Save --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ma --type EmailSender --method Send --no-rename --dry-run",
            "rt refactor make-async --type HeroApiClient --method FetchAvailability --solution RoslynTools.slnx"
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class containing the method") { IsRequired = true };
        var methodOpt = new Option<string>("--method", "Method name to make async") { IsRequired = true };
        var noRenameOpt = new Option<bool>("--no-rename", "Skip renaming to Async suffix");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("make-async", "Convert a synchronous method to async Task<T> and update direct callers");
        cmd.AddAlias("ma");
        cmd.AddAll(typeOpt, methodOpt, noRenameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(
            async (type, method, noRename, solution, dryRun) =>
            {
                await ExecuteAsync(type, method, noRename, solution, dryRun);
            }, typeOpt, methodOpt, noRenameOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the MakeAsyncCommand with async parameters
    /// </summary>
    internal static async Task ExecuteAsync(string typeName, string methodName, bool noRename, string? solutionPath,
        bool dryRun)
    {
        var (workspace, sln) = await RtServiceProvider.GetOrDefault(IWorkspaceSessionFactory.CreateDefault).LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await RtServiceProvider.Get<MakeAsyncService>().RunAsync(sln, typeName, methodName, noRename, dryRun);
        }
    }
}
