using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class InlineMethodCommand : IRtCommand
{
    private InlineMethodCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "inline-method",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["im"],
        WhenToUse: "Inlining a trivial helper method (expression-bodied or single-return block) at every call site and removing the method declaration. Best for one-liner wrappers that add no clarity — over-extracted private methods that obscure intent.",
        Examples:
        [
            "rt refactor inline-method --type MathHelper --method Double --solution RoslynTools.slnx",
            "rt refactor im --type OrderService --method GetTotal --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
        ]
    );

    public static Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class containing the method to inline");
        var methodOpt = CommandOptionFactory.CreateMethodOption("--method", "Method name to inline at all call sites");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("inline-method", "Inline a method by replacing all call sites with the method body");
        cmd.AddAlias("im");
        cmd.Add(typeOpt); cmd.Add(methodOpt); cmd.Add(solutionOpt); cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, method, solution, dryRun) =>
        {
            await ExecuteAsync(type, method, solution, dryRun);
        }, typeOpt, methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
            await RtServiceProvider.Get<InlineMethodService>().RunAsync(sln, typeName, methodName, dryRun);
    }
}

