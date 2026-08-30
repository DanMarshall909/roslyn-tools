using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding test commands to the project.
/// </summary>
internal sealed class AddTestCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly AddTestOperation _operation;

    public AddTestCommand(
        DaemonRpcRecoveryCoordinator daemon,
        AddTestOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => AddTestCommandDefinition.Info;

    public Command Build()
    {
        // Legacy mode options
        var classOpt = new Option<string?>("--class", "Test class name to add methods to");
        var nameOpt = new Option<string?>("--name", "Method name for single-test mode");
        var acsOpt = new Option<string?>("--acs", "Semicolon-separated acceptance criteria (AC mode)");
        var nestedClassOpt =
            new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");

        // New command-based mode options
        var commandOpt =
            new Option<string?>("--command", "rt command name to scaffold tests for (e.g., ExtractMethod)");
        var scenarioOpt = new Option<string?>("--scenario",
            "Scenario/precondition description (e.g., 'source file as first occurrence')");
        var outcomeOpt = new Option<string?>("--outcome", "Expected outcome description (e.g., 'updates call site')");
        var concernOpt = new Option<string?>("--concern",
            "Trait grouping for test organization (e.g., 'SourceFileCallSite')");
        var beforeOpt = new Option<string?>("--before", "File with starting code for transformation tests");
        var afterOpt = new Option<string?>("--after", "File with ending code for transformation tests");

        // Common options
        var fileOpt = new Option<string?>("--file", "Path to the .cs file containing the test class");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-test", "Append xUnit test stubs to an existing test class");
        cmd.AddAlias("at");
        cmd.AddAll(classOpt, nameOpt, acsOpt, nestedClassOpt, commandOpt, scenarioOpt, outcomeOpt, concernOpt,
            beforeOpt, afterOpt, fileOpt, solutionOpt, dryRunOpt);

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

            var mode = command is null ? AddTestMode.Legacy : AddTestMode.Command;
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new AddTestRequest(
                    mode,
                    ClassName: className,
                    Name: name,
                    AcceptanceCriteria: acs,
                    NestedClass: nestedClass,
                    Command: command,
                    Scenario: scenario,
                    Outcome: outcome,
                    Concern: concern,
                    BeforePath: before,
                    AfterPath: after,
                    FilePath: file,
                    Solution: solution,
                    DryRun: dryRun),
                ctx);
        });

        return cmd;
    }

}
