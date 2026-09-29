using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public sealed class ChangeSignatureHandlerTests
{
    [Fact]
    public void descriptor_uses_the_shared_typed_port_and_requires_a_trusted_host()
    {
        using var untrustedProvider = McpTestHost.CreateProvider();
        using var trustedProvider = McpTestHost.CreateProvider(
            new McpHostOptions(Directory.GetCurrentDirectory()));
        var descriptor = trustedProvider.GetRequiredService<ApplicationCommandCatalog>()
            .All.Single(candidate => candidate.ToolName == ChangeSignatureCommandDefinition.Definition.Name);
        var tool = trustedProvider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == ChangeSignatureCommandDefinition.Definition.Name);

        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(typeof(ChangeSignatureRequest));
        descriptor.Port.ResponseType.ShouldBe(typeof(MutationExecutionResult<ChangeSignatureResult>));
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.DryRunByDefault);
        descriptor.Effects.ShouldBe(CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite);
        tool.annotations.readOnlyHint.ShouldBeFalse();
        tool.annotations.destructiveHint.ShouldBeTrue();
        untrustedProvider.GetRequiredService<ToolRegistry>().GetTools()
            .ShouldNotContain(candidate => candidate.name == ChangeSignatureCommandDefinition.Definition.Name);
    }

    [Fact]
    public void schema_exposes_four_operation_specific_request_shapes()
    {
        using var provider = McpTestHost.CreateProvider(
            new McpHostOptions(Directory.GetCurrentDirectory()));
        var tool = provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == ChangeSignatureCommandDefinition.Definition.Name);
        using var schema = JsonDocument.Parse(JsonSerializer.Serialize(tool.inputSchema));
        var root = schema.RootElement;

        root.GetProperty("required").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["operation", "methodSpec"], ignoreOrder: true);
        root.GetProperty("properties").GetProperty("operation").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["rename-param", "reorder", "add-param", "remove-param"], ignoreOrder: true);

        var requiredByOperation = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["rename-param"] = ["methodSpec", "operation", "oldName", "newName"],
            ["reorder"] = ["methodSpec", "operation", "order"],
            ["add-param"] = ["methodSpec", "operation", "paramSpec"],
            ["remove-param"] = ["methodSpec", "operation", "paramName"]
        };
        var variants = root.GetProperty("oneOf").EnumerateArray().ToArray();
        variants.Length.ShouldBe(4);
        foreach (var variant in variants)
        {
            var operation = variant.GetProperty("properties").GetProperty("operation")
                .GetProperty("enum")[0].GetString()!;
            requiredByOperation.ShouldContainKey(operation);
            variant.GetProperty("properties").GetProperty("methodSpec").GetProperty("type").GetString()
                .ShouldBe("string");
            variant.GetProperty("required").EnumerateArray().Select(item => item.GetString())
                .ShouldBe(requiredByOperation[operation], ignoreOrder: true);
            var forbidden = variant.GetProperty("not").GetProperty("anyOf").EnumerateArray()
                .Select(item => item.GetProperty("required")[0].GetString())
                .ToArray();
            foreach (var requiredName in requiredByOperation[operation])
                forbidden.ShouldNotContain(requiredName);
            forbidden.ShouldNotBeEmpty();
        }

        tool.description.ShouldContain("Edit");
        var cliExamples = tool.metadata["rt/cliExamples"]
            .ShouldBeAssignableTo<IReadOnlyList<string>>();
        foreach (var operation in requiredByOperation.Keys)
        {
            tool.description.ShouldContain($"\"operation\":\"{operation}\"");
            cliExamples.ShouldContain(example => example.Contains(operation, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("rename-param", "oldName", "first", "newName", "renamed")]
    [InlineData("reorder", "order", "second,first", null, null)]
    [InlineData("add-param", "paramSpec", "int added = 0", null, null)]
    [InlineData("remove-param", "paramName", "second", "force", "true")]
    public async Task every_operation_has_equivalent_cli_and_mcp_dry_run_results(
        string operation,
        string firstKey,
        string firstValue,
        string? secondKey,
        string? secondValue)
    {
        const string sourceText = "namespace TestApp; public class Service { public void Foo(int first, int second) { } }";
        const string callerText = "namespace TestApp; public class Caller { public void Run(Service service) => service.Foo(1, 2); }";
        using var builder = new AdhocProjectBuilder("TestApp")
            .AddSource("Service.cs", sourceText)
            .AddSource("Caller.cs", callerText);
        var (workspace, solution) = builder.Build();
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory));
        var input = new Dictionary<string, object>
        {
            ["operation"] = operation,
            ["methodSpec"] = "TestApp.Service.Foo",
            ["solution"] = builder.SolutionPath,
            [firstKey] = firstKey == "order" ? firstValue.Split(',') : firstValue
        };
        if (secondKey is not null)
            input[secondKey] = secondKey == "force" ? true : secondValue!;
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(input));

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync(ChangeSignatureCommandDefinition.Definition.Name, arguments.RootElement);

            result.isError.ShouldBeFalse(result.content);
            var payload = result.structuredContent!.Value;
            payload.GetProperty("status").GetString().ShouldBe(nameof(MutationExecutionStatus.DryRun));
            payload.GetProperty("value").GetProperty("changed").GetBoolean().ShouldBeTrue();

            var cliArguments = new List<string>
            {
                "change-signature", operation, "TestApp.Service.Foo", firstValue
            };
            if (secondKey == "newName")
                cliArguments.Add(secondValue!);
            if (secondKey == "force")
                cliArguments.Add("--force");
            cliArguments.AddRange(["--solution", builder.SolutionPath]);

            var originalOut = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                (await App.RunAsync([.. cliArguments])).ShouldBe(0);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            using var cliResult = JsonDocument.Parse(output.ToString());
            var cliPayload = cliResult.RootElement.GetProperty("data");
            cliPayload.GetProperty("status").GetInt32().ShouldBe((int)MutationExecutionStatus.DryRun);
            cliPayload.GetProperty("value").GetProperty("changed").GetBoolean()
                .ShouldBe(payload.GetProperty("value").GetProperty("changed").GetBoolean());
            cliPayload.GetProperty("value").GetProperty("mode").GetString()
                .ShouldBe(payload.GetProperty("value").GetProperty("mode").GetString());
            cliPayload.GetProperty("value").GetProperty("mode").GetString()
                .ShouldBe(operation switch
                {
                    "rename-param" => nameof(ChangeSignatureMode.RenameParam),
                    "reorder" => nameof(ChangeSignatureMode.Reorder),
                    "add-param" => nameof(ChangeSignatureMode.AddParam),
                    _ => nameof(ChangeSignatureMode.RemoveParam)
                });
            cliPayload.GetProperty("changedFiles").EnumerateArray()
                .Select(item => item.GetString())
                .ShouldBe(payload.GetProperty("changedFiles").EnumerateArray()
                    .Select(item => item.GetString()), ignoreOrder: true);
            File.ReadAllText(Path.Combine(builder.ProjectDir, "Service.cs")).ShouldBe(sourceText);
            File.ReadAllText(Path.Combine(builder.ProjectDir, "Caller.cs")).ShouldBe(callerText);
        }
    }

    [Theory]
    [InlineData("[]", "must be a JSON object")]
    [InlineData("{}", "operation")]
    [InlineData("{\"operation\":\"unsupported\",\"methodSpec\":\"TestApp.Service.Foo\"}", "Invalid operation")]
    [InlineData("{\"operation\":0,\"methodSpec\":\"TestApp.Service.Foo\",\"oldName\":\"first\",\"newName\":\"renamed\"}", "Invalid operation")]
    [InlineData("{\"operation\":99,\"methodSpec\":\"TestApp.Service.Foo\"}", "Invalid operation")]
    [InlineData("{\"operation\":\"rename-param\",\"Operation\":\"remove-param\",\"methodSpec\":\"TestApp.Service.Foo\"}", "Duplicate tool argument")]
    [InlineData("{\"operation\":\"rename-param\",\"methodSpec\":\"TestApp.Service.Foo\"}", "requires 'oldName'")]
    [InlineData("{\"operation\":\"rename-param\",\"methodSpec\":\"TestApp.Service.Foo\",\"oldName\":\"first\",\"newName\":\"renamed\",\"force\":true}", "does not allow 'force'")]
    public async Task invalid_operation_requests_are_rejected_before_workspace_load(
        string json,
        string expectedDiagnostic)
    {
        using var provider = McpTestHost.CreateProvider(
            new McpHostOptions(Directory.GetCurrentDirectory()));
        using var arguments = JsonDocument.Parse(json);

        var result = await provider.GetRequiredService<ToolRegistry>()
            .InvokeAsync(ChangeSignatureCommandDefinition.Definition.Name, arguments.RootElement);

        result.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(result.content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
        error.RootElement.GetProperty("message").GetString()!
            .ShouldContain(expectedDiagnostic, Case.Insensitive);
    }

    [Fact]
    public async Task cli_and_mcp_reject_an_unknown_operation_before_workspace_load()
    {
        using var provider = McpTestHost.CreateProvider(
            new McpHostOptions(Directory.GetCurrentDirectory()));
        using var arguments = JsonDocument.Parse("""
            {"operation":"unsupported","methodSpec":"TestApp.Service.Foo"}
            """);
        var mcpResult = await provider.GetRequiredService<ToolRegistry>()
            .InvokeAsync(ChangeSignatureCommandDefinition.Definition.Name, arguments.RootElement);

        mcpResult.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(mcpResult.content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));

        var originalError = Console.Error;
        using var diagnostics = new StringWriter();
        Console.SetError(diagnostics);
        try
        {
            (await App.RunAsync(["change-signature", "unsupported", "TestApp.Service.Foo"]))
                .ShouldNotBe(0);
        }
        finally
        {
            Console.SetError(originalError);
        }

        diagnostics.ToString().ShouldContain("unsupported", Case.Insensitive);
    }

}
