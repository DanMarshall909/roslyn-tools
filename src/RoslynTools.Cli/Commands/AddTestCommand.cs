using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class AddTestCommand : IRtCommand
{
    private AddTestCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-test",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["at"],
        WhenToUse: "Appending xUnit [Fact] stub methods to an existing test class. Two modes: legacy (--class with --name or --acs) adds to arbitrary test classes; command-based (--command with --scenario/--outcome or --before/--after) scaffolds organized test files by rt command category.",
        Examples:
        [
            "rt refactor add-test --class BookingServiceTests --name Returns_404_When_Not_Found --solution RoslynTools.slnx",
            "rt refactor add-test --class OrderTests --acs \"Returns total;Applies discount\" --nested-class WhenCalculatingTotal",
            "rt refactor add-test --command ExtractMethod --scenario \"source file as first occurrence\" --outcome \"call site updated\" --concern SourceFileCallSite --solution RoslynTools.slnx",
            "rt refactor add-test --command Rename --concern CrossFileRefs --before before.cs --after after.cs --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        // Legacy mode options
        var classOpt = new Option<string?>("--class", "Test class name to add methods to");
        var nameOpt = new Option<string?>("--name", "Method name for single-test mode");
        var acsOpt = new Option<string?>("--acs", "Semicolon-separated acceptance criteria (AC mode)");
        var nestedClassOpt = new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");

        // New command-based mode options
        var commandOpt = new Option<string?>("--command", "rt command name to scaffold tests for (e.g., ExtractMethod)");
        var scenarioOpt = new Option<string?>("--scenario", "Scenario/precondition description (e.g., 'source file as first occurrence')");
        var outcomeOpt = new Option<string?>("--outcome", "Expected outcome description (e.g., 'updates call site')");
        var concernOpt = new Option<string?>("--concern", "Trait grouping for test organization (e.g., 'SourceFileCallSite')");
        var beforeOpt = new Option<string?>("--before", "File with starting code for transformation tests");
        var afterOpt = new Option<string?>("--after", "File with ending code for transformation tests");

        // Common options
        var fileOpt = new Option<string?>("--file", "Path to the .cs file containing the test class");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-test", "Append xUnit test stubs to an existing test class");
        cmd.AddAlias("at");
        cmd.AddAll(classOpt, nameOpt, acsOpt, nestedClassOpt, commandOpt, scenarioOpt, outcomeOpt, concernOpt, beforeOpt, afterOpt, fileOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async ctx =>
        {
            var className = ctx.ParseResult.GetValueForOption(classOpt);
            var name = ctx.ParseResult.GetValueForOption(nameOpt);
            var acs = ctx.ParseResult.GetValueForOption(acsOpt);
            var nestedClass = ctx.ParseResult.GetValueForOption(nestedClassOpt);
            var command = ctx.ParseResult.GetValueForOption(commandOpt);
            var scenario = ctx.ParseResult.GetValueForOption(scenarioOpt);
            var outcome = ctx.ParseResult.GetValueForOption(outcomeOpt);
            var concern = ctx.ParseResult.GetValueForOption(concernOpt);
            var before = ctx.ParseResult.GetValueForOption(beforeOpt);
            var after = ctx.ParseResult.GetValueForOption(afterOpt);
            var file = ctx.ParseResult.GetValueForOption(fileOpt);
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);

            await ExecuteAsync(className, name, acs, nestedClass, command, scenario, outcome, concern, before, after, file, solution, dryRun);
        });

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string? className,
        string? name,
        string? acs,
        string? nestedClass,
        string? command,
        string? scenario,
        string? outcome,
        string? concern,
        string? beforePath,
        string? afterPath,
        string? filePath,
        string? solutionPath,
        bool dryRun)
    {
        // Command-based mode
        if (command != null)
        {
            if (concern == null)
            {
                RtLog.Error("Error: --concern is required with --command mode.");
                Environment.ExitCode = 1;
                return;
            }

            var hasScenarioOutcome = scenario != null || outcome != null;
            var hasBeforeAfter = beforePath != null || afterPath != null;

            if (!hasScenarioOutcome && !hasBeforeAfter)
            {
                RtLog.Error("Error: either --scenario and --outcome, or --before and --after is required with --command mode.");
                Environment.ExitCode = 1;
                return;
            }

            if (hasScenarioOutcome && hasBeforeAfter)
            {
                RtLog.Error("Error: --scenario/--outcome and --before/--after are mutually exclusive.");
                Environment.ExitCode = 1;
                return;
            }

            var commandInfo = ResolveCommandInfo(command);
            if (commandInfo == null)
            {
                RtLog.Error("Command '{CommandName}' not found in registry.", command);
                Environment.ExitCode = 1;
                return;
            }

            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
            using (workspace)
            {
                await AddTestService.RunCommandBasedAsync(sln, commandInfo, scenario, outcome, concern, beforePath, afterPath, dryRun);
            }
        }
        // Legacy mode
        else
        {
            if (className == null)
            {
                RtLog.Error("Error: either --class (legacy mode) or --command (command-based mode) is required.");
                Environment.ExitCode = 1;
                return;
            }

            if (name == null && acs == null)
            {
                RtLog.Error("Error: either --name or --acs is required in legacy mode.");
                Environment.ExitCode = 1;
                return;
            }

            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
            using (workspace)
            {
                await AddTestService.RunAsync(sln, className, name, acs, nestedClass, filePath, dryRun);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC → C# identifier conversion (delegates to Core; kept here for tests)
    // ──────────────────────────────────────────────────────────────────────────

    internal static string AcToMethodName(string ac) => AddTestService.AcToMethodName(ac);

    // ──────────────────────────────────────────────────────────────────────────
    //  Command registry resolution
    // ──────────────────────────────────────────────────────────────────────────

    private static RtCommandInfo? ResolveCommandInfo(string commandName)
    {
        var entry = App.Commands
            .FirstOrDefault(e =>
                e.Info.Name == commandName ||
                (e.Info.Aliases?.Contains(commandName) ?? false));

        return entry.Info ?? null;
    }
}
