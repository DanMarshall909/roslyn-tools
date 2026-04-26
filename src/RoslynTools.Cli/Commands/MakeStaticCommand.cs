using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class MakeStaticCommand : IRtCommand
{
    private MakeStaticCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "make-static",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation — structure",
        Aliases: ["mst"],
        WhenToUse: "Converting an instance method to static when it has no instance-state dependencies. "
            + "Validates safety (no 'this', no instance field/property access, not virtual/override/abstract) "
            + "before modifying and rewrites all call sites from instance.Method() to Type.Method().",
        Examples:
        [
            "rt make-static --method OrderValidator.Validate --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt mst --method MathHelper.Clamp --dry-run",
            "rt make-static --method SymbolLocator.ParseSpec --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var methodOpt = new Option<string>("--method", "Method to convert: <Type>.<Method>") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("make-static", "Convert an instance method to static when it has no instance-state dependencies");
        cmd.AddAlias("mst");
        cmd.AddAll(methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (method, solution, dryRun) =>
        {
            await ExecuteAsync(method, solution, dryRun);
        }, methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string methodSpec, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var exitCode = await MakeStaticCore.RunAsync(sln, methodSpec, dryRun);
            if (exitCode != 0)
                Environment.ExitCode = exitCode;
        }
    }
}
