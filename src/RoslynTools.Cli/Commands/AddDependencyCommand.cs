using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding dependencies to a solution.
/// </summary>
internal sealed class AddDependencyCommand : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly AddDependencyService _addDependency;
    public AddDependencyCommand(IWorkspaceSessionFactory sessionFactory, AddDependencyService addDependency)
    {
        _addDependency = addDependency;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "add-dependency",
        true,
        false,
        "refactor",
        Aliases: ["ad"],
        WhenToUse:
        "Injecting a new constructor dependency into a class without manually editing the constructor and field declarations. Prefer this over a manual edit when the class has multiple constructors or when you want to be confident the field naming convention (_camelCase) is applied consistently.",
        Examples:
        [
            "rt refactor add-dependency --type BookingService --dependency IEmailService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ad --type OrderController --dependency IPaymentGateway --field _payment --dry-run",
            "rt refactor add-dependency --type ReportGenerator --dependency IRepository --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var typeOpt = new Option<string>("--type", "Target class name") { IsRequired = true };
        var depOpt = new Option<string>("--dependency", "Dependency type to inject (e.g. IEmailService)")
        { IsRequired = true };
        var fieldOpt =
            CommandOptionFactory.CreateFieldOption("--field", "Field name override (default: derived from type)");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
        cmd.AddAlias("ad");
        cmd.AddAll(typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(
            async (type, dep, field, solution, dryRun) => { await ExecuteAsync(type, dep, field, solution, dryRun); },
            typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the add dependency command asynchronously.
    /// </summary>
    internal async Task ExecuteAsync(string typeName, string depType, string? fieldName, string? solutionPath,
        bool dryRun)
    {
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await _addDependency.RunAsync(solution, typeName, depType, fieldName, dryRun);
        }
    }

    /// <summary>
    /// Executes the add dependency command in the core context.
    /// </summary>
    internal Task ExecuteCoreAsync(Solution solution, string typeName, string depType, string? fieldName,
        bool dryRun)
    {
        return _addDependency.RunAsync(solution, typeName, depType, fieldName, dryRun);
    }
}
