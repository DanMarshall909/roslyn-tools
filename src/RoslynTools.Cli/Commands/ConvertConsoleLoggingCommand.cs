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
            "rt convert-console-logging --solution C:\\code\\libraries\\ProjectX.Libraries.sln --dry-run",
            "rt ccl --logger _logger --solution RoslynTools.slnx",
            "rt convert-console-logging --logger _log --information-method Info --error-method Error --solution C:\\code\\app\\App.sln",
        ],
        KnownIssues:
        [
            "The command rewrites call sites only; it does not inject the logger dependency.",
            "Ambiguous non-template expressions are wrapped as {Value} rather than inferred into detailed templates.",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var loggerOpt = new Option<string>("--logger", () => "_logger", "Logger expression to call");
        var informationMethodOpt = new Option<string>("--information-method", () => "LogInformation", "Method used for Console.Write/WriteLine");
        var errorMethodOpt = new Option<string>("--error-method", () => "LogError", "Method used for Console.Error.Write/WriteLine");

        var cmd = new Command("convert-console-logging", "Convert Console.Write/WriteLine calls to logger calls");
        cmd.AddAlias("ccl");
        cmd.AddAll(solutionOpt, dryRunOpt, loggerOpt, informationMethodOpt, errorMethodOpt);

        cmd.SetHandler(async (solution, dryRun, logger, informationMethod, errorMethod) =>
        {
            await ExecuteAsync(solution, dryRun, logger, informationMethod, errorMethod);
        }, solutionOpt, dryRunOpt, loggerOpt, informationMethodOpt, errorMethodOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string? solutionPath,
        bool dryRun,
        string loggerExpression,
        string informationMethod,
        string errorMethod)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await ExecuteCoreAsync(solution, dryRun, loggerExpression, informationMethod, errorMethod);
        }
    }

    internal static Task ExecuteCoreAsync(
        Microsoft.CodeAnalysis.Solution solution,
        bool dryRun,
        string loggerExpression = "_logger",
        string informationMethod = "LogInformation",
        string errorMethod = "LogError") =>
        ConsoleLoggingCore.RunAsync(
            solution,
            new ConsoleLoggingOptions(loggerExpression, informationMethod, errorMethod),
            dryRun);
}
