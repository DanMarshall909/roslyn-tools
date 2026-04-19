using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class FindDuplicatesCommand : IRtCommand
{
    private FindDuplicatesCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-duplicates",
        Mutates: false,
        JsonOutput: false,
        Category: "Analysis — duplication",
        Aliases: ["fd"],
        WhenToUse: "Discovering repeated code blocks (exact clones) across a solution before an extract-method pass. Use --min-statements to tune noise vs. signal and --min-occurrences to target the most-duplicated blocks first.",
        Examples:
        [
            "rt find-duplicates --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt fd --min-statements 5 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-duplicates --min-statements 4 --min-occurrences 3 --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var minStatementsOpt = new Option<int>("--min-statements", () => 3, "Minimum number of statements in a duplicate window");
        var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum number of occurrences to report a group");

        var cmd = new Command("find-duplicates", "Report exact-clone code blocks repeated across the solution");
        cmd.AddAlias("fd");
        cmd.Add(solutionOpt);
        cmd.Add(minStatementsOpt);
        cmd.Add(minOccurrencesOpt);

        cmd.SetHandler(async (solution, minStatements, minOccurrences) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var options = new FindDuplicatesOptions(minStatements, minOccurrences);
                var groups = await FindDuplicatesCore.RunAsync(sln, options);
                PrintGroups(groups);
            }
        }, solutionOpt, minStatementsOpt, minOccurrencesOpt);

        return cmd;
    }

    private static void PrintGroups(IReadOnlyList<DuplicateGroup> groups)
    {
        if (groups.Count == 0)
        {
            Console.WriteLine("No duplicate groups found.");
            return;
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var partialTag = g.PartiallyExpandable ? "  [partial]" : "";
            Console.WriteLine($"Group {i + 1}  id={g.GroupId}  occurrences={g.Occurrences.Count}  score={g.Score:F2}{partialTag}");
            Console.WriteLine($"preview: {g.Preview}");
            for (var j = 0; j < g.Occurrences.Count; j++)
            {
                var o = g.Occurrences[j];
                Console.WriteLine($"  {j + 1}. {o.FilePath}:{o.StartLine} in {o.ContainingType}.{o.ContainingMember}");
            }
            Console.WriteLine();
        }
    }
}
