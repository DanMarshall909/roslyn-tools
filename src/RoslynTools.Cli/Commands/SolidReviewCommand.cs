using System.CommandLine;

using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.SemanticReview;

namespace RoslynTools.Commands;

internal sealed class SolidReviewCommand(
    SolidReviewOperation operation) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly SolidReviewOperation _operation = operation;

    public static RtCommandInfo Info => SolidReviewCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var principleOpt = new Option<string>(
            "--principle",
            () => "all",
            "SOLID principle to review: SRP, ISP, DIP, OCP, LSP, or all");
        var minMethodsOpt = new Option<int>(
            "--min-methods",
            () => 10,
            "Minimum public method count for an SRP candidate");
        var minDepsOpt = new Option<int>(
            "--min-deps",
            () => 5,
            "Minimum injected dependency count for an SRP candidate");
        var minMembersOpt = new Option<int>(
            "--min-members",
            () => 7,
            "Minimum declared interface member count for an ISP candidate");
        var minBranchesOpt = new Option<int>(
            "--min-branches",
            () => 2,
            "Minimum concrete-type branches for an OCP candidate");
        var providerOpt = new Option<string>(
            "--provider",
            () => "typesafe",
            "Semantic review provider");
        var modelOpt = new Option<string>(
            "--model",
            () => "jev-latest",
            "Provider model identifier");
        var maxCandidatesOpt = new Option<int>(
            "--max-candidates",
            () => 20,
            "Maximum candidates to prepare or review (1-100)");
        var dryRunOpt = new Option<bool>(
            "--dry-run",
            "Explicitly preview bounded payload metadata without transmitting source");
        var transmitSourceOpt = new Option<bool>(
            "--transmit-source",
            "Transmit bounded sanitized candidate source to the selected provider");

        var command = new Command(
            "solid-review",
            "Rank deterministic SOLID candidates with bounded advisory semantic review");
        command.AddAll(
            solutionOpt,
            principleOpt,
            minMethodsOpt,
            minDepsOpt,
            minMembersOpt,
            minBranchesOpt,
            providerOpt,
            modelOpt,
            maxCandidatesOpt,
            dryRunOpt,
            transmitSourceOpt);

        command.SetHandler(async context =>
        {
            var request = new SolidReviewRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(principleOpt)!,
                context.ParseResult.GetValueForOption(minMethodsOpt),
                context.ParseResult.GetValueForOption(minDepsOpt),
                context.ParseResult.GetValueForOption(minMembersOpt),
                context.ParseResult.GetValueForOption(minBranchesOpt),
                context.ParseResult.GetValueForOption(providerOpt)!,
                context.ParseResult.GetValueForOption(modelOpt)!,
                context.ParseResult.GetValueForOption(maxCandidatesOpt),
                context.ParseResult.GetValueForOption(dryRunOpt),
                context.ParseResult.GetValueForOption(transmitSourceOpt));
            var result = await _operation.ExecuteAsync(
                request,
                context.GetCancellationToken());
            result.Match(
                value =>
                {
                    RtResultRenderer.Write(RtResult<SolidReviewOutput>.Ok(new SolidReviewOutput(value)));
                    if (value.Outcome is "Partial" or "Failed")
                        context.ExitCode = 1;
                },
                error => RtErrorPresenter.Surface(context, error));
        });

        return command;
    }
}
