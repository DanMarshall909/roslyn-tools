using System.CommandLine;

using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.SemanticReview;

namespace RoslynTools.Commands;

internal sealed class CodeSmellsCommand(CodeSmellsOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    public static RtCommandInfo Info => CodeSmellsCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var configOpt = new Option<string?>("--config", "Catalogue path relative to the selected repository");
        var ruleIdsOpt = new Option<string[]>("--rule-ids", "Select catalogue rule IDs") { AllowMultipleArgumentsPerToken = true };
        var providerOpt = new Option<string>("--provider", () => "typesafe", "Semantic review provider");
        var modelOpt = new Option<string>("--model", () => "jev-latest", "Provider model identifier");
        var maxCandidatesOpt = new Option<int>("--max-candidates", () => 20, "Global candidate cap (1–100)");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without provider calls");
        var transmitSourceOpt = new Option<bool>("--transmit-source", "Transmit bounded sanitized source with repository trust");
        var command = new Command("code-smells", "Review bounded code smells using an editable catalogue");
        command.AddAll(solutionOpt, configOpt, ruleIdsOpt, providerOpt, modelOpt, maxCandidatesOpt, dryRunOpt, transmitSourceOpt);
        command.SetHandler(async context =>
        {
            var selected = context.ParseResult.GetValueForOption(ruleIdsOpt);
            var result = await operation.ExecuteAsync(new(
                context.ParseResult.GetValueForOption(solutionOpt), context.ParseResult.GetValueForOption(configOpt),
                selected is { Length: > 0 } ? selected : null,
                context.ParseResult.GetValueForOption(providerOpt)!, context.ParseResult.GetValueForOption(modelOpt)!,
                context.ParseResult.GetValueForOption(maxCandidatesOpt), context.ParseResult.GetValueForOption(dryRunOpt),
                context.ParseResult.GetValueForOption(transmitSourceOpt)), context.GetCancellationToken());
            result.Match(value =>
            {
                RtResultRenderer.Write(RtResult<CodeSmellsOutput>.Ok(new(value)));
                if (value.Outcome is "Partial" or "Failed") context.ExitCode = 1;
            }, error => RtErrorPresenter.Surface(context, error));
        });
        return command;
    }
}
