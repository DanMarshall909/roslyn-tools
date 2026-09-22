using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class MoveSliceHandlerTests
{
    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }

    [Fact]
    public void Descriptor_is_a_typed_review_default_mutation_hidden_from_untrusted_hosts()
    {
        using var untrustedProvider = McpTestHost.CreateProvider();
        using var trustedProvider = McpTestHost.CreateProvider(
            new McpHostOptions(Directory.GetCurrentDirectory()));
        var descriptor = trustedProvider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == MoveSliceCommandDefinition.Definition.Name);
        var tool = trustedProvider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == MoveSliceCommandDefinition.Definition.Name);

        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(typeof(MoveSliceRequest));
        descriptor.Port.ResponseType.ShouldBe(typeof(MutationExecutionResult<MoveSliceResult>));
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.DryRunByDefault);
        descriptor.Effects.ShouldBe(CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite);
        tool.annotations.readOnlyHint.ShouldBeFalse();
        tool.annotations.destructiveHint.ShouldBeTrue();
        var required = tool.inputSchema["required"].ShouldBeOfType<string[]>();
        required.ShouldBe(["to-project", "to-namespace"], ignoreOrder: true);
        var properties = tool.inputSchema["properties"]
            .ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();
        properties.Keys.ShouldContain("types");
        properties.Keys.ShouldContain("namespace");
        properties.Keys.ShouldContain("dry-run");
        properties.Keys.ShouldContain("add-internals-visible-to");
        untrustedProvider.GetRequiredService<ToolRegistry>().GetTools()
            .ShouldNotContain(candidate => candidate.name == MoveSliceCommandDefinition.Definition.Name);
    }

    [Fact]
    public async Task Trusted_host_dry_run_invokes_the_shared_port_and_leaves_files_untouched()
    {
        const string sourceText = "namespace Legacy; public sealed class Service { }";
        using var builder = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", sourceText);
        builder.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = builder.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["Source", "Target"]);
        WriteProjectFiles(solution);
        var sourcePath = Path.Combine(builder.Directory, "Source", "Service.cs");
        var targetPath = Path.Combine(builder.Directory, "Target", "Feature", "Service.cs");

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory)))
        using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["types"] = new List<string> { "Legacy.Service" },
            ["to-project"] = "Target",
            ["to-namespace"] = "App.Target.Feature"
        })))
        {
            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync(MoveSliceCommandDefinition.Definition.Name, arguments.RootElement);

            result.isError.ShouldBeFalse(result.content);
            result.structuredContent.ShouldNotBeNull();
            var payload = result.structuredContent!.Value;
            payload.GetProperty("status").GetString().ShouldBe(nameof(MutationExecutionStatus.DryRun));
            payload.GetProperty("value").GetProperty("applicable").GetBoolean().ShouldBeTrue();
            File.ReadAllText(sourcePath).ShouldBe(sourceText);
            File.Exists(targetPath).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task persistent_trust_discovery_does_not_authorize_a_different_repository_target()
    {
        using var builder = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", "namespace Legacy; public sealed class Service { }");
        builder.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = builder.Build();
        WriteProjectFiles(solution);
        var registryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-mutation-trust-{Guid.NewGuid():N}");
        try
        {
            var registry = new RepositoryTrustRegistry(
                Path.Combine(registryDirectory, "repository-trust.json"));
            registry.Add(FindRepoRoot()).IsOk.ShouldBeTrue();
            var untrustedSolution = Path.Combine(registryDirectory, "Other.sln");
            File.WriteAllText(untrustedSolution, string.Empty);
            using var provider = McpTestHost.CreateProvider(
                new McpHostOptions(trustRegistry: registry));
            provider.GetRequiredService<ToolSchemaBuilder>().Build()
                .ShouldContain(tool => tool.name == MoveSliceCommandDefinition.Definition.Name);
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["types"] = new List<string> { "Legacy.Service" },
                ["to-project"] = "Target",
                ["to-namespace"] = "App.Target.Feature",
                ["solution"] = untrustedSolution
            }));
            using (workspace)
            {
                var result = await provider.GetRequiredService<ToolRegistry>()
                    .InvokeAsync(MoveSliceCommandDefinition.Definition.Name, arguments.RootElement);

                result.isError.ShouldBeTrue();
                result.content.ShouldContain(nameof(RtErrorKind.TrustRequired));
                result.content.ShouldNotContain("Unknown tool");
            }
        }
        finally
        {
            if (Directory.Exists(registryDirectory))
                Directory.Delete(registryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Trusted_stdio_protocol_discovers_and_invokes_move_slice_as_a_dry_run()
    {
        const string sourceText = "namespace Legacy; public sealed class Service { }";
        using var builder = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", sourceText);
        builder.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = builder.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["Source", "Target"]);
        WriteProjectFiles(solution);
        var sourcePath = Path.Combine(builder.Directory, "Source", "Service.cs");
        var targetPath = Path.Combine(builder.Directory, "Target", "Feature", "Service.cs");
        var request = string.Join(
            Environment.NewLine,
            """{"jsonrpc":"2.0","method":"tools/list","id":1}""",
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "tools/call",
                id = 2,
                @params = new
                {
                    name = MoveSliceCommandDefinition.Definition.Name,
                    arguments = new Dictionary<string, object>
                    {
                        ["types"] = new List<string> { "Legacy.Service" },
                        ["to-project"] = "Target",
                        ["to-namespace"] = "App.Target.Feature",
                        ["solution"] = builder.SolutionPath
                    }
                }
            }));
        using var output = new StringWriter();
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(
            new StringReader(request + Environment.NewLine),
            output,
            new McpHostOptions(builder.Directory));

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = services.BuildServiceProvider())
        {
            await provider.GetRequiredService<Server>().RunAsync();
        }

        var responses = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        responses.Length.ShouldBe(2);
        using var discovery = JsonDocument.Parse(responses[0]);
        discovery.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .ShouldContain(tool =>
                tool.GetProperty("name").GetString() == MoveSliceCommandDefinition.Definition.Name);
        using var invocation = JsonDocument.Parse(responses[1]);
        var result = invocation.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeFalse();
        result.GetProperty("structuredContent").GetProperty("status")
            .GetString().ShouldBe(nameof(MutationExecutionStatus.DryRun));
        File.ReadAllText(sourcePath).ShouldBe(sourceText);
        File.Exists(targetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Trusted_stdio_dependency_analysis_ignores_constructed_target_types()
    {
        using var builder = BuildWageClaimsRuleItemFixture();
        var (workspace, solution) = builder.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["Infrastructure", "Application"]);
        WriteProjectFiles(solution);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            id = 1,
            @params = new
            {
                name = MoveSliceCommandDefinition.Definition.Name,
                arguments = new Dictionary<string, object>
                {
                    ["types"] = new List<string>
                    {
                        "WageClaim.Function.Services.IRuleItemManagementService",
                        "WageClaim.Function.Services.RuleItemManagementService"
                    },
                    ["to-project"] = "Application",
                    ["to-namespace"] = "WageClaims.Application.Rules",
                    ["analyze-dependencies"] = true,
                    ["solution"] = builder.SolutionPath
                }
            }
        });
        using var output = new StringWriter();
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(
            new StringReader(request + Environment.NewLine),
            output,
            new McpHostOptions(builder.Directory));

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = services.BuildServiceProvider())
        {
            await provider.GetRequiredService<Server>().RunAsync();
        }

        using var invocation = JsonDocument.Parse(output.ToString());
        invocation.RootElement.TryGetProperty("error", out _).ShouldBeFalse();
        var result = invocation.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeFalse();
        var value = result.GetProperty("structuredContent").GetProperty("value");
        value.GetProperty("applicable").GetBoolean().ShouldBeTrue();
        value.GetProperty("requiredRelatedTypes").GetArrayLength().ShouldBe(0);
        value.GetProperty("projectEdits").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Trusted_stdio_protocol_returns_a_typed_error_for_a_source_generated_partial_type()
    {
        using var builder = new AdhocProjectBuilder("Source")
            .AddSource(
                "Service.cs",
                "namespace Legacy; public sealed partial class Service { }");
        builder.AddProject("Source").AddGeneratedSource(
            "Service.g.cs",
            "namespace Legacy; public sealed partial class Service { public int Generated => 1; }");
        builder.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = builder.Build();
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["Source", "Target"],
            new Dictionary<string, string[]> { ["Source"] = ["Service.g.cs"] });
        WriteProjectFiles(solution);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            id = 1,
            @params = new
            {
                name = MoveSliceCommandDefinition.Definition.Name,
                arguments = new Dictionary<string, object>
                {
                    ["types"] = new List<string> { "Legacy.Service" },
                    ["to-project"] = "Target",
                    ["to-namespace"] = "App.Target.Feature",
                    ["analyze-dependencies"] = true,
                    ["dry-run"] = true,
                    ["solution"] = builder.SolutionPath
                }
            }
        });
        using var output = new StringWriter();
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(
            new StringReader(request + Environment.NewLine),
            output,
            new McpHostOptions(builder.Directory));

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = services.BuildServiceProvider())
        {
            await provider.GetRequiredService<Server>().RunAsync();
        }

        using var invocation = JsonDocument.Parse(output.ToString());
        invocation.RootElement.TryGetProperty("error", out _).ShouldBeFalse();
        var result = invocation.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
        var content = result.GetProperty("content")[0].GetProperty("text").GetString();
        content.ShouldNotBeNull();
        using var error = JsonDocument.Parse(content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
        var message = error.RootElement.GetProperty("message").GetString();
        message.ShouldNotBeNull();
        message!.ShouldContain("source-generated declaration");
    }

    private static void WriteProjectFiles(Solution solution)
    {
        foreach (var project in solution.Projects)
            File.WriteAllText(project.FilePath!, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    }

    private static AdhocProjectBuilder BuildWageClaimsRuleItemFixture()
    {
        var fixture = new AdhocProjectBuilder("Infrastructure")
            .AddSource(
                "IRuleItemManagementService.cs",
                """
                using System.Threading.Tasks;
                using WageClaims.Application.Profiles;
                using WageClaims.Application.Rules;
                namespace WageClaim.Function.Services;
                public interface IRuleItemManagementService
                {
                    Task<(ProfileHeaderRecord header, RuleVersionFence version)> LoadAsync();
                }
                """)
            .AddSource(
                "RuleItemManagementService.cs",
                """
                using System.Threading.Tasks;
                using WageClaims.Application.Profiles;
                using WageClaims.Application.Rules;
                namespace WageClaim.Function.Services;
                public sealed class RuleItemManagementService : IRuleItemManagementService
                {
                    public Task<(ProfileHeaderRecord header, RuleVersionFence version)> LoadAsync() =>
                        Task.FromResult((new ProfileHeaderRecord(), new RuleVersionFence()));

                    public async Task<ProfileHeaderRecord> LoadHeaderAsync()
                    {
                        var result = await LoadAsync();
                        return result.header;
                    }
                }
                """);
        fixture.AddProject("Application")
            .WithDefaultNamespace("WageClaims.Application")
            .AddSource(
                "Profiles/ProfileHeaderRecord.cs",
                "namespace WageClaims.Application.Profiles; public sealed record ProfileHeaderRecord;")
            .AddSource(
                "Rules/RuleVersionFence.cs",
                "namespace WageClaims.Application.Rules; public sealed record RuleVersionFence;");
        fixture.AddProject("Infrastructure").ReferenceProject("Application");
        return fixture;
    }
}
