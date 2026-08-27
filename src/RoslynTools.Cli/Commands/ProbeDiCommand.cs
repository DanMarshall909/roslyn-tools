using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>Probes one registration method and reports implementations hidden from static analysis.</summary>
internal sealed class ProbeDiCommand(ProbeDiOperation operation) : IRtCommand, IAnalyzeCommand, IStructuredOutputCommand
{
    private readonly ProbeDiOperation _operation = operation;
    public static RtCommandInfo Info => ProbeDiCommandDefinition.Info;

    public Command Build()
    {
        var entrypointOption = new Option<string>("--entrypoint", "Registration method in Type.Method form")
        {
            IsRequired = true
        };
        var binder = RtCommandFlagBinder.Create().WithSolution();
        var command = new Command("probe-di", "Execute one DI registration method and report invisible registrations");
        command.Add(entrypointOption);
        binder.AddTo(command);

        command.SetHandler(async context =>
        {
            var request = binder.Bind(context.ParseResult, flags => new Request(
                context.ParseResult.GetValueForOption(entrypointOption)!,
                flags.SolutionPath));
            var result = await _operation.ExecuteAsync(new ProbeDiRequest(request.Entrypoint, request.SolutionPath), context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<ProbeDiOutput>.Ok(ProbeDiOutput.From(value))),
                error => RtErrorPresenter.Surface(context, error));
        });

        return command;
    }

    private sealed record Request(string Entrypoint, string? SolutionPath) : ISupportsSolutionOption;
}

internal sealed record ProbeDiOutput : IRtOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Llm, "findings")]
    public required IReadOnlyList<ProbeDiFinding> Findings { get; init; }

    internal static ProbeDiOutput From(ProbeDiResult result) => new() { Findings = result.Findings };

    public void RenderText(TextWriter writer)
    {
        for (var index = 0; index < Findings.Count; index++)
        {
            var finding = Findings[index];
            writer.Write($"{finding.Status,-12} {finding.ImplementationType ?? finding.ServiceType} — {finding.Reason}");
            if (finding.SuggestedFix is { } fix)
            {
                writer.Write("\n  ");
                fix.RenderText(writer);
            }
            if (index < Findings.Count - 1)
                writer.WriteLine();
        }
    }

    public void RenderJson(TextWriter writer)
    {
        writer.Write(System.Text.Json.JsonSerializer.Serialize(new
        {
            findings = Findings.Select(f => new
            {
                serviceType = f.ServiceType,
                implementationType = f.ImplementationType,
                status = f.Status,
                reason = f.Reason,
                suggestedFix = f.SuggestedFix?.AsJsonObject()
            }).ToArray()
        }, SymbolFormatter.DefaultJsonOptions));
    }
}
