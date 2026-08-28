using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record ConvertConsoleLoggingRequest(
    string? Solution = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = false,
    bool Apply = false,
    bool Review = false,
    string Logger = "_logger",
    [property: JsonPropertyName("information-method")] string InformationMethod = "LogInformation",
    [property: JsonPropertyName("warning-method")] string WarningMethod = "LogWarning",
    [property: JsonPropertyName("error-method")] string ErrorMethod = "LogError",
    string Stream = "stderr",
    [property: JsonPropertyName("exclude")] IReadOnlyList<string>? ExcludePaths = null,
    [property: JsonPropertyName("no-infer-level")] bool NoInferLevel = false,
    [property: JsonPropertyName("inject-logger")] bool InjectLogger = false,
    [property: JsonPropertyName("logger-dependency")] string LoggerDependency = "ILogger<{TypeName}>",
    [property: JsonPropertyName("logger-field")] string LoggerField = "_logger");

internal sealed record ConvertConsoleLoggingResult(
    int EntryCount,
    IReadOnlyList<ConsoleLoggingReviewEntry> Entries,
    IReadOnlyList<LoggerInjectionTarget> LoggerInjections);

internal sealed record LoggerInjectionTarget(string TypeFullName, string DependencyType);

internal sealed class ConvertConsoleLoggingOperation
    : CommandApplicationPort<ConvertConsoleLoggingRequest,
        MutationExecutionResult<ConvertConsoleLoggingResult>>, IRtService
{
    private readonly AddDependencyService _addDependency;
    private readonly PreparedChangeSetExecutor _executor;
    private readonly IWorkspaceSessionFactory _sessions;

    public ConvertConsoleLoggingOperation(
        IWorkspaceSessionFactory sessions,
        AddDependencyService addDependency,
        ISolutionChangeWriter writer)
    {
        _sessions = sessions;
        _addDependency = addDependency;
        _executor = new PreparedChangeSetExecutor(new SolutionPreparedChangeSetWriter(writer));
    }

    public override ApplicationCommandDefinition Definition =>
        ConvertConsoleLoggingCommandDefinition.Definition;

    public override async Task<Result<MutationExecutionResult<ConvertConsoleLoggingResult>, RtError>> ExecuteAsync(
        ConvertConsoleLoggingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var optionsResult = CreateOptions(request);
        if (optionsResult.IsErr)
            return optionsResult.Match(
                _ => throw new InvalidOperationException("An invalid console logging option was accepted."),
                error => Result<MutationExecutionResult<ConvertConsoleLoggingResult>, RtError>.Err(error));
        var options = optionsResult.Unwrap();

        var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var review = await ConsoleLoggingCore.AnalyzeAsync(solution, options);
                var targets = request.InjectLogger
                    ? GetInjectionTargets(review, request.LoggerDependency)
                    : [];
                var value = new ConvertConsoleLoggingResult(review.Entries.Count, review.Entries, targets);

                if (request.Review || (!request.Apply && !request.DryRun))
                    return Result<MutationExecutionResult<ConvertConsoleLoggingResult>, RtError>.Ok(
                        MutationExecutionResult<ConvertConsoleLoggingResult>.Reviewed(value));

                var prepared = await PrepareAsync(
                    solution,
                    options,
                    targets,
                    request.LoggerField,
                    cancellationToken);
                if (prepared.IsErr)
                    return prepared.Match(
                        _ => throw new InvalidOperationException("An invalid console logging preparation was accepted."),
                        error => Result<MutationExecutionResult<ConvertConsoleLoggingResult>, RtError>.Err(error));

                var changeSet = new PreparedChangeSet(solution, prepared.Unwrap());
                if (!changeSet.HasChanges)
                    return Result<MutationExecutionResult<ConvertConsoleLoggingResult>, RtError>.Ok(
                        MutationExecutionResult<ConvertConsoleLoggingResult>.NoOp(
                            value,
                            [new MutationDiagnostic(
                                MutationDiagnosticSeverity.Information,
                                "No matching Console logging calls were found.")]));

                var execution = request.DryRun
                    ? _executor.Preview(value, changeSet)
                    : await _executor.ApplyAsync(value, changeSet, cancellationToken: cancellationToken);
                return Result<MutationExecutionResult<ConvertConsoleLoggingResult>, RtError>.Ok(execution);
            }
            catch (InvalidOperationException exception)
            {
                return Result<MutationExecutionResult<ConvertConsoleLoggingResult>, RtError>.Err(
                    new RtError(RtErrorKind.InvalidInput, exception.Message));
            }
        }
    }

    private static Result<ConsoleLoggingOptions, RtError> CreateOptions(ConvertConsoleLoggingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Stream))
            return Result<ConsoleLoggingOptions, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Stream must be one of: all, stdout, stderr."));

        var stream = request.Stream.ToLowerInvariant() switch
        {
            "all" => ConsoleLoggingStream.All,
            "stdout" => ConsoleLoggingStream.Stdout,
            "stderr" => ConsoleLoggingStream.Stderr,
            _ => (ConsoleLoggingStream?)null
        };

        if (stream is null)
            return Result<ConsoleLoggingOptions, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Stream must be one of: all, stdout, stderr."));

        return Result<ConsoleLoggingOptions, RtError>.Ok(new ConsoleLoggingOptions(
            request.Logger,
            request.InformationMethod,
            request.WarningMethod,
            request.ErrorMethod,
            stream.Value,
            request.ExcludePaths ?? [],
            !request.NoInferLevel));
    }

    private async Task<Result<Microsoft.CodeAnalysis.Solution, RtError>> PrepareAsync(
        Microsoft.CodeAnalysis.Solution solution,
        ConsoleLoggingOptions options,
        IReadOnlyList<LoggerInjectionTarget> targets,
        string loggerField,
        CancellationToken cancellationToken)
    {
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var injection = await _addDependency.PrepareAsync(
                solution,
                target.TypeFullName,
                target.DependencyType,
                loggerField,
                cancellationToken);
            if (injection.Outcome is AddDependencyOutcome.TargetNotFound
                or AddDependencyOutcome.TargetAmbiguous
                or AddDependencyOutcome.Unsupported)
                return Result<Microsoft.CodeAnalysis.Solution, RtError>.Err(CreateInjectionError(injection));

            solution = injection.Solution;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result<Microsoft.CodeAnalysis.Solution, RtError>.Ok(
            await ConsoleLoggingCore.ApplyAsync(solution, options));
    }

    private static RtError CreateInjectionError(AddDependencyService.DependencyInjectionResult result)
    {
        var message = result.Message ?? result.Outcome.ToString();
        var context = result.TargetType is null
            ? null
            : new Dictionary<string, string> { ["targetType"] = result.TargetType };
        return result.Outcome switch
        {
            AddDependencyOutcome.TargetNotFound => RtError.NotFound(message, context),
            AddDependencyOutcome.TargetAmbiguous => RtError.AmbiguousMatch(message, context),
            _ => new RtError(RtErrorKind.InvalidInput, message, context)
        };
    }

    private static LoggerInjectionTarget[] GetInjectionTargets(
        ConsoleLoggingReview review,
        string dependencyTemplate)
    {
        return review.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.TypeName)
                            && !string.IsNullOrWhiteSpace(entry.TypeFullName))
            .GroupBy(entry => entry.TypeFullName!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var entry = group.First();
                return new LoggerInjectionTarget(
                    entry.TypeFullName!,
                    FormatDependencyType(dependencyTemplate, entry.TypeName!, entry.TypeFullName!));
            })
            .ToArray();
    }

    private static string FormatDependencyType(string template, string typeName, string typeFullName)
    {
        return template.Replace("{TypeName}", typeName, StringComparison.Ordinal)
            .Replace("{TypeFullName}", typeFullName, StringComparison.Ordinal);
    }
}

internal static class ConvertConsoleLoggingCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "convert-console-logging",
        true,
        false,
        "refactor",
        Aliases: ["ccl"],
        WhenToUse:
        "Use this instead of manual Edit calls when migrating Console.Write/WriteLine and Console.Error.Write/WriteLine calls to structured logger calls. Defaults to structured review; pass --apply to write changes.",
        Examples:
        [
            "rt refactor convert-console-logging --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ccl --inject-logger --solution C:\\code\\app\\App.sln",
            "rt refactor ccl --inject-logger --apply --solution C:\\code\\app\\App.sln",
            "rt refactor convert-console-logging --stream all --logger _log --information-method Info --warning-method Warn --error-method Error --dry-run --solution C:\\code\\app\\App.sln"
        ],
        KnownIssues:
        [
            "Logger injection reuses add-dependency and only targets containing classes that have matching Console call sites.",
            "Ambiguous non-template expressions are wrapped as {Value} and flagged by review output."
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ConvertConsoleLoggingRequest),
        typeof(MutationExecutionResult<ConvertConsoleLoggingResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
