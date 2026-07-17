using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Probes one registration method and reports implementations hidden from static analysis.</summary>
internal sealed class ProbeDiCommand : IRtCommand, IAnalyzeCommand, IStructuredOutputCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly ProbeDiService _probeDi;

    public ProbeDiCommand(IWorkspaceSessionFactory sessionFactory, ProbeDiService probeDi)
    {
        _sessionFactory = sessionFactory;
        _probeDi = probeDi;
    }

    public static RtCommandInfo Info { get; } = new(
        "probe-di",
        false,
        true,
        "analyze",
        WhenToUse:
        "Auditing assembly-scanning dependency injection after a successful build. This explicitly executes only the selected registration method and never boots the application host.",
        Examples:
        [
            "rt analyze probe-di --entrypoint Bootstrap.RegisterServices --solution MyApp.slnx"
        ],
        SeeAlso: ["add-dynamic-dependency", "find-unused"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

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
            var solution = await _sessionFactory.GetSolutionAsync(request.SolutionPath);
            var registrations = await _probeDi.ProbeAsync(solution, request.Entrypoint);
            var result = ProbeDiResult.From(registrations, request.Entrypoint, solution.FilePath);
            RtResultRenderer.Write(RtResult<ProbeDiResult>.Ok(result));
        });

        return command;
    }

    private sealed record Request(string Entrypoint, string? SolutionPath) : ISupportsSolutionOption;
}

internal sealed record ProbeDiFinding
{
    internal const string UnprobeableReason =
        "The registration uses an implementation factory and has no ImplementationType.";

    internal const string InvisibleReason =
        "The implementation is registered at runtime but has no source reference or reflective-use annotation.";

    public required string ServiceType { get; init; }
    public string? ImplementationType { get; init; }
    public required string Status { get; init; }
    public required string Reason { get; init; }
    public SuggestedFix? SuggestedFix { get; init; }

    internal static ProbeDiFinding From(ProbeDiRegistration registration, string root, string? solutionPath)
    {
        if (registration.Status != ProbeDiStatus.Invisible)
            return new ProbeDiFinding
            {
                ServiceType = registration.ServiceType,
                ImplementationType = registration.ImplementationType,
                Status = registration.Status,
                Reason = UnprobeableReason
            };

        return new ProbeDiFinding
        {
            ServiceType = registration.ServiceType,
            ImplementationType = registration.ImplementationType,
            Status = registration.Status,
            Reason = InvisibleReason,
            SuggestedFix = new SuggestedFix
            {
                Tool = "rt",
                Ref = "add-dynamic-dependency",
                Description = "Add a durable static reference at the composition root.",
                CommandLine =
                    $"rt refactor add-dynamic-dependency --type \"{registration.ImplementationType}\" "
                    + $"--root \"{root}\" --solution \"{solutionPath}\""
            }
        };
    }
}

internal sealed record ProbeDiResult : IRtOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Llm, "findings")]
    public required IReadOnlyList<ProbeDiFinding> Findings { get; init; }

    internal static ProbeDiResult From(
        IEnumerable<ProbeDiRegistration> registrations,
        string root,
        string? solutionPath) =>
        new()
        {
            Findings = registrations.Select(r => ProbeDiFinding.From(r, root, solutionPath)).ToList()
        };

    public void RenderText(TextWriter writer)
    {
        for (var index = 0; index < Findings.Count; index++)
        {
            var finding = Findings[index];
            writer.Write($"{finding.Status,-12} {finding.ImplementationType ?? finding.ServiceType} — {finding.Reason}");
            if (finding.SuggestedFix != null)
            {
                writer.WriteLine();
                finding.SuggestedFix.RenderText(writer);
            }

            if (index < Findings.Count - 1)
                writer.WriteLine();
        }
    }

    public void RenderJson(TextWriter writer)
    {
        writer.Write(JsonSerializer.Serialize(new
        {
            findings = Findings.Select(finding => new
            {
                serviceType = finding.ServiceType,
                implementationType = finding.ImplementationType,
                status = finding.Status,
                reason = finding.Reason,
                suggestedFix = finding.SuggestedFix?.AsJsonObject()
            })
        }, SymbolFormatter.DefaultJsonOptions));
    }
}
