using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Creates an instance of a given type
/// </summary>
internal sealed class MakeInstanceCommand : IRtCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly MakeInstanceService _makeInstance;
    public MakeInstanceCommand(IWorkspaceSessionFactory sessionFactory, MakeInstanceService makeInstance)
    {
        _makeInstance = makeInstance;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "make-instance",
        true,
        false,
        "refactor",
        Aliases: ["mns"],
        WhenToUse:
        "Converting a static method to an instance method. Removes the `static` modifier from the declaration and rewrites `Type.Method(...)` call sites to `instance.Method(...)` where an instance is reachable (local variable, field, or parameter of the correct type). Call sites with no reachable instance are warned and left unchanged.",
        Examples:
        [
            "rt refactor make-instance --method OrderService.Validate --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor mns --method MathHelper.Compute --dry-run --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var methodOpt =
            CommandOptionFactory.CreateMethodOption("--method", "Fully-qualified method to convert: Type.Method");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("make-instance", "Remove static from a method and rewrite call sites to use an instance");
        cmd.AddAlias("mns");
        cmd.AddAll(methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (method, solution, dryRun) => { await ExecuteAsync(method, solution, dryRun); }, methodOpt,
            solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the MakeInstanceCommand with async parameters
    /// </summary>
    internal async Task ExecuteAsync(string methodSpec, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await _makeInstance.RunAsync(sln, methodSpec, dryRun);
        }
    }
}
