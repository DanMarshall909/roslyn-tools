using System.CommandLine;

namespace RoslynTools.Commands;

internal static class AddTestCommand
{
    internal static Command Build()
    {
        var classOpt = new Option<string>("--class", "Name of the test class to add tests to") { IsRequired = true };
        var nameOpt = new Option<string?>("--name", "Single test method name to add");
        var acsOpt = new Option<string?>("--acs", "Semicolon-separated acceptance criteria to generate test stubs for");
        var nestedClassOpt = new Option<string?>("--nested-class", "Wrap generated tests in a nested class with this name");
        var fileOpt = new Option<string?>("--file", "Path to the test .cs file (auto-detected if omitted)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx (auto-detected if omitted)");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");

        var cmd = new Command("add-test", "Add one or more [Fact] test stubs to an existing test class");
        cmd.AddAlias("at");
        cmd.Add(classOpt);
        cmd.Add(nameOpt);
        cmd.Add(acsOpt);
        cmd.Add(nestedClassOpt);
        cmd.Add(fileOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (className, name, acs, nestedClass, file, solution, dryRun) =>
        {
            await ExecuteAsync(className, name, acs, nestedClass, solution, dryRun);
        }, classOpt, nameOpt, acsOpt, nestedClassOpt, fileOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Adds [Fact] test stubs to an existing test class.
    /// Single mode: adds one method with the given <paramref name="testName"/>.
    /// AC mode: parses <paramref name="acs"/> (semicolon-separated) and generates a stub per AC.
    /// </summary>
    internal static Task ExecuteAsync(
        string className,
        string? testName,
        string? acs,
        string? nestedClass,
        string? solutionPath,
        bool dryRun)
    {
        throw new NotImplementedException("add-test is not yet implemented.");
    }
}
