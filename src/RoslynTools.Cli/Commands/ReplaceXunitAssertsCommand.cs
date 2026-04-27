using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class ReplaceXunitAssertsCommand : IRtCommand
{
    private ReplaceXunitAssertsCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "replace-xunit-asserts",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["rxa"],
        WhenToUse: "Migrating Xunit Assert calls in tests to Shouldly's fluent assertion style. Defaults to review mode; pass --apply to write changes.",
        Examples:
        [
            "rt refactor replace-xunit-asserts --solution RoslynTools.slnx",
            "rt refactor rxa --apply --solution C:\\code\\app\\App.sln",
            "rt refactor replace-xunit-asserts --dry-run --solution C:\\code\\app\\App.sln",
        ],
        KnownIssues:
        [
            "Only common Assert calls are converted automatically; unsupported assertions are left unchanged.",
            "Assert overloads that include comparers, precision, inspectors, or custom messages are left unchanged.",
            "The command adds `using Shouldly;` to files with replacements and leaves existing Xunit imports in place for attributes such as [Fact].",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var applyOpt = new Option<bool>("--apply", "Write changes to disk. Without this, the command prints a review summary.");
        var reviewOpt = new Option<bool>("--review", "Summarize matching assert call sites without writing files or printing a full diff. This is the default mode.");

        var cmd = new Command("replace-xunit-asserts", "Replace common Xunit Assert calls with Shouldly assertions");
        cmd.AddAlias("rxa");
        cmd.AddAll(solutionOpt, dryRunOpt, applyOpt, reviewOpt);

        cmd.SetHandler(async (solution, dryRun, apply, review) =>
        {
            await ExecuteAsync(solution, dryRun, apply, review);
        }, solutionOpt, dryRunOpt, applyOpt, reviewOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string? solutionPath, bool dryRun, bool apply, bool review)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await ExecuteCoreAsync(solution, dryRun, apply, review);
        }
    }

    internal static async Task ExecuteCoreAsync(
        Microsoft.CodeAnalysis.Solution solution,
        bool dryRun,
        bool apply = false,
        bool review = false)
    {
        if (review || (!apply && !dryRun))
        {
            await ReviewAsync(solution);
            return;
        }

        await ReplaceXunitAssertsCore.RunAsync(solution, dryRun);
    }

    private static async Task ReviewAsync(Microsoft.CodeAnalysis.Solution solution)
    {
        var review = await ReplaceXunitAssertsCore.AnalyzeAsync(solution);
        var entries = review.Entries;

        RtConsole.WriteLine(RtOutputContext.Result, $"xunit assert review: {entries.Count} call site(s)");

        foreach (var group in entries.GroupBy(e => e.FilePath).OrderBy(g => g.Key, StringComparer.Ordinal).Take(20))
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"  {group.Key}: {group.Count()}");
            foreach (var entry in group.Take(5))
                RtConsole.WriteLine(RtOutputContext.Result, $"    line {entry.Line}: {entry.Original} -> {entry.Replacement}");
        }
    }
}
