using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class FindUndocumentedCommand : IRtCommand
{
    private FindUndocumentedCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "find-undocumented",
        false,
        false,
        "inspect",
        Aliases: ["undoc"],
        WhenToUse:
        "Finding public types and members that have no XML documentation comment — the first step before bulk-generating docs with an LLM. Use --members to include public methods and properties, not just types.",
        Examples:
        [
            "rt inspect find-undocumented --solution RoslynTools.slnx",
            "rt inspect undoc --members --scope RoslynTools.Core --solution RoslynTools.slnx",
            "rt inspect find-undocumented --json --solution RoslynTools.slnx > undocumented.json"
        ]
    );

    public static Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var membersOpt = new Option<bool>("--members", "Also report undocumented public methods and properties");
        var scopeOpt = new Option<string?>("--scope", "Namespace prefix filter (e.g. RoslynTools.Core)");

        var cmd = new Command("find-undocumented", "Report public types and members with no XML doc comment");
        cmd.AddAlias("undoc");
        cmd.AddAll(solutionOpt, membersOpt, scopeOpt);

        cmd.SetHandler(async (solution, members, scope) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var symbols = await RtServiceProvider.Get<FindUndocumentedService>()
                    .AnalyzeAsync(sln, members, scope);
                RtSerializer.Write(FindUndocumentedResult.From(symbols));
            }
        }, solutionOpt, membersOpt, scopeOpt);

        return cmd;
    }
}
