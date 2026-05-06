using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ConvertConsoleLoggingCommand : IRtCommand
{
    private ConvertConsoleLoggingCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "convert-console-logging",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["ccl"],
        WhenToUse: "Migrating Console.Write/WriteLine and Console.Error.Write/WriteLine calls to logger calls while preserving messages as structured templates where possible. Use this after the target logger dependency already exists, or pair it with add-dependency.",
        Examples:
        [
            "rt refactor convert-console-logging --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ccl --inject-logger --solution C:\\code\\app\\App.sln",
            "rt refactor ccl --inject-logger --apply --solution C:\\code\\app\\App.sln",
            "rt refactor convert-console-logging --stream all --logger _log --information-method Info --warning-method Warn --error-method Error --dry-run --solution C:\\code\\app\\App.sln",
        ],
        KnownIssues:
        [
            "Logger injection reuses add-dependency and only targets containing classes that have matching Console call sites.",
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
        var applyOpt = new Option<bool>("--apply", "Write changes to disk. Without this, the command prints a review summary.");
        var reviewOpt = new Option<bool>("--review", "Summarize matching call sites without writing files or printing a full diff. This is the default mode.");
        var injectLoggerOpt = new Option<bool>("--inject-logger", "Inject the logger dependency into each containing class before rewriting call sites");
        var loggerDependencyOpt = new Option<string>("--logger-dependency", () => "ILogger<{TypeName}>", "Logger dependency type template; supports {TypeName} and {TypeFullName}");
        var loggerFieldOpt = new Option<string>("--logger-field", () => "_logger", "Logger field name passed to add-dependency");

        var cmd = new Command("convert-console-logging", "Convert Console.Write/WriteLine calls to logger calls");
        cmd.AddAlias("ccl");
        cmd.AddAll(solutionOpt, dryRunOpt, loggerOpt, informationMethodOpt, warningMethodOpt, errorMethodOpt, streamOpt, excludeOpt, noInferLevelOpt, applyOpt, reviewOpt, injectLoggerOpt, loggerDependencyOpt, loggerFieldOpt);

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
                context.ParseResult.GetValueForOption(applyOpt),
                context.ParseResult.GetValueForOption(reviewOpt),
                context.ParseResult.GetValueForOption(injectLoggerOpt),
                context.ParseResult.GetValueForOption(loggerDependencyOpt)!,
                context.ParseResult.GetValueForOption(loggerFieldOpt)!);
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
        bool apply,
        bool review,
        bool injectLogger,
        string loggerDependency,
        string loggerField)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await ExecuteCoreAsync(solution, dryRun, loggerExpression, informationMethod, warningMethod, errorMethod, stream, excludePaths, inferLevel, apply, review, injectLogger, loggerDependency, loggerField);
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
        bool apply = false,
        bool review = false,
        bool injectLogger = false,
        string loggerDependency = "ILogger<{TypeName}>",
        string loggerField = "_logger")
    {
        var options = new ConsoleLoggingOptions(
                loggerExpression,
                informationMethod,
                warningMethod,
                errorMethod,
                ParseStream(stream),
                excludePaths ?? [],
                inferLevel);

        if (review || (!apply && !dryRun))
            return ReviewAsync(solution, options, injectLogger, loggerDependency);

        if (injectLogger)
            return ApplyWithLoggerInjectionAsync(solution, options, dryRun, loggerDependency, loggerField);

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

    private static async Task ApplyWithLoggerInjectionAsync(
        Microsoft.CodeAnalysis.Solution solution,
        ConsoleLoggingOptions options,
        bool dryRun,
        string loggerDependency,
        string loggerField)
    {
        var originalSolution = solution;
        var review = await ConsoleLoggingCore.AnalyzeAsync(solution, options);

        foreach (var target in GetInjectionTargets(review, loggerDependency))
        {
            var result = await new AddDependencyService().ApplyAsync(
                solution,
                target.TypeFullName,
                target.DependencyType,
                loggerField);
            solution = result.Solution;
        }

        solution = await ConsoleLoggingCore.ApplyAsync(solution, options);
        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private static async Task ReviewAsync(
        Microsoft.CodeAnalysis.Solution solution,
        ConsoleLoggingOptions options,
        bool injectLogger,
        string loggerDependency)
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

        if (injectLogger)
        {
            var targets = GetInjectionTargets(review, loggerDependency).ToList();
            RtConsole.WriteLine(RtOutputContext.Result, $"  logger injections: {targets.Count}");
            foreach (var target in targets.Take(20))
                RtConsole.WriteLine(RtOutputContext.Result, $"    {target.TypeFullName}: {target.DependencyType}");
        }

        foreach (var entry in entries.Where(e => e.NeedsReview).Take(20))
        {
            RtConsole.WriteLine(
                RtOutputContext.Result,
                $"  REVIEW {entry.FilePath}({entry.Line}): {entry.Template}");
        }
    }

    private static IEnumerable<LoggerInjectionTarget> GetInjectionTargets(
        ConsoleLoggingReview review,
        string dependencyTemplate)
    {
        return review.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.TypeName)
                && !string.IsNullOrWhiteSpace(e.TypeFullName))
            .GroupBy(e => e.TypeFullName!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var entry = g.First();
                return new LoggerInjectionTarget(
                    entry.TypeFullName!,
                    FormatDependencyType(dependencyTemplate, entry.TypeName!, entry.TypeFullName!));
            });
    }

    private static string FormatDependencyType(string template, string typeName, string typeFullName) =>
        template.Replace("{TypeName}", typeName, StringComparison.Ordinal)
            .Replace("{TypeFullName}", typeFullName, StringComparison.Ordinal);

    private sealed record LoggerInjectionTarget(string TypeFullName, string DependencyType);
}
