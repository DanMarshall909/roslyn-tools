using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class StaticClassInventoryCommand : IRtCommand
{
    private StaticClassInventoryCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "static-class-inventory",
        false,
        true,
        "analyze",
        "Planning a static-to-instance migration by listing every static class with its call-site count, member count, and inbound static coupling. Results are sorted leaf-first so the safest classes to de-static appear at the top.",
        [
            "rt analyze static-class-inventory --solution RoslynTools.slnx",
            "rt analyze static-class-inventory --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt static-class-inventory --solution RoslynTools.slnx --json"
        ]);

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");

        var cmd = new Command("static-class-inventory",
            "List every static class with call-site count, member count, and inbound coupling for migration planning");
        cmd.Add(solutionOpt);

        cmd.SetHandler(async solution =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var report = await RtServiceProvider.Get<StaticClassInventoryService>().AnalyzeAsync(sln);
                RtSerializer.Write(StaticClassInventoryResult.FromReport(report));
            }
        }, solutionOpt);

        return cmd;
    }
}
