using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all occurrences of a given pattern in the current file
/// </summary>
internal sealed class FindPatternCommand : IRtCommand, IStructuredOutputCommand
{
    private FindPatternCommand()
    {
    }

    // Use the shared options so generic type names like List<int> render as `<` not `<`.
    private static JsonSerializerOptions JsonOptions => SymbolFormatter.DefaultJsonOptions;

    public static RtCommandInfo Info { get; } = new(
        "find-pattern",
        false,
        true,
        "inspect",
        Aliases: ["fp"],
        WhenToUse:
        "Find every location in the solution that structurally matches a given line range — same SyntaxKind tree shape, only identifier and literal values differ. Useful for spotting duplicated logic even when variable names differ.",
        Examples:
        [
            "rt inspect find-pattern --file src/Services/BookingService.cs --start-line 42 --end-line 50 --solution RoslynTools.slnx",
            "rt inspect fp --file src/Controllers/AdminController.cs --start-line 110 --end-line 125 --scope RoslynTools.Core --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public static Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file containing the pattern.")
        { IsRequired = true };
        var startLineOpt = new Option<int>("--start-line", "First line of the pattern range (1-based).")
        { IsRequired = true };
        var endLineOpt = new Option<int>("--end-line", "Last line of the pattern range (1-based, inclusive).")
        { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx. Auto-detected if omitted.");
        var scopeOpt = new Option<string?>("--scope",
            "Filter matches to documents in this namespace prefix or project name.");

        var cmd = new Command("find-pattern",
            "Find all solution locations structurally matching a given statement range.");
        cmd.AddAlias("fp");
        cmd.AddAll(fileOpt, startLineOpt, endLineOpt, solutionOpt, scopeOpt);

        cmd.SetHandler(async ctx =>
        {
            var file = ctx.ParseResult.GetValueForOption(fileOpt)!;
            var startLine = ctx.ParseResult.GetValueForOption(startLineOpt);
            var endLine = ctx.ParseResult.GetValueForOption(endLineOpt);
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var scope = ctx.ParseResult.GetValueForOption(scopeOpt);
            var sp = ctx.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<IWorkspaceSessionFactory>() ?? IWorkspaceSessionFactory.CreateDefault();

            var sln = await loader.GetSolutionAsync(solution);
            RtLog.Info("Searching for pattern at {File}:{StartLine}-{EndLine}...", file, startLine, endLine);
            List<PatternMatch> matches;
            try
            {
                matches = await PatternMatcher.FindMatchesAsync(sln, file, startLine, endLine, scope);
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            var output = new { matches };
            RtResultRenderer.Write(
                RtResult<object>.Ok(output),
                JsonOptions);
        });

        return cmd;
    }
}
