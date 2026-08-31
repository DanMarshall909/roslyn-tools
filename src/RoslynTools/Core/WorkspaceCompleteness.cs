using Microsoft.CodeAnalysis;
using System.Globalization;

namespace RoslynTools.Core;

/// <summary>Whether the loaded semantic inputs can support absence-based conclusions.</summary>
internal enum WorkspaceCompletenessState
{
    Complete,
    Partial,
    Failed
}

/// <summary>A bounded assessment used to gate proof-sensitive operations.</summary>
internal sealed record WorkspaceCompleteness(
    WorkspaceCompletenessState State,
    IReadOnlyList<string> Diagnostics)
{
    private const int DiagnosticLimit = 20;

    internal static async Task<WorkspaceCompleteness> AssessSemanticAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var projects = solution.Projects.ToArray();
        if (projects.Length == 0)
            return new WorkspaceCompleteness(
                WorkspaceCompletenessState.Failed,
                ["The loaded solution contains no projects."]);

        var diagnostics = new List<string>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                diagnostics.Add($"Project '{project.Name}' did not produce a compilation.");
                continue;
            }

            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken)
                         .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                         .Take(DiagnosticLimit - diagnostics.Count))
            {
                diagnostics.Add(
                    $"{project.Name}: {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");
            }

            if (diagnostics.Count >= DiagnosticLimit)
                break;
        }

        return diagnostics.Count == 0
            ? new WorkspaceCompleteness(WorkspaceCompletenessState.Complete, [])
            : new WorkspaceCompleteness(WorkspaceCompletenessState.Partial, diagnostics);
    }
}
