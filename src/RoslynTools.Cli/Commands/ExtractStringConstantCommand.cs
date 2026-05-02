using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ExtractStringConstantCommand : IRtCommand
{
    private ExtractStringConstantCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "extract-string-constant",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["esc"],
        WhenToUse: "Replacing a repeated string literal inside one containing type with a named private const string. Use find-magic-strings first to locate the file and line.",
        Examples:
        [
            "rt refactor extract-string-constant --file src/App/Foo.cs --line 42 --name DefaultStatus --solution C:\\code\\app\\App.sln",
            "rt refactor esc --file tests/FooTests.cs --line 18 --name ExpectedMessage --dry-run --solution RoslynTools.slnx",
            "rt refactor extract-string-constant --file src/Domain/Order.cs --line 27 --name SubmittedStatus --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file containing the literal") { IsRequired = true };
        var lineOpt = new Option<int>("--line", "Line number of the string literal to extract") { IsRequired = true };
        var nameOpt = new Option<string>("--name", "Constant name to create") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("extract-string-constant", "Extract a string literal into a private const string");
        cmd.AddAlias("esc");
        cmd.AddAll(fileOpt, lineOpt, nameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (file, line, name, solution, dryRun) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                await MagicStringService.ExtractConstantAsync(sln, file, line, name, dryRun);
            }
        }, fileOpt, lineOpt, nameOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
