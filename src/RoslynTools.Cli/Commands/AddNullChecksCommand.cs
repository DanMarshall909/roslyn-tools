using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding null checks to the project.
/// </summary>
internal sealed class AddNullChecksCommand : IRtCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly AddNullChecksService _addNullChecks;
    public AddNullChecksCommand(IWorkspaceSessionFactory sessionFactory, AddNullChecksService addNullChecks)
    {
        _addNullChecks = addNullChecks;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "add-null-checks",
        true,
        false,
        "refactor",
        Aliases: ["anc"],
        WhenToUse:
        "Retrofitting ArgumentNullException.ThrowIfNull guards onto reference-type parameters in an existing method. Prefer this over a manual edit when a method has several parameters and you want to skip any that are already guarded or are nullable (annotated with ?).",
        Examples:
        [
            "rt refactor add-null-checks --type BookingService --method CreateBooking --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor anc --type OrderProcessor --method Process --dry-run",
            "rt refactor add-null-checks --type ReportController --method Generate --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class containing the method");
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method name to add null checks to");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-null-checks",
            "Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters");
        cmd.AddAlias("anc");
        cmd.AddAll(typeOpt, methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(
            async (type, method, solution, dryRun) => { await ExecuteAsync(type, method, solution, dryRun); }, typeOpt,
            methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the add null checks command asynchronously.
    /// </summary>
    internal async Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await _addNullChecks.RunAsync(sln, typeName, methodName, dryRun);
        }
    }
}
