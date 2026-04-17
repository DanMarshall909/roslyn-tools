using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class ListSmellsCommand
{
    internal static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var maxClassOpt = new Option<int>("--max-class-lines", () => 300, "Max lines per class");
        var maxMethodOpt = new Option<int>("--max-method-lines", () => 40, "Max lines per method");
        var maxParamsOpt = new Option<int>("--max-params", () => 7, "Max constructor params");
        var scopeOpt = new Option<string?>("--scope", "Namespace/project prefix filter");

        var cmd = new Command("list-smells", "Report code smells: large classes, long methods, over-injected constructors");
        cmd.AddAlias("smells");
        cmd.Add(solutionOpt);
        cmd.Add(maxClassOpt);
        cmd.Add(maxMethodOpt);
        cmd.Add(maxParamsOpt);
        cmd.Add(scopeOpt);

        cmd.SetHandler(async (solution, maxClass, maxMethod, maxParams, scope) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                Console.Error.WriteLine("Analyzing solution for code smells...");

                var smells = await SmellAnalyzer.AnalyzeAsync(sln, maxClass, maxMethod, maxParams, scope);

                foreach (var smell in smells)
                {
                    var member = smell.MemberName is not null ? $"  {smell.MemberName}" : "";
                    Console.WriteLine($"{smell.SmellType,-24}{smell.TypeName,-30}{member,-20}{smell.FilePath}:{smell.Line}  {smell.Detail}");
                }

                Console.Error.WriteLine($"Found {smells.Count} smell(s).");
            }
        }, solutionOpt, maxClassOpt, maxMethodOpt, maxParamsOpt, scopeOpt);

        return cmd;
    }
}
