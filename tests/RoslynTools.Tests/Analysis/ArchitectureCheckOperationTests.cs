using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class ArchitectureCheckOperationTests
{
    [Fact]
    public async Task ExecuteAsync_reports_project_semantic_package_framework_sdk_and_technology_violations()
    {
        using var fixture = CreateLayeredFixture();
        var (workspace, solution) = fixture.Builder.Build();
        WriteProjectFiles(fixture.Builder.Directory);
        WriteConfiguration(fixture.Builder.Directory);
        var operation = new ArchitectureCheckOperation(new FixedWorkspaceSessionFactory(workspace, solution));

        var result = Unwrap(await operation.ExecuteAsync(new ArchitectureCheckRequest(
            fixture.Builder.SolutionPath)));

        result.IsCompliant.ShouldBeFalse();
        result.CheckedProjectCount.ShouldBe(4);
        result.Violations.ShouldContain(violation =>
            violation.Rule == "layer-project-reference"
            && violation.Project == "Demo.Infrastructure"
            && violation.Dependency == "Demo.Functions");
        result.Violations.ShouldContain(violation =>
            violation.Rule == "layer-semantic-dependency"
            && violation.Project == "Demo.Infrastructure"
            && violation.Dependency == "Demo.Functions.FunctionEndpoint");
        result.Violations.ShouldContain(violation =>
            violation.Rule == "forbidden-package-reference"
            && violation.Dependency == "PackageReference: PdfSharpCore");
        result.Violations.ShouldContain(violation =>
            violation.Rule == "technology:http"
            && violation.Project == "Demo.Domain"
            && violation.Dependency == "System.Net.Http.HttpClient");
        result.Violations.ShouldContain(violation =>
            violation.Rule == "technology:aspnet"
            && violation.Dependency == "SDK: Microsoft.NET.Sdk.Web");
        result.Violations.ShouldContain(violation =>
            violation.Rule == "technology:aspnet"
            && violation.Dependency == "FrameworkReference: Microsoft.AspNetCore.App");
        result.Violations.ShouldAllBe(violation =>
            !string.IsNullOrWhiteSpace(violation.File)
            && violation.Line > 0
            && !string.IsNullOrWhiteSpace(violation.Source)
            && violation.BaselineState == "new");
    }

    [Fact]
    public async Task ExecuteAsync_keeps_baselined_violations_visible_and_counts_new_findings()
    {
        using var fixture = CreateLayeredFixture();
        var (workspace, solution) = fixture.Builder.Build();
        WriteProjectFiles(fixture.Builder.Directory);
        WriteConfiguration(fixture.Builder.Directory);
        var operation = new ArchitectureCheckOperation(new FixedWorkspaceSessionFactory(workspace, solution));
        var initial = Unwrap(await operation.ExecuteAsync(new ArchitectureCheckRequest(
            fixture.Builder.SolutionPath)));
        var baselinedFingerprint = initial.Violations[0].Fingerprint;
        var baselinePath = Path.Combine(fixture.Builder.Directory, ".rt", "architecture-baseline.json");
        await File.WriteAllTextAsync(baselinePath, JsonSerializer.Serialize(new
        {
            version = 1,
            fingerprints = new[] { baselinedFingerprint }
        }));

        var checkedResult = Unwrap(await operation.ExecuteAsync(new ArchitectureCheckRequest(
            fixture.Builder.SolutionPath,
            Baseline: ".rt/architecture-baseline.json")));

        checkedResult.ViolationCount.ShouldBe(initial.ViolationCount);
        checkedResult.BaselinedViolationCount.ShouldBe(1);
        checkedResult.NewViolationCount.ShouldBe(initial.ViolationCount - 1);
        checkedResult.Violations.Single(violation => violation.Fingerprint == baselinedFingerprint)
            .BaselineState.ShouldBe("existing");
    }

    [Fact]
    public async Task ExecuteAsync_rejects_configuration_outside_the_solution_directory()
    {
        using var builder = new AdhocProjectBuilder("Demo.Domain");
        builder.AddSource("Value.cs", "namespace Demo.Domain; public sealed class Value { }");
        var (workspace, solution) = builder.Build();
        WriteProjectFile(builder.Directory, "Demo.Domain", "Microsoft.NET.Sdk", "");
        var operation = new ArchitectureCheckOperation(new FixedWorkspaceSessionFactory(workspace, solution));

        var error = UnwrapError(await operation.ExecuteAsync(new ArchitectureCheckRequest(
            builder.SolutionPath,
            "../architecture.json")));

        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("outside solution directory");
    }

    [Fact]
    public async Task ExecuteAsync_rejects_invalid_configuration_json()
    {
        using var builder = new AdhocProjectBuilder("Demo.Domain");
        builder.AddSource("Value.cs", "namespace Demo.Domain; public sealed class Value { }");
        var (workspace, solution) = builder.Build();
        WriteProjectFile(builder.Directory, "Demo.Domain", "Microsoft.NET.Sdk", "");
        var configDirectory = Path.Combine(builder.Directory, ".rt");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(configDirectory, "architecture.json"),
            "{ \"version\": 1, \"layers\": [");
        var operation = new ArchitectureCheckOperation(new FixedWorkspaceSessionFactory(workspace, solution));

        var error = UnwrapError(await operation.ExecuteAsync(new ArchitectureCheckRequest(builder.SolutionPath)));

        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("invalid JSON");
    }

    [Fact]
    public async Task ExecuteAsync_fails_when_a_configured_project_pattern_matches_no_loaded_project()
    {
        using var builder = new AdhocProjectBuilder("Demo.Domain");
        builder.AddSource("Value.cs", "namespace Demo.Domain; public sealed class Value { }");
        var (workspace, solution) = builder.Build();
        WriteProjectFile(builder.Directory, "Demo.Domain", "Microsoft.NET.Sdk", "");
        var configDirectory = Path.Combine(builder.Directory, ".rt");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllTextAsync(Path.Combine(configDirectory, "architecture.json"), """
            {
              "version": 1,
              "layers": [
                { "name": "Domain", "projects": ["Demo.Domain"], "allowedProjectReferences": [] },
                { "name": "Missing", "projects": ["Demo.Missing"], "allowedProjectReferences": [] }
              ]
            }
            """);
        var operation = new ArchitectureCheckOperation(new FixedWorkspaceSessionFactory(workspace, solution));

        var error = UnwrapError(await operation.ExecuteAsync(new ArchitectureCheckRequest(builder.SolutionPath)));

        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("Demo.Missing");
        error.Message.ShouldContain("matched no loaded project");
    }

    [Fact]
    public async Task ExecuteAsync_fails_closed_when_a_configured_project_has_compilation_errors()
    {
        using var builder = new AdhocProjectBuilder("Demo.Domain");
        builder.AddSource("Broken.cs", "namespace Demo.Domain; public sealed class Broken { MissingType Value; }");
        var (workspace, solution) = builder.Build();
        WriteProjectFile(builder.Directory, "Demo.Domain", "Microsoft.NET.Sdk", "");
        WriteConfiguration(builder.Directory, domainOnly: true);
        var operation = new ArchitectureCheckOperation(new FixedWorkspaceSessionFactory(workspace, solution));

        var error = UnwrapError(await operation.ExecuteAsync(new ArchitectureCheckRequest(builder.SolutionPath)));

        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("compilation errors");
        error.Message.ShouldContain("MissingType");
    }

    [Fact]
    public async Task Cli_emits_machine_readable_evidence_and_exits_one_when_violations_exist()
    {
        using var fixture = CreateLayeredFixture();
        var (workspace, solution) = fixture.Builder.Build();
        WriteProjectFiles(fixture.Builder.Directory);
        WriteConfiguration(fixture.Builder.Directory);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            using var workspaceOverride = WorkspaceLoader.UseOverride(workspace, solution);
            var services = new ServiceCollection();
            global::RoslynTools.Bootstrap.RegisterServices(services);
            await using var provider = services.BuildServiceProvider();

            var exitCode = await App.RunAsync(
                [
                    "check-architecture",
                    "--solution", fixture.Builder.SolutionPath,
                    "--config", ".rt/architecture.json",
                    "--json"
                ],
                provider);

            exitCode.ShouldBe(1);
            using var payload = JsonDocument.Parse(stdout.ToString());
            payload.RootElement.GetProperty("isCompliant").GetBoolean().ShouldBeFalse();
            payload.RootElement.GetProperty("newViolationCount").GetInt32().ShouldBeGreaterThan(0);
            payload.RootElement.GetProperty("violations").GetArrayLength().ShouldBeGreaterThan(0);
            stderr.ToString().ShouldBeEmpty();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            workspace.Dispose();
        }
    }

    private static LayeredFixture CreateLayeredFixture()
    {
        var builder = new AdhocProjectBuilder("Demo.Domain");
        builder.AddSource("Claim.cs", """
            namespace Demo.Domain;
            public sealed class Claim
            {
                public System.Net.Http.HttpClient Client { get; } = new();
            }
            """);
        builder.AddProject("Demo.Application")
            .ReferenceProject("Demo.Domain")
            .AddSource("UseCase.cs", "namespace Demo.Application; public sealed class UseCase { Demo.Domain.Claim Value = new(); }");
        builder.AddProject("Demo.Functions")
            .AddSource("FunctionEndpoint.cs", "namespace Demo.Functions; public sealed class FunctionEndpoint { }");
        builder.AddProject("Demo.Infrastructure")
            .ReferenceProject("Demo.Functions")
            .AddSource("Adapter.cs", "namespace Demo.Infrastructure; public sealed class Adapter { Demo.Functions.FunctionEndpoint Value = new(); }");
        return new LayeredFixture(builder);
    }

    private static void WriteProjectFiles(string root)
    {
        WriteProjectFile(root, "Demo.Domain", "Microsoft.NET.Sdk.Web", """
            <ItemGroup>
              <FrameworkReference Include="Microsoft.AspNetCore.App" />
            </ItemGroup>
            """);
        WriteProjectFile(root, "Demo.Application", "Microsoft.NET.Sdk", """
            <ItemGroup>
              <ProjectReference Include="..\Demo.Domain\Demo.Domain.csproj" />
            </ItemGroup>
            """);
        WriteProjectFile(root, "Demo.Functions", "Microsoft.NET.Sdk", "");
        WriteProjectFile(root, "Demo.Infrastructure", "Microsoft.NET.Sdk", """
            <ItemGroup>
              <ProjectReference Include="..\Demo.Functions\Demo.Functions.csproj" />
              <PackageReference Include="PdfSharpCore" Version="1.3.65" />
            </ItemGroup>
            """);
    }

    private static void WriteProjectFile(string root, string project, string sdk, string content)
    {
        File.WriteAllText(Path.Combine(root, project, project + ".csproj"), $$"""
            <Project Sdk="{{sdk}}">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              {{content}}
            </Project>
            """);
    }

    private static void WriteConfiguration(string root, bool domainOnly = false)
    {
        var directory = Path.Combine(root, ".rt");
        Directory.CreateDirectory(directory);
        var layers = domainOnly
            ? """
              [{ "name": "Domain", "projects": ["Demo.Domain"], "allowedProjectReferences": [] }]
              """
            : """
              [
                { "name": "Domain", "projects": ["Demo.Domain"], "allowedProjectReferences": [] },
                { "name": "Application", "projects": ["Demo.Application"], "allowedProjectReferences": ["Domain"] },
                { "name": "Infrastructure", "projects": ["Demo.Infrastructure"], "allowedProjectReferences": ["Application", "Domain"], "forbiddenPackageReferences": ["PdfSharp*"] },
                { "name": "Functions", "projects": ["Demo.Functions"], "allowedProjectReferences": ["Application", "Infrastructure"] }
              ]
              """;
        var forbiddenTechnologyLayers = domainOnly ? "[\"Domain\"]" : "[\"Domain\", \"Application\"]";
        File.WriteAllText(Path.Combine(directory, "architecture.json"), $$"""
            {
              "version": 1,
              "layers": {{layers}},
              "technologyRules": [
                {
                  "name": "http",
                  "forbiddenInLayers": {{forbiddenTechnologyLayers}},
                  "namespacePrefixes": ["System.Net.Http"]
                },
                {
                  "name": "aspnet",
                  "forbiddenInLayers": {{forbiddenTechnologyLayers}},
                  "sdks": ["Microsoft.NET.Sdk.Web"],
                  "frameworkReferences": ["Microsoft.AspNetCore.App"],
                  "namespacePrefixes": ["Microsoft.AspNetCore"]
                }
              ]
            }
            """);
    }

    private static T Unwrap<T>(Result<T, RtError> result) =>
        result.Match(value => value, error => throw new ShouldAssertException(error.Message));

    private static RtError UnwrapError<T>(Result<T, RtError> result) =>
        result.Match(
            _ => throw new ShouldAssertException("Expected a typed error."),
            error => error);

    private sealed record LayeredFixture(AdhocProjectBuilder Builder) : IDisposable
    {
        public void Dispose() => Builder.Dispose();
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
