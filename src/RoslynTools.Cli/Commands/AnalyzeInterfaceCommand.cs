using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Models;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for analyzing an interface for issues.
/// </summary>
internal sealed class AnalyzeInterfaceCommand : IRtCommand
{
    private AnalyzeInterfaceCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "analyze-interface",
        false,
        true,
        "analyze",
        "Grouping interface consumers by the exact interface members they use so interface-segregation work can be driven by evidence instead of raw reference dumps. Use --scope to focus on one namespace or project, and --json for automation-friendly output.",
        [
            "rt analyze analyze-interface ProjectX.Core.IEmailSender --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze analyze-interface IEmailSender --scope ProjectX.Core --solution RoslynTools.slnx",
            "rt analyze analyze-interface ProjectX.Core.IEmailSender --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);

    public static Command Build()
    {
        var interfaceArg = new Argument<string>("interface", "Interface symbol to analyze");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var scopeOpt = new Option<string?>("--scope", "Project name or namespace prefix to restrict analysis");
        var includeTestsOpt = new Option<bool>("--include-tests", () => false, "Include test projects in results");
        var suggestOpt = new Option<bool>("--suggest", () => false,
            "Augment output with advisory minimal replacement interface suggestions per consumer");

        var cmd = new Command("analyze-interface", "Group interface consumers by the exact members they use");
        cmd.Add(interfaceArg);
        cmd.Add(solutionOpt);
        cmd.Add(scopeOpt);
        cmd.Add(includeTestsOpt);
        cmd.Add(suggestOpt);

        cmd.SetHandler(async (interfaceSpec, solution, scope, includeTests, suggest) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var report = await RtServiceProvider.Get<AnalyzeInterfaceService>().AnalyzeAsync(
                    sln,
                    interfaceSpec,
                    new AnalyzeInterfaceOptions(scope, includeTests, suggest));

                RtSerializer.Write(AnalyzeInterfaceResult.FromReport(report, suggest));
            }
        }, interfaceArg, solutionOpt, scopeOpt, includeTestsOpt, suggestOpt);

        return cmd;
    }
}
