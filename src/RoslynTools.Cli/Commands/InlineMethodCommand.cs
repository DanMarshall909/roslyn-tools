using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Inlines a method into its calling context
/// </summary>
internal sealed class InlineMethodCommand(IWorkspaceSessionFactory sessionFactory, InlineMethodService inlineMethod) : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly InlineMethodService _inlineMethod = inlineMethod;
    public static RtCommandInfo Info { get; } = new(
        "inline-method",
        true,
        false,
        "refactor",
        Aliases: ["im"],
        WhenToUse:
        "Inlining a trivial helper method (expression-bodied or single-return block) at every call site and removing the method declaration. Best for one-liner wrappers that add no clarity — over-extracted private methods that obscure intent.",
        Examples:
        [
            "rt refactor inline-method --type MathHelper --method Double --solution RoslynTools.slnx",
            "rt refactor im --type OrderService --method GetTotal --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ],
        SeeAlso: ["extract-method", "find-callers"]
    );

    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class containing the method to inline");
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method name to inline at all call sites");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("inline-method", "Inline a method by replacing all call sites with the method body");
        cmd.AddAlias("im");
        cmd.Add(typeOpt);
        cmd.Add(methodOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(
            async (type, method, solution, dryRun) => { await ExecuteAsync(type, method, solution, dryRun); }, typeOpt,
            methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the InlineMethodCommand with async parameters
    /// </summary>
    internal async Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await _inlineMethod.RunAsync(sln, typeName, methodName, dryRun);
        }
    }
}
