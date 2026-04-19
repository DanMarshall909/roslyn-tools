using System.CommandLine;
using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class FindDuplicatesCommand : IRtCommand
{
    private FindDuplicatesCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-duplicates",
        Mutates: false,
        JsonOutput: true,
        Category: "Analysis — duplication",
        Aliases: ["fd"],
        WhenToUse: "Discovering repeated code blocks (exact clones) across a solution before an extract-method pass. Use --min-statements to tune noise vs. signal, --min-occurrences to target the most-duplicated blocks, and --scope to focus on one subsystem.",
        Examples:
        [
            "rt find-duplicates --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt fd --min-statements 5 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-duplicates --min-statements 4 --min-occurrences 3 --solution RoslynTools.slnx",
            "rt find-duplicates --scope MyApp.Services --solution RoslynTools.slnx",
            "rt find-duplicates --include-tests --solution RoslynTools.slnx",
            "rt find-duplicates --max-statements 20 --solution RoslynTools.slnx",
            "rt find-duplicates --json --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var minStatementsOpt = new Option<int>("--min-statements", () => 3, "Minimum number of statements in a duplicate window");
        var minOccurrencesOpt = new Option<int>("--min-occurrences", () => 2, "Minimum number of occurrences to report a group");
        var scopeOpt = new Option<string?>("--scope", "Project name or namespace prefix to restrict search to");
        var includeTestsOpt = new Option<bool>("--include-tests", () => false, "Include test projects (excluded by default)");
        var maxStatementsOpt = new Option<int>("--max-statements", () => 40, "Hard cap on duplicate window size");
        var jsonOpt = new Option<bool>("--json", () => false, "Emit results as JSON to stdout");

        var cmd = new Command("find-duplicates", "Report exact-clone code blocks repeated across the solution");
        cmd.AddAlias("fd");
        cmd.Add(solutionOpt);
        cmd.Add(minStatementsOpt);
        cmd.Add(minOccurrencesOpt);
        cmd.Add(scopeOpt);
        cmd.Add(includeTestsOpt);
        cmd.Add(maxStatementsOpt);
        cmd.Add(jsonOpt);

        cmd.SetHandler(async (solution, minStatements, minOccurrences, scope, includeTests, maxStatements, json) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var options = new FindDuplicatesOptions(minStatements, minOccurrences, scope, includeTests, maxStatements);
                var groups = await FindDuplicatesCore.RunAsync(sln, options);

                if (json)
                {
                    var solutionDir = sln.FilePath is not null
                        ? Path.GetDirectoryName(sln.FilePath)
                        : null;
                    PrintJson(groups, solutionDir);
                }
                else
                {
                    PrintGroups(groups);
                }
            }
        }, solutionOpt, minStatementsOpt, minOccurrencesOpt, scopeOpt, includeTestsOpt, maxStatementsOpt, jsonOpt);

        return cmd;
    }

    internal static void PrintJson(IReadOnlyList<DuplicateGroup> groups, string? solutionDir)
    {
        var doc = new
        {
            mode = "structural",
            groups = groups.Select(g => new
            {
                groupId = g.GroupId,
                mode = "structural",
                score = g.Score,
                occurrenceCount = g.Occurrences.Count,
                extractability = g.Extractability,
                preview = g.Preview,
                occurrences = g.Occurrences.Select(o => new
                {
                    file = MakeRelativePath(o.FilePath, solutionDir),
                    line = o.StartLine,
                    endLine = o.EndLine,
                    type = o.ContainingType,
                    member = o.ContainingMember,
                    link = o.FilePath.Replace('\\', '/') + ":" + o.StartLine,
                }).ToArray(),
                checks = (g.Checks ?? []).Select(c => new
                {
                    name = c.Name,
                    status = c.Status,
                    detail = c.Detail,
                }).ToArray(),
            }).ToArray(),
        };

        Console.WriteLine(JsonSerializer.Serialize(doc, SymbolFormatter.DefaultJsonOptions));
    }

    private static string MakeRelativePath(string absolutePath, string? solutionDir)
    {
        if (solutionDir is not null)
        {
            try
            {
                return Path.GetRelativePath(solutionDir, absolutePath);
            }
            catch
            {
                // Fall through to filename fallback
            }
        }

        return Path.GetFileName(absolutePath);
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
            Console.WriteLine($"Group {i + 1}  id={g.GroupId}  occurrences={g.Occurrences.Count}  score={g.Score:F2}  extractable={g.Extractability}{partialTag}");
            Console.WriteLine($"preview: {g.Preview}");
            if (g.Checks is { Count: > 0 })
            {
                foreach (var c in g.Checks)
                {
                    var detail = c.Detail is not null ? $": {c.Detail}" : "";
                    Console.WriteLine($"  [{c.Status}] {c.Name}{detail}");
                }
            }
            for (var j = 0; j < g.Occurrences.Count; j++)
            {
                var o = g.Occurrences[j];
                var path = o.FilePath.Replace('\\', '/');
                Console.WriteLine($"  {j + 1}. {path}:{o.StartLine}-{o.EndLine} in {o.ContainingType}.{o.ContainingMember}");
            }
            Console.WriteLine();
        }
    }
}
