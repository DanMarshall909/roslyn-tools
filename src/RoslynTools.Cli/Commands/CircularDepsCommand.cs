using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for detecting and handling circular dependencies.
/// </summary>
internal sealed class CircularDepsCommand : IRtCommand
{
    private CircularDepsCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "circular-deps",
        false,
        false,
        "analyze",
        Aliases: ["cdeps"],
        WhenToUse:
        "Detecting circular namespace dependencies in a solution before a refactor, or after adding new inter-namespace references. Use --scope to limit analysis to your domain namespaces and ignore third-party noise.",
        Examples:
        [
            "rt analyze circular-deps --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze cdeps --scope ProjectX.Core --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze circular-deps --solution RoslynTools.slnx --scope RoslynTools"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var scopeOpt = new Option<string?>("--scope", "Optional namespace prefix to limit analysis");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");

        var cmd = new Command("circular-deps", "Detect circular namespace/type dependencies in the solution");
        cmd.AddAlias("cdeps");
        cmd.Add(scopeOpt);
        cmd.Add(solutionOpt);

        cmd.SetHandler(async (scope, solution) =>
        {
            var (workspace, sln) = await RtServiceProvider.Get<WorkspaceLoader>().LoadWorkspaceAsync(solution);
            using (workspace)
            {
                var cycles = await RtServiceProvider.Get<CircularDepsService>().AnalyzeCyclesAsync(sln, scope);
                var result = CircularDepsResult.FromCycles(cycles);
                RtResultRenderer.WriteOutput(RtResult<CircularDepsResult>.Ok(result));
            }
        }, scopeOpt, solutionOpt);

        return cmd;
    }

    // Kept for test backwards-compatibility — delegates to the service.
    /// <summary>
    /// Analyzes cycles in a solution using Roslyn.
    /// </summary>
    internal static Task<IReadOnlyList<IReadOnlyList<string>>> AnalyzeCyclesAsync(Solution sln, string? scope)
    {
        return RtServiceProvider.Get<CircularDepsService>().AnalyzeCyclesAsync(sln, scope);
    }

    /// <summary>
    /// Analyzes cycles in a solution asynchronously.
    /// </summary>
    internal static Task<string> AnalyzeAsync(Solution sln, string? scope)
    {
        return RtServiceProvider.Get<CircularDepsService>().AnalyzeAsync(sln, scope);
    }
}
