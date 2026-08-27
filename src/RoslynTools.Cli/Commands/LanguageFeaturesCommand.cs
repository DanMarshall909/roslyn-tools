using System.CommandLine;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Inventories C# language-feature usage and comparable refactoring candidates.</summary>
internal sealed class LanguageFeaturesCommand(
    DaemonRpcClient daemon,
    LanguageFeaturesOperation operation) : IRtCommand, IAnalyzeCommand, IStructuredOutputCommand
{
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DaemonRpcClient _daemon = daemon;
    private readonly LanguageFeaturesOperation _operation = operation;

    public static RtCommandInfo Info => LanguageFeaturesCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var featureOpt = new Option<string?>(
            "--feature",
            "Stable feature ID to inspect, for example collection_expression");
        var categoryOpt = new Option<string?>(
            "--category",
            "Feature category to inspect, for example declaration, expression, or pattern");
        var introducedSinceOpt = new Option<string?>(
            "--introduced-since",
            "Only features introduced in this C# version or later, for example 12 or 14.0");
        var topOpt = new Option<int?>(
            "--top",
            "Return the N most-used matching features; omit or use 0 for all");
        var includeLocationsOpt = new Option<bool>(
            "--include-locations",
            "Include occurrence and refactoring-candidate source locations");
        var includeGeneratedOpt = new Option<bool>(
            "--include-generated",
            "Include generated, intermediate, and auto-generated source documents");
        var refactorableOnlyOpt = new Option<bool>(
            "--refactorable-only",
            "Only return features with mechanically comparable alternatives to review");

        var command = new Command(
            "language-features",
            "Inventory C# language features, density, adoption, and refactoring candidates")
        {
            solutionOpt,
            featureOpt,
            categoryOpt,
            introducedSinceOpt,
            topOpt,
            includeLocationsOpt,
            includeGeneratedOpt,
            refactorableOnlyOpt
        };
        command.AddAlias("lang-features");

        command.SetHandler(async context =>
        {
            var request = new LanguageFeaturesRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(featureOpt),
                context.ParseResult.GetValueForOption(categoryOpt),
                context.ParseResult.GetValueForOption(introducedSinceOpt),
                context.ParseResult.GetValueForOption(topOpt),
                context.ParseResult.GetValueForOption(includeLocationsOpt),
                context.ParseResult.GetValueForOption(includeGeneratedOpt),
                context.ParseResult.GetValueForOption(refactorableOnlyOpt));

            var result = await ExecuteDaemonPreferredAsync(request);
            if (result is null)
            {
                RtLog.Error("Language-feature analysis returned no result.");
                context.ExitCode = 1;
                return;
            }

            RtResultRenderer.Write(RtResult<LanguageFeaturesResult>.Ok(result));
            if (result.Diagnostics.Any(diagnostic =>
                    string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase)))
                context.ExitCode = 1;
        });

        return command;
    }

    private async Task<LanguageFeaturesResult?> ExecuteDaemonPreferredAsync(
        LanguageFeaturesRequest request)
    {
        var response = await _daemon.TryExecuteAsync(
            LanguageFeaturesCommandDefinition.Name,
            JsonSerializer.SerializeToElement(request));
        if (response is null)
            return await _operation.ExecuteAsync(request);

        // A resident daemon may be an older rt build. This command is read-only, so a
        // command-not-found response is safe to recover in process without restart friction.
        if (response.Error?.Code == DaemonRpcErrorCodes.CommandNotFound)
            return await _operation.ExecuteAsync(request);

        if (response.Error is not null)
            throw new InvalidOperationException(response.Error.Message);

        if (response.Result is not { } envelope
            || !envelope.TryGetProperty("data", out var data))
            return null;

        return data.Deserialize<LanguageFeaturesResult>(ResponseOptions);
    }
}
