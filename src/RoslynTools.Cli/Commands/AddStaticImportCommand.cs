using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class AddStaticImportCommand : IRtCommand
{
    private AddStaticImportCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-static-import",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["asi"],
        WhenToUse: "Adding a C# `using static` directive to a specific file so static members can be referenced without qualifying the containing type. Prefer a narrow file target over solution-wide static imports.",
        Examples:
        [
            "rt refactor add-static-import --file tests/MathTests.cs --type Xunit.Assert --solution C:\\code\\app\\App.sln",
            "rt refactor asi --file src/App/Program.cs --type System.Console --dry-run --solution RoslynTools.slnx",
            "rt refactor add-static-import --file src/Commands/FooCommand.cs --type RoslynTools.Core.RtLog --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file to update") { IsRequired = true };
        var typeOpt = new Option<string>("--type", "Fully qualified type to import statically") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-static-import", "Add a using static directive to a file");
        cmd.AddAlias("asi");
        cmd.AddAll(fileOpt, typeOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (file, type, solution, dryRun) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                await ImportDirectiveService.AddStaticUsingAsync(sln, file, type, dryRun);
            }
        }, fileOpt, typeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
