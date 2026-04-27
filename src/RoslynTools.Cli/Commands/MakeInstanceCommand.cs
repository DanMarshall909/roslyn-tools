using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class MakeInstanceCommand : IRtCommand
{
    private MakeInstanceCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "make-instance",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["mns"],
        WhenToUse: "Converting a static method to an instance method. Removes the `static` modifier from the declaration and rewrites `Type.Method(...)` call sites to `instance.Method(...)` where an instance is reachable (local variable, field, or parameter of the correct type). Call sites with no reachable instance are warned and left unchanged.",
        Examples:
        [
            "rt refactor make-instance --method OrderService.Validate --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor mns --method MathHelper.Compute --dry-run --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var methodOpt = new Option<string>("--method", "Fully-qualified method to convert: Type.Method") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("make-instance", "Remove static from a method and rewrite call sites to use an instance");
        cmd.AddAlias("mns");
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
            await MakeInstanceCore.RunAsync(sln, methodSpec, dryRun);
        }
    }
}
