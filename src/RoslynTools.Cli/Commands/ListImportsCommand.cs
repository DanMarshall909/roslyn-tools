using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Lists all imported namespaces in the current file
/// </summary>
internal sealed class ListImportsCommand : IRtCommand, IAnalyzeCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly ImportDirectiveService _importDirective;
    public ListImportsCommand(IWorkspaceSessionFactory sessionFactory, ImportDirectiveService importDirective)
    {
        _importDirective = importDirective;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "list-imports",
        false,
        false,
        "analyze",
        Aliases: ["imports"],
        WhenToUse:
        "Auditing C# using directives, especially global using and using static directives, before consolidating imports or introducing static member imports.",
        Examples:
        [
            "rt analyze list-imports --kind global --solution RoslynTools.slnx",
            "rt analyze imports --kind static --solution C:\\code\\app\\App.sln",
            "rt analyze list-imports --kind all --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var kindOpt = new Option<string>("--kind", () => "all", "Filter: all, global, static, or alias");

        var cmd = new Command("list-imports", "List using directives, including global and static imports");
        cmd.AddAlias("imports");
        cmd.AddAll(solutionOpt, kindOpt);

        cmd.SetHandler(async (solution, kind) =>
        {
            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                var imports = await _importDirective.AnalyzeAsync(sln, kind);
                RtResultRenderer.Write(RtResult<ImportDirectiveResult>.Ok(ImportDirectiveResult.FromImports(imports)));
            }
        }, solutionOpt, kindOpt);

        return cmd;
    }
}
