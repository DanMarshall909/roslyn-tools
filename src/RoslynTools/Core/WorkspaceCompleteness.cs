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
    /// <summary>
    /// How many error messages the assessment sets out to list. It is a budget for detail, not a
    /// cap on attribution: every project that failed is named even where that costs more lines, and
    /// whatever the budget excludes is stated rather than dropped.
    /// </summary>
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

        // Every project is assessed. Stopping once a budget filled attributed the whole failure to
        // whichever projects happened to compile first and left the rest looking clean — the reading
        // that turned a mis-parsed domain project into trusted find-unused output.
        var failures = new List<IReadOnlyList<string>>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                failures.Add([$"Project '{project.Name}' did not produce a compilation."]);
                continue;
            }

            var errors = compilation.GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic =>
                    $"{project.Name}: {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}")
                .ToArray();

            if (errors.Length > 0)
                failures.Add(errors);
        }

        return failures.Count == 0
            ? new WorkspaceCompleteness(WorkspaceCompletenessState.Complete, [])
            : new WorkspaceCompleteness(WorkspaceCompletenessState.Partial, Summarize(failures));
    }

    /// <summary>
    /// Reports the failures within <see cref="DiagnosticLimit" /> while naming every project that
    /// failed. The budget is shared equally, so one project with hundreds of errors cannot crowd its
    /// neighbours out; where more projects failed than the budget has room for, each still gets a
    /// line, because a project absent from this list reads as a project that compiled. Anything left
    /// out is then said aloud — a list that stops silently at its own limit invites the reader to
    /// take it for the complete account.
    /// </summary>
    private static List<string> Summarize(List<IReadOnlyList<string>> failures)
    {
        var perProject = Math.Max(1, DiagnosticLimit / failures.Count);
        var reported = new List<string>();
        var total = 0;
        foreach (var errors in failures)
        {
            total += errors.Count;
            reported.AddRange(errors.Take(perProject));
        }

        if (total > reported.Count)
            reported.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Diagnostic list truncated: {0} of {1} errors shown, across {2} project(s) that failed to compile.",
                reported.Count,
                total,
                failures.Count));

        return reported;
    }
}
