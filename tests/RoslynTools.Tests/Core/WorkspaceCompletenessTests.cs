using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
/// Issue #128, remedy 4. The completeness assessment is what a reader uses to decide which of an
/// analysis's findings to trust. A project missing from its diagnostic list reads as a project that
/// compiled, and a list that stops at a round number reads as the whole truth — so what the
/// assessment leaves out matters as much as what it reports.
/// </summary>
public sealed class WorkspaceCompletenessTests
{
    /// <summary>
    /// Enough broken declarations to exhaust any fixed diagnostic budget on their own.
    /// </summary>
    private static string ManyErrors(int count) =>
        string.Join(
            Environment.NewLine,
            Enumerable.Range(0, count).Select(i => $"public class Broken{i} : NoSuchBaseType{i} {{ }}"));

    [Fact]
    public async Task every_project_that_failed_to_compile_is_named()
    {
        using var builder = new AdhocProjectBuilder("Empty");
        builder.AddProject("Noisy").AddSource("Noisy.cs", ManyErrors(40));
        builder.AddProject("Quiet").AddSource("Quiet.cs", ManyErrors(1));
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, default);

            completeness.State.ShouldBe(WorkspaceCompletenessState.Partial);
            completeness.Diagnostics.ShouldContain(
                diagnostic => diagnostic.StartsWith("Quiet:", StringComparison.Ordinal),
                "a project crowded out of the list by a noisier one is indistinguishable from a "
                + "project that compiled, which is how mis-parsed source comes to be trusted");
        }
    }

    [Fact]
    public async Task a_diagnostic_list_that_leaves_errors_out_says_so()
    {
        using var builder = new AdhocProjectBuilder("Noisy");
        builder.AddSource("Noisy.cs", ManyErrors(40));
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, default);

            completeness.Diagnostics.ShouldContain(
                diagnostic => diagnostic.Contains("truncated", StringComparison.Ordinal)
                              && diagnostic.Contains("40", StringComparison.Ordinal),
                "a list that ends silently at its own limit invites the reader to treat it as the "
                + "complete account of what went wrong");
        }
    }

    [Fact]
    public async Task a_solution_whose_projects_all_compile_reports_no_diagnostics()
    {
        using var builder = new AdhocProjectBuilder("Healthy");
        builder.AddSource("Healthy.cs", "public class Healthy { }");
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, default);

            completeness.State.ShouldBe(WorkspaceCompletenessState.Complete);
            completeness.Diagnostics.ShouldBeEmpty(
                "a truncation notice on a clean workspace would be a warning about nothing");
        }
    }
}
