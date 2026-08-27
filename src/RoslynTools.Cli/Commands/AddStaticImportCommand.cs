using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding static imports to the project.
/// </summary>
internal sealed class AddStaticImportCommand(IWorkspaceSessionFactory sessionFactory, ImportDirectiveService importDirective) : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly ImportDirectiveService _importDirective = importDirective;
    public static RtCommandInfo Info { get; } = new(
        "add-static-import",
        true,
        false,
        "refactor",
        Aliases: ["asi"],
        WhenToUse:
        "Adding a C# `using static` directive to a specific file so static members can be referenced without qualifying the containing type. Prefer a narrow file target over solution-wide static imports. Pass --simplify to also strip the now-redundant `<Type>.` qualifiers from the file body in one pass.",
        Examples:
        [
            "rt refactor add-static-import --file tests/MathTests.cs --type Xunit.Assert --solution C:\\code\\app\\App.sln",
            "rt refactor asi --file src/App/Program.cs --type System.Console --dry-run --solution RoslynTools.slnx",
            "rt refactor add-static-import --file src/Commands/FooCommand.cs --type RoslynTools.Core.RtLog --solution RoslynTools.slnx",
            "rt refactor asi --file src/App/Program.cs --type System.Console --simplify --solution RoslynTools.slnx",
            "rt refactor asi --file src/App/Program.cs --type System.Console --simplify-all-static-imports --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateStringOption("--file", "Path to the .cs file to update");
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Fully qualified type to import statically");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();
        var simplifyOpt = new Option<bool>("--simplify",
            "After adding the directive, strip the `<Type>.` qualifier from references to --type in the same file.");
        var simplifyAllOpt = new Option<bool>("--simplify-all-static-imports",
            "Implies --simplify. Also strips qualifiers for every other type already imported via `using static` in the file.");

        var cmd = new Command("add-static-import", "Add a using static directive to a file");
        cmd.AddAlias("asi");
        cmd.AddAll(fileOpt, typeOpt, solutionOpt, dryRunOpt, simplifyOpt, simplifyAllOpt);

        cmd.SetHandler(async (file, type, solution, dryRun, simplify, simplifyAll) =>
        {
            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                await _importDirective.AddStaticUsingAsync(sln, file, type, dryRun, simplify, simplifyAll);
            }
        }, fileOpt, typeOpt, solutionOpt, dryRunOpt, simplifyOpt, simplifyAllOpt);

        return cmd;
    }
}
