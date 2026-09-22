using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
/// Issue #128, remedy 4. Machine diagnostics must retain every affected project and error.
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
            completeness.FailedProjects.ShouldContain(
                project => Path.GetFileNameWithoutExtension(project) == "Quiet");
            completeness.StructuredDiagnostics.ShouldContain(
                diagnostic => diagnostic.ProjectPath != null
                    && Path.GetFileNameWithoutExtension(diagnostic.ProjectPath) == "Quiet");
        }
    }

    [Fact]
    public async Task machine_diagnostics_retain_every_compiler_error()
    {
        using var builder = new AdhocProjectBuilder("Noisy");
        builder.AddSource("Noisy.cs", ManyErrors(40));
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, default);

            completeness.StructuredDiagnostics.Count(diagnostic => diagnostic.Origin == "compiler"
                && diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .ShouldBeGreaterThanOrEqualTo(40);
            completeness.Diagnostics.ShouldNotContain(
                diagnostic => diagnostic.Contains("truncated", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task a_solution_without_acquisition_receipts_remains_partial_even_when_it_compiles()
    {
        using var builder = new AdhocProjectBuilder("Healthy");
        builder.AddSource("Healthy.cs", "public class Healthy { }");
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, default);

            completeness.State.ShouldBe(WorkspaceCompletenessState.Partial);
            completeness.StructuredDiagnostics.ShouldNotContain(
                diagnostic => diagnostic.Origin == "compiler"
                    && diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        }
    }
}
