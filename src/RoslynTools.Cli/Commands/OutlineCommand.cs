using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Provides an outline of the solution's structure and elements.
/// </summary>
internal sealed class OutlineCommand : IRtCommand
{
    private OutlineCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "outline",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Getting a JSON summary of all types and member signatures in a file or namespace without reading the raw source. Useful for agents to orient themselves in an unfamiliar module before planning a refactor, or to diff the public surface area across branches. Use --summary for a compact one-entry-per-type view (fqn, kind, line range, optional XML doc description) — ideal for directory-wide orientation.",
        Examples:
        [
            "rt inspect outline src/backend/TourStore.Api/Controllers/Admin/AdminController.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect outline ProjectX.Core.Services --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect outline RoslynTools.Commands --solution RoslynTools.slnx",
            "rt inspect outline \"src/**/*.cs\" --solution RoslynTools.slnx",
            "rt inspect outline \"RoslynTools.*\" --solution RoslynTools.slnx",
            "rt inspect outline --regex \"Service\\.cs$\" --solution RoslynTools.slnx",
            "rt inspect outline src/RoslynTools/Core --solution RoslynTools.slnx --summary",
            "rt inspect outline RoslynTools.Core --solution RoslynTools.slnx --summary"
        ]
    );

    public static Command Build()
    {
        var targetArg = new Argument<string?>("target", () => null,
            "File path, directory, glob pattern, or namespace to outline");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var regexOpt = new Option<string?>("--regex", "Regex pattern matched against solution file paths");
        var summaryOpt = new Option<bool>("--summary",
            "Compact mode: one entry per top-level type (fqn, kind, lines, description if present); no member detail");

        var cmd = new Command("outline",
            "List all types and member signatures in a file, glob, namespace, or regex (JSON)")
        {
            targetArg, solutionOpt, regexOpt, summaryOpt
        };

        cmd.SetHandler(async (target, solution, regex, summary) =>
            {
                var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
                using (workspace)
                {
                    List<Dictionary<string, object?>> result;
                    var svc = RtServiceProvider.Get<OutlineService>();

                    if (regex is not null)
                    {
                        result = await svc.OutlineRegexAsync(sln, regex, summary);
                    }
                    else if (target?.EndsWith(".*", StringComparison.Ordinal) == true
                             && !target.Contains('/') && !target.Contains('\\'))
                    {
                        result = await svc.OutlineNamespaceAsync(sln, target, summary);
                    }
                    else if (target is not null && (target.Contains('*') || target.Contains('?')))
                    {
                        result = await svc.OutlineGlobAsync(sln, target, summary);
                    }
                    else if (target is not null && Directory.Exists(target))
                    {
                        result = await svc.OutlineDirectoryAsync(sln, target, summary);
                    }
                    else if (target is not null && (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                                    Path.Exists(target)))
                    {
                        result = await svc.OutlineFileAsync(sln, target, summary);
                    }
                    else if (target is not null)
                    {
                        result = await svc.OutlineNamespaceAsync(sln, target, summary);
                    }
                    else
                    {
                        RtLog.Error("specify a target (file, directory, glob, or namespace) or --regex <pattern>");
                        Environment.Exit(1);
                        return;
                    }

                    StructuredOutput.Write(result, SymbolFormatter.DefaultJsonOptions);
                }
            },
            targetArg, solutionOpt, regexOpt, summaryOpt);

        return cmd;
    }
}
