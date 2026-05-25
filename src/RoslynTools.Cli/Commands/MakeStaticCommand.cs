using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Creates a static command for executing code changes.
/// </summary>
internal sealed class MakeStaticCommand : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly MakeStaticService _makeStatic;
    public MakeStaticCommand(IWorkspaceSessionFactory sessionFactory, MakeStaticService makeStatic)
    {
        _makeStatic = makeStatic;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "make-static",
        true,
        false,
        "refactor",
        Aliases: ["mst"],
        WhenToUse: "Converting an instance method to static when it has no instance-state dependencies. "
                   + "Validates safety (no 'this', no instance field/property access, not virtual/override/abstract) "
                   + "before modifying and rewrites all call sites from instance.Method() to Type.Method().",
        Examples:
        [
            "rt refactor make-static --method OrderValidator.Validate --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor mst --method MathHelper.Clamp --dry-run",
            "rt refactor make-static --method SymbolLocator.ParseSpec --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method to convert: <Type>.<Method>");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("make-static",
            "Convert an instance method to static when it has no instance-state dependencies");
        cmd.AddAlias("mst");
        cmd.AddAll(methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (method, solution, dryRun) => { await ExecuteAsync(method, solution, dryRun); }, methodOpt,
            solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Asynchronously executes a static command with provided parameters.
    /// </summary>
    internal async Task ExecuteAsync(string methodSpec, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            var exitCode = await _makeStatic.RunAsync(sln, methodSpec, dryRun);
            if (exitCode != 0)
                Environment.ExitCode = exitCode;
        }
    }
}
