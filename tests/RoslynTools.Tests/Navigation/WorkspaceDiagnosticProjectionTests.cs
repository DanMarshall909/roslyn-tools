using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class WorkspaceDiagnosticProjectionTests
{
    [Theory]
    [InlineData("search-symbols")]
    [InlineData("outline")]
    [InlineData("relationships")]
    [InlineData("doctor")]
    [InlineData("find-unused")]
    public async Task operations_preserve_acquisition_diagnostic_identity_severity_and_context(string operation)
    {
        using var builder = new AdhocProjectBuilder("DiagnosticApp");
        builder.AddSource("Healthy.cs", "namespace App; public class Healthy { }");
        var (workspace, solution) = builder.Build();
        var expected = Enum.GetValues<DiagnosticSeverity>().Select(severity =>
            new WorkspaceAcquisitionDiagnostic($"collector.{severity}", severity,
                severity == DiagnosticSeverity.Error
                    ? CompletenessDiagnosticImpact.RequiredInputIncomplete
                    : CompletenessDiagnosticImpact.None,
                "collector", $"Original {severity} message", "input:sample", solution.Projects.Single().FilePath)).ToArray();
        foreach (var diagnostic in expected)
            WorkspaceDiagnosticRegistry.Record(workspace, diagnostic);

        var actual = await ExecuteAsync(operation, builder, workspace, solution);

        foreach (var diagnostic in expected)
        {
            var projected = actual.Single(item => item.Code == diagnostic.Id);
            projected.Severity.ShouldBe(diagnostic.Severity.ToString());
            projected.Message.ShouldBe(diagnostic.Message);
            projected.Context.ShouldNotBeNull();
            projected.Context["origin"].ShouldBe(diagnostic.Origin);
            projected.Context["completenessImpact"].ShouldBe(diagnostic.Impact.ToString());
            projected.Context["inputId"].ShouldBe(diagnostic.InputId);
            projected.Context["projectPath"].ShouldBe(diagnostic.ProjectPath);
        }
    }

    [Theory]
    [InlineData("search-symbols")]
    [InlineData("outline")]
    [InlineData("relationships")]
    [InlineData("doctor")]
    [InlineData("find-unused")]
    public async Task operations_preserve_real_compiler_warning_hidden_and_error_diagnostics(string operation)
    {
        using var builder = new AdhocProjectBuilder("CompilerDiagnosticApp");
        builder.AddSource("Healthy.cs", """
            #warning ordinary warning
            using System.Text;
            namespace App;
            public class Healthy { private MissingType value; }
            """);
        var (workspace, solution) = builder.Build();

        var diagnostics = await ExecuteAsync(operation, builder, workspace, solution);

        diagnostics.ShouldContain(item => item.Code == "CS1030" && item.Severity == "Warning");
        diagnostics.ShouldContain(item => item.Code == "CS8019" && item.Severity == "Hidden");
        diagnostics.ShouldContain(item => item.Code == "CS0246" && item.Severity == "Error");
        foreach (var diagnostic in diagnostics.Where(item => item.Code is "CS1030" or "CS8019" or "CS0246"))
        {
            diagnostic.Context.ShouldNotBeNull();
            diagnostic.Context["origin"].ShouldBe("compiler");
            diagnostic.Context["inputId"].ShouldStartWith("compiler:");
            diagnostic.Context["projectPath"].ShouldBe(solution.Projects.Single().FilePath);
            diagnostic.Context["completenessImpact"].ShouldBe(diagnostic.Code == "CS0246"
                ? "RequiredInputIncomplete" : "None");
        }
    }

    private static async Task<IReadOnlyList<RtStructuredDiagnostic>> ExecuteAsync(
        string operation, AdhocProjectBuilder builder, Workspace workspace, Solution solution)
    {
        var sessions = new FixedSessionFactory(workspace, solution);
        var symbols = new SymbolCandidateService();
        switch (operation)
        {
            case "search-symbols":
                return (await new SearchSymbolsOperation(sessions, symbols)
                    .ExecuteAsync(new SearchSymbolsRequest("Healthy"))).Unwrap().Diagnostics;
            case "outline":
                return (await new OutlineOperation(sessions, new OutlineService())
                    .ExecuteAsync(new OutlineRequest(Path.Combine(builder.ProjectDir, "Healthy.cs")))).Unwrap().Diagnostics;
            case "relationships":
                return (await new RelationshipsOperation(sessions,
                    new FindRefsService(new RtConsoleLogger<FindRefsService>(), symbols),
                    new FindCallersService(new RtConsoleLogger<FindCallersService>(), symbols),
                    new FindImplementationsService(), symbols)
                    .ExecuteAsync(new RelationshipsRequest("App.Healthy", RelationshipMode.References, Kind: "type")))
                    .Unwrap().Diagnostics;
            case "doctor":
                var solutionPath = Path.Combine(builder.Directory, "App.slnx");
                File.WriteAllText(solutionPath, "<Solution />");
                using (RepositoryTrustContext.UseExplicitTrust(builder.Directory))
                    return (await new DoctorOperation(sessions, new FixedCapabilityCatalog())
                        .ExecuteAsync(new DoctorRequest(builder.Directory, solutionPath))).Unwrap().Diagnostics;
            case "find-unused":
                return (await new FindUnusedOperation(sessions, new FindUnusedService())
                    .ExecuteAsync(new FindUnusedRequest())).Unwrap().Diagnostics;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private sealed class FixedCapabilityCatalog : IProductCapabilityCatalog
    {
        public IReadOnlyList<ApplicationCommandDefinition> GetDefinitions() => [DoctorCommandDefinition.Definition];
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
            => Task.FromResult((workspace, solution));
    }
}
