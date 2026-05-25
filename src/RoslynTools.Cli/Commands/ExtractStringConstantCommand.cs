using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Extracts string literals from the current file
/// </summary>
internal sealed class ExtractStringConstantCommand : IRtCommand, IRefactorCommand, IMutatingCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly MagicStringService _magicString;
    public ExtractStringConstantCommand(IWorkspaceSessionFactory sessionFactory, MagicStringService magicString)
    {
        _magicString = magicString;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "extract-string-constant",
        true,
        false,
        "refactor",
        Aliases: ["esc"],
        WhenToUse:
        "Replacing a repeated string literal inside one containing type with a named private const string. Use find-magic-strings first to locate the file and line.",
        Examples:
        [
            "rt refactor extract-string-constant --file src/App/Foo.cs --line 42 --name DefaultStatus --solution C:\\code\\app\\App.sln",
            "rt refactor esc --file tests/FooTests.cs --line 18 --name ExpectedMessage --dry-run --solution RoslynTools.slnx",
            "rt refactor extract-string-constant --file src/Domain/Order.cs --line 27 --name SubmittedStatus --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateStringOption("--file", "Path to the .cs file containing the literal");
        var lineOpt = new Option<int>("--line", "Line number of the string literal to extract") { IsRequired = true };
        var nameOpt = CommandOptionFactory.CreateStringOption("--name", "Constant name to create");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("extract-string-constant", "Extract a string literal into a private const string");
        cmd.AddAlias("esc");
        cmd.AddAll(fileOpt, lineOpt, nameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (file, line, name, solution, dryRun) =>
        {
            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                await _magicString.ExtractConstantAsync(sln, file, line, name, dryRun);
            }
        }, fileOpt, lineOpt, nameOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
