using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class ConvertConsoleLoggingCommand : IRtCommand
{
    private ConvertConsoleLoggingCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "convert-console-logging",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["ccl"],
        WhenToUse: "Migrating Console.Write/WriteLine and Console.Error.Write/WriteLine calls to logger calls while preserving messages as structured templates where possible. Use this after the target logger dependency already exists, or pair it with add-dependency.",
        Examples:
        [
            "rt convert-console-logging --solution C:\\code\\libraries\\ProjectX.Libraries.sln --review",
            "rt ccl --logger _logger --solution RoslynTools.slnx",
            "rt convert-console-logging --stream all --logger _log --information-method Info --warning-method Warn --error-method Error --solution C:\\code\\app\\App.sln",
        ],
        KnownIssues:
        [
            "The command rewrites call sites only; it does not inject the logger dependency.",
            "Ambiguous non-template expressions are wrapped as {Value} and flagged by --review.",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var loggerOpt = new Option<string>("--logger", () => "_logger", "Logger expression to call");
        var informationMethodOpt = new Option<string>("--information-method", () => "LogInformation", "Method used for Console.Write/WriteLine");
        var warningMethodOpt = new Option<string>("--warning-method", () => "LogWarning", "Method used when warning-level text is inferred");
        var errorMethodOpt = new Option<string>("--error-method", () => "LogError", "Method used for Console.Error.Write/WriteLine");
        var streamOpt = new Option<string>("--stream", () => "stderr", "Console stream to rewrite: all, stdout, or stderr");
        var excludeOpt = new Option<string[]>("--exclude", "Path fragment to exclude; repeat for multiple exclusions")
        {
            AllowMultipleArgumentsPerToken = true,
        };
        var noInferLevelOpt = new Option<bool>("--no-infer-level", "Disable warning/info/error inference and map stderr to the error method");
        var reviewOpt = new Option<bool>("--review", "Summarize matching call sites without writing files or printing a full diff");

        var cmd = new Command("convert-console-logging", "Convert Console.Write/WriteLine calls to logger calls");
        cmd.AddAlias("ccl");
        cmd.AddAll(solutionOpt, dryRunOpt, loggerOpt, informationMethodOpt, warningMethodOpt, errorMethodOpt, streamOpt, excludeOpt, noInferLevelOpt, reviewOpt);

        cmd.SetHandler(async context =>
        {
            await ExecuteAsync(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(dryRunOpt),
                context.ParseResult.GetValueForOption(loggerOpt)!,
                context.ParseResult.GetValueForOption(informationMethodOpt)!,
                context.ParseResult.GetValueForOption(warningMethodOpt)!,
                context.ParseResult.GetValueForOption(errorMethodOpt)!,
                context.ParseResult.GetValueForOption(streamOpt)!,
                context.ParseResult.GetValueForOption(excludeOpt) ?? [],
                !context.ParseResult.GetValueForOption(noInferLevelOpt),
                context.ParseResult.GetValueForOption(reviewOpt));
        });

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string? solutionPath,
        bool dryRun,
        string loggerExpression,
        string informationMethod,
        string warningMethod,
        string errorMethod,
        string stream,
        string[] excludePaths,
        bool inferLevel,
        bool review)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await ExecuteCoreAsync(solution, dryRun, loggerExpression, informationMethod, warningMethod, errorMethod, stream, excludePaths, inferLevel, review);
        }
    }

    internal static Task ExecuteCoreAsync(
        Microsoft.CodeAnalysis.Solution solution,
        bool dryRun,
        string loggerExpression = "_logger",
        string informationMethod = "LogInformation",
        string warningMethod = "LogWarning",
        string errorMethod = "LogError",
        string stream = "all",
        string[]? excludePaths = null,
        bool inferLevel = true,
        bool review = false)
    {
        var options = new ConsoleLoggingOptions(
                loggerExpression,
                informationMethod,
                warningMethod,
                errorMethod,
                ParseStream(stream),
                excludePaths ?? [],
                inferLevel);

        if (review)
            return ReviewAsync(solution, options);

        return ConsoleLoggingCore.RunAsync(solution, options, dryRun);
    }

    private static ConsoleLoggingStream ParseStream(string stream) =>
        stream.ToLowerInvariant() switch
        {
            "all" => ConsoleLoggingStream.All,
            "stdout" => ConsoleLoggingStream.Stdout,
            "stderr" => ConsoleLoggingStream.Stderr,
            _ => throw new ArgumentException("Stream must be one of: all, stdout, stderr.", nameof(stream)),
        };

    private static async Task ReviewAsync(Microsoft.CodeAnalysis.Solution solution, ConsoleLoggingOptions options)
    {
        var review = await ConsoleLoggingCore.AnalyzeAsync(solution, options);
        var entries = review.Entries;

        RtConsole.WriteLine(RtOutputContext.Result, $"console logging review: {entries.Count} call site(s)");
        foreach (var group in entries.GroupBy(e => e.Stream).OrderBy(g => g.Key))
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"  {group.Key}: {group.Count()}");
            foreach (var levelGroup in group.GroupBy(e => e.Level).OrderBy(g => g.Key))
                RtConsole.WriteLine(RtOutputContext.Result, $"    {levelGroup.Key}: {levelGroup.Count()}");
        }

        var reviewCount = entries.Count(e => e.NeedsReview);
        if (reviewCount > 0)
            RtConsole.WriteLine(RtOutputContext.Result, $"  needs-review: {reviewCount}");

        foreach (var entry in entries.Where(e => e.NeedsReview).Take(20))
        {
            RtConsole.WriteLine(
                RtOutputContext.Result,
                $"  REVIEW {entry.FilePath}({entry.Line}): {entry.Template}");
        }
    }
}
