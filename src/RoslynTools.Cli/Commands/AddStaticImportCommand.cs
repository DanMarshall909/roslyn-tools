using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding static imports to the project.
/// </summary>
internal sealed class AddStaticImportCommand : IRtCommand
{
    private AddStaticImportCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "add-static-import",
        true,
        false,
        "refactor",
        Aliases: ["asi"],
        WhenToUse:
        "Adding a C# `using static` directive to a specific file so static members can be referenced without qualifying the containing type. Prefer a narrow file target over solution-wide static imports.",
        Examples:
        [
            "rt refactor add-static-import --file tests/MathTests.cs --type Xunit.Assert --solution C:\\code\\app\\App.sln",
            "rt refactor asi --file src/App/Program.cs --type System.Console --dry-run --solution RoslynTools.slnx",
            "rt refactor add-static-import --file src/Commands/FooCommand.cs --type RoslynTools.Core.RtLog --solution RoslynTools.slnx"
        ]
    );

    public static Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateStringOption("--file", "Path to the .cs file to update");
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Fully qualified type to import statically");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-static-import", "Add a using static directive to a file");
        cmd.AddAlias("asi");
        cmd.AddAll(fileOpt, typeOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (file, type, solution, dryRun) =>
        {
            var (workspace, sln) = await RtServiceProvider.GetOrDefault(IWorkspaceSessionFactory.CreateDefault).LoadWorkspaceAsync(solution);
            using (workspace)
            {
                await RtServiceProvider.Get<ImportDirectiveService>().AddStaticUsingAsync(sln, file, type, dryRun);
            }
        }, fileOpt, typeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
