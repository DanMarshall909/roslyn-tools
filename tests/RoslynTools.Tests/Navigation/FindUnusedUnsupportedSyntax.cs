using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindUnusedUnsupportedSyntax
{
    [Theory]
    [InlineData("all")]
    [InlineData("methods")]
    [InlineData("types")]
    public async Task unsupported_unions_in_multiple_projects_do_not_become_unused_symbols(string scope)
    {
        using var builder = new AdhocProjectBuilder("Domain");
        builder.AddSource("ValueExpression.cs", """
            namespace Domain;

            public readonly union ValueExpression(
                ValueExpression.Literal,
                ValueExpression.Dice,
                ValueExpression.Add);
            """);
        builder.AddSource("DomainHelper.cs", """
            namespace Domain;
            internal static class DomainHelper
            {
                internal static void InvestigateDomain() { }
            }
            """);
        builder.AddProject("Application")
            .ReferenceProject("Domain")
            .AddSource("StartDuelResult.cs", """
                namespace Application;

                public readonly union StartDuelResult(
                    StartDuelResult.Started,
                    StartDuelResult.Rejected);
                """)
            .AddSource("ApplicationHelper.cs", """
                namespace Application;
                internal static class ApplicationHelper
                {
                    internal static void InvestigateApplication() { }
                }
                """);
        var (workspace, originalSolution) = builder.Build();
        using (workspace)
        {
            var solution = originalSolution;
            foreach (var projectId in solution.ProjectIds)
                solution = solution.WithProjectParseOptions(projectId,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

            foreach (var project in solution.Projects)
            {
                var unionDocument = project.Documents.Single(document => !document.Name.EndsWith("Helper.cs", StringComparison.Ordinal));
                var root = (await unionDocument.GetSyntaxRootAsync()).ShouldNotBeNull();
                root.GetDiagnostics().ShouldContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                root.DescendantNodes().OfType<MethodDeclarationSyntax>().ShouldNotBeEmpty(
                    "the regression fixture must exercise recovery as a method, as reported in issue 128");
            }

            var findings = await new FindUnusedService().AnalyzeAsync(solution, scope);

            findings.ShouldNotContain(finding => finding.Name == "ValueExpression" || finding.Name == "StartDuelResult");
            findings.ShouldAllBe(finding => finding.FilePath != null
                && finding.FilePath.EndsWith("Helper.cs", StringComparison.Ordinal));
            foreach (var projectName in new[] { "Domain", "Application" })
            {
                if (scope is "all" or "methods")
                    findings.ShouldContain(finding => finding.Name == $"Investigate{projectName}" && finding.Kind == "method");
                if (scope is "all" or "types")
                    findings.ShouldContain(finding => finding.Name == $"{projectName}Helper" && finding.Kind == "type");
            }
        }
    }

    [Fact]
    public async Task syntax_errors_suppress_all_candidates_in_the_affected_document()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Broken.cs", """
            internal class Broken
            {
                internal void OtherwiseValidMethod() { }
                internal void IncompleteMethod(
            }
            """);
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var findings = await new FindUnusedService().AnalyzeAsync(solution);

            findings.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task cancelled_analysis_does_not_report_empty_workspace_as_success()
    {
        using var workspace = new AdhocWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => new FindUnusedService().AnalyzeAsync(
            workspace.CurrentSolution,
            cancellationToken: cancellation.Token));
    }
}
