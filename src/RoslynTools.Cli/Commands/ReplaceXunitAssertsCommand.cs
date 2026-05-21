using System.CommandLine;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Replaces XUnit assertions with alternative testing methods.
/// </summary>
internal sealed class ReplaceXunitAssertsCommand : IRtCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly ReplaceXunitAssertsService _replace;
    public ReplaceXunitAssertsCommand(IWorkspaceSessionFactory sessionFactory, ReplaceXunitAssertsService replace)
    {
        _replace = replace;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "replace-xunit-asserts",
        true,
        false,
        "refactor",
        Aliases: ["rxa"],
        WhenToUse:
        "Migrating Xunit Assert calls in tests to Shouldly's fluent assertion style. Defaults to review mode; pass --apply to write changes.",
        Examples:
        [
            "rt refactor replace-xunit-asserts --solution RoslynTools.slnx",
            "rt refactor rxa --apply --solution C:\\code\\app\\App.sln",
            "rt refactor replace-xunit-asserts --dry-run --solution C:\\code\\app\\App.sln"
        ],
        KnownIssues:
        [
            "Only common Assert calls are converted automatically; unsupported assertions are left unchanged.",
            "Assert overloads that include comparers, precision, inspectors, or custom messages are left unchanged.",
            "The command adds `using Shouldly;` to files with replacements and leaves existing Xunit imports in place for attributes such as [Fact]."
        ]
    );

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var applyOpt = new Option<bool>("--apply",
            "Write changes to disk. Without this, the command prints a review summary.");
        var reviewOpt = new Option<bool>("--review",
            "Summarize matching assert call sites without writing files or printing a full diff. This is the default mode.");

        var cmd = new Command("replace-xunit-asserts", "Replace common Xunit Assert calls with Shouldly assertions");
        cmd.AddAlias("rxa");
        cmd.AddAll(solutionOpt, dryRunOpt, applyOpt, reviewOpt);

        cmd.SetHandler(
            async (solution, dryRun, apply, review) => { await ExecuteAsync(solution, dryRun, apply, review); },
            solutionOpt, dryRunOpt, applyOpt, reviewOpt);

        return cmd;
    }

    /// <summary>
    /// Asynchronously replaces XUnit assertions with alternative testing methods.
    /// </summary>
    internal async Task ExecuteAsync(string? solutionPath, bool dryRun, bool apply, bool review)
    {
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await ExecuteCoreAsync(solution, dryRun, apply, review);
        }
    }

    /// <summary>
    /// Asynchronously replaces XUnit assertions with alternative testing methods using core analysis.
    /// </summary>
    internal async Task ExecuteCoreAsync(
        Solution solution,
        bool dryRun,
        bool apply = false,
        bool review = false)
    {
        if (review || (!apply && !dryRun))
        {
            await ReviewAsync(solution);
            return;
        }

        await _replace.RunAsync(solution, dryRun);
    }

    private async Task ReviewAsync(Solution solution)
    {
        var review = await _replace.AnalyzeAsync(solution);
        var entries = review.Entries;
        var output = new StringBuilder();

        output.AppendLine(FormattableString.Invariant($"xunit assert review: {entries.Count} call site(s)"));

        foreach (var group in entries.GroupBy(e => e.FilePath).OrderBy(g => g.Key, StringComparer.Ordinal).Take(20))
        {
            output.AppendLine(FormattableString.Invariant($"  {group.Key}: {group.Count()}"));
            foreach (var entry in group.Take(5))
                output.AppendLine(
                    FormattableString.Invariant($"    line {entry.Line}: {entry.Original} -> {entry.Replacement}"));
        }

        RtResultRenderer.WriteText(RtResult<string>.Ok(output.ToString()));
    }
}
