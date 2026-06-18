using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Provides an outline of the solution's structure and elements.
/// </summary>
internal sealed class OutlineCommand : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly OutlineService _outline;
    public OutlineCommand(IWorkspaceSessionFactory sessionFactory, OutlineService outline)
    {
        _outline = outline;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "outline",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Getting a JSON summary of all types and member signatures in a file or namespace without reading the raw source. The default is a compact per-type view { name, kind, signatures: [...] } with no line numbers (stable across edits) — ideal for agents orienting in an unfamiliar module or diffing public surface area across branches. Use --with-locations for the legacy verbose grouped-members shape with line ranges, or --summary for a one-entry-per-type overview (fqn, kind, line range, optional XML doc description).",
        Examples:
        [
            "rt inspect outline src/backend/TourStore.Api/Controllers/Admin/AdminController.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect outline ProjectX.Core.Services --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect outline RoslynTools.Commands --solution RoslynTools.slnx",
            "rt inspect outline \"src/**/*.cs\" --solution RoslynTools.slnx",
            "rt inspect outline \"RoslynTools.*\" --solution RoslynTools.slnx",
            "rt inspect outline --regex \"Service\\.cs$\" --solution RoslynTools.slnx",
            "rt inspect outline src/RoslynTools/Core --solution RoslynTools.slnx --summary",
            "rt inspect outline RoslynTools.Core --solution RoslynTools.slnx --summary",
            "rt inspect outline src/RoslynTools/Core/SymbolLocator.cs --solution RoslynTools.slnx --with-locations"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public Command Build()
    {
        var targetArg = new Argument<string?>("target", () => null,
            "File path, directory, glob pattern, or namespace to outline");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var regexOpt = new Option<string?>("--regex", "Regex pattern matched against solution file paths");
        var summaryOpt = new Option<bool>("--summary",
            "Compact mode: one entry per top-level type (fqn, kind, lines, description if present); no member detail");
        var withLocationsOpt = new Option<bool>("--with-locations",
            "Legacy verbose mode: grouped members with per-member line ranges, instead of the compact signature array");

        var cmd = new Command("outline",
            "List types as a compact signature array (or verbose with --with-locations) in a file, glob, namespace, or regex (JSON)")
        {
            targetArg, solutionOpt, regexOpt, summaryOpt, withLocationsOpt
        };

        cmd.SetHandler(async context =>
        {
            var target = context.ParseResult.GetValueForArgument(targetArg);
            var solution = context.ParseResult.GetValueForOption(solutionOpt);
            var regex = context.ParseResult.GetValueForOption(regexOpt);
            var summary = context.ParseResult.GetValueForOption(summaryOpt);
            var withLocations = context.ParseResult.GetValueForOption(withLocationsOpt);

            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                List<object> result;

                if (regex is not null)
                {
                    result = await _outline.OutlineRegexAsync(sln, regex, summary, withLocations);
                }
                else if (target?.EndsWith(".*", StringComparison.Ordinal) == true
                         && !target.Contains('/') && !target.Contains('\\'))
                {
                    result = await _outline.OutlineNamespaceAsync(sln, target, summary, withLocations);
                }
                else if (target is not null && (target.Contains('*') || target.Contains('?')))
                {
                    result = await _outline.OutlineGlobAsync(sln, target, summary, withLocations);
                }
                else if (target is not null && Directory.Exists(target))
                {
                    result = await _outline.OutlineDirectoryAsync(sln, target, summary, withLocations);
                }
                else if (target is not null && (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                                Path.Exists(target)))
                {
                    result = await _outline.OutlineFileAsync(sln, target, summary, withLocations);
                }
                else if (target is not null)
                {
                    result = await _outline.OutlineNamespaceAsync(sln, target, summary, withLocations);
                }
                else
                {
                    RtLog.Error("specify a target (file, directory, glob, or namespace) or --regex <pattern>");
                    context.ExitCode = 1;
                    return;
                }

                RtResultRenderer.Write(RtResult<List<object>>.Ok(result));
            }
        });

        return cmd;
    }
}
