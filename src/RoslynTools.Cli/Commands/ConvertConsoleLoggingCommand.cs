using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Converts console logging to structured logger calls.</summary>
internal sealed class ConvertConsoleLoggingCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ConvertConsoleLoggingOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ConvertConsoleLoggingOperation _operation = operation;

    public static RtCommandInfo Info => ConvertConsoleLoggingCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var loggerOpt = new Option<string>("--logger", () => "_logger", "Logger expression to call");
        var informationMethodOpt = new Option<string>("--information-method", () => "LogInformation",
            "Method used for Console.Write/WriteLine");
        var warningMethodOpt = new Option<string>("--warning-method", () => "LogWarning",
            "Method used when warning-level text is inferred");
        var errorMethodOpt = new Option<string>("--error-method", () => "LogError",
            "Method used for Console.Error.Write/WriteLine");
        var streamOpt = new Option<string>("--stream", () => "stderr",
            "Console stream to rewrite: all, stdout, or stderr");
        var excludeOpt = new Option<string[]>("--exclude", "Path fragment to exclude; repeat for multiple exclusions")
        {
            AllowMultipleArgumentsPerToken = true
        };
        var noInferLevelOpt = new Option<bool>("--no-infer-level",
            "Disable warning/info/error inference and map stderr to the error method");
        var applyOpt = new Option<bool>("--apply",
            "Write changes to disk. Without this, the command returns a structured review.");
        var reviewOpt = new Option<bool>("--review",
            "Summarize matching call sites without writing files or printing a full diff. This is the default mode.");
        var injectLoggerOpt = new Option<bool>("--inject-logger",
            "Inject the logger dependency into each containing class before rewriting call sites");
        var loggerDependencyOpt = new Option<string>("--logger-dependency", () => "ILogger<{TypeName}>",
            "Logger dependency type template; supports {TypeName} and {TypeFullName}");
        var loggerFieldOpt = new Option<string>("--logger-field", () => "_logger",
            "Logger field name passed to add-dependency");

        var cmd = new Command("convert-console-logging", "Convert Console.Write/WriteLine calls to logger calls");
        cmd.AddAlias("ccl");
        cmd.AddAll(solutionOpt, dryRunOpt, loggerOpt, informationMethodOpt, warningMethodOpt, errorMethodOpt, streamOpt,
            excludeOpt, noInferLevelOpt, applyOpt, reviewOpt, injectLoggerOpt, loggerDependencyOpt, loggerFieldOpt);

        cmd.SetHandler(async context =>
        {
            var request = new ConvertConsoleLoggingRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(dryRunOpt),
                context.ParseResult.GetValueForOption(applyOpt),
                context.ParseResult.GetValueForOption(reviewOpt),
                context.ParseResult.GetValueForOption(loggerOpt)!,
                context.ParseResult.GetValueForOption(informationMethodOpt)!,
                context.ParseResult.GetValueForOption(warningMethodOpt)!,
                context.ParseResult.GetValueForOption(errorMethodOpt)!,
                context.ParseResult.GetValueForOption(streamOpt)!,
                context.ParseResult.GetValueForOption(excludeOpt) ?? [],
                context.ParseResult.GetValueForOption(noInferLevelOpt),
                context.ParseResult.GetValueForOption(injectLoggerOpt),
                context.ParseResult.GetValueForOption(loggerDependencyOpt)!,
                context.ParseResult.GetValueForOption(loggerFieldOpt)!);
            await DaemonCommandExecutor.ExecuteAsync(_daemon, _operation, request, context);
        });

        return cmd;
    }
}
