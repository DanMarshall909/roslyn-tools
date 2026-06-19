using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies undocumented symbols in the current file
/// </summary>
internal sealed class FindUndocumentedCommand : IRtCommand, IInspectCommand
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
        var binder = RtCommandFlagBinder.Create()
            .WithSolution()
            .WithScope("Namespace prefix filter (e.g. RoslynTools.Core)");
        var membersOpt = new Option<bool>("--members", "Also report undocumented public methods and properties");
        var levelOpt = new Option<string>("--level",
            "Comma-separated accessibility levels to check: public, internal, protected, private, all (default: public)")
        {
            ArgumentHelpName = "levels"
        };
        levelOpt.SetDefaultValue("public");

        var cmd = new Command("find-undocumented", "Report types and members with no XML doc comment");
        cmd.AddAlias("undoc");
        cmd.Add(membersOpt);
        cmd.Add(levelOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var members = ctx.ParseResult.GetValueForOption(membersOpt);
            var level = ctx.ParseResult.GetValueForOption(levelOpt);
            var levels = level!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var accessibilityKinds = FindUndocumentedService.ParseAccessibilityLevels(levels);
            var req = binder.Bind(ctx.ParseResult, f => new FindUndocumentedCliRequest(f.SolutionPath, f.Scope));

            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(req.SolutionPath);
            using (workspace)
            {
                var symbols = await _findUndocumented
                    .AnalyzeAsync(sln, members, req.Scope, accessibilityKinds);
                RtResultRenderer.Write(RtResult<FindUndocumentedResult>.Ok(FindUndocumentedResult.From(symbols, sln.FilePath)));
            }
        });

        return cmd;
    }

    private sealed record FindUndocumentedCliRequest(
        string? SolutionPath,
        string? Scope) : ISupportsSolutionOption, ISupportsScopeOption;
}
