using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies undocumented symbols in the current file
/// </summary>
internal sealed class FindUndocumentedCommand : IRtCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly FindUndocumentedService _findUndocumented;
    public FindUndocumentedCommand(IWorkspaceSessionFactory sessionFactory, FindUndocumentedService findUndocumented)
    {
        _findUndocumented = findUndocumented;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "find-undocumented",
        false,
        false,
        "inspect",
        Aliases: ["undoc"],
        WhenToUse:
        "Finding types and members that have no XML documentation comment — the first step before bulk-generating docs with an LLM. Use --members to include methods and properties, not just types. Use --level to control which accessibility levels are checked (default: public).",
        Examples:
        [
            "rt inspect find-undocumented --solution RoslynTools.slnx",
            "rt inspect undoc --members --level internal --solution RoslynTools.slnx",
            "rt inspect undoc --members --level public,internal --scope RoslynTools.Core --solution RoslynTools.slnx",
            "rt inspect find-undocumented --level all --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var membersOpt = new Option<bool>("--members", "Also report undocumented public methods and properties");
        var scopeOpt = new Option<string?>("--scope", "Namespace prefix filter (e.g. RoslynTools.Core)");
        var levelOpt = new Option<string>("--level",
            "Comma-separated accessibility levels to check: public, internal, protected, private, all (default: public)")
        {
            ArgumentHelpName = "levels"
        };
        levelOpt.SetDefaultValue("public");

        var cmd = new Command("find-undocumented", "Report types and members with no XML doc comment");
        cmd.AddAlias("undoc");
        cmd.AddAll(solutionOpt, membersOpt, scopeOpt, levelOpt);

        cmd.SetHandler(async (solution, members, scope, level) =>
        {
            var levels = level.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var accessibilityKinds = FindUndocumentedService.ParseAccessibilityLevels(levels);

            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                var symbols = await _findUndocumented
                    .AnalyzeAsync(sln, members, scope, accessibilityKinds);
                RtResultRenderer.WriteOutput(RtResult<FindUndocumentedResult>.Ok(FindUndocumentedResult.From(symbols, sln.FilePath)));
            }
        }, solutionOpt, membersOpt, scopeOpt, levelOpt);

        return cmd;
    }
}
