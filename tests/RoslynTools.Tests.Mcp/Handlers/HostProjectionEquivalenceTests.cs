using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public sealed class HostProjectionEquivalenceTests
{
    [Fact]
    public async Task read_only_prime_returns_the_same_result_without_a_workspace()
    {
        using var provider = McpTestHost.CreateProvider();
        using var arguments = JsonDocument.Parse("{}");

        var mcp = await provider.GetRequiredService<ToolRegistry>()
            .InvokeAsync("prime", arguments.RootElement);
        mcp.isError.ShouldBeFalse(mcp.content);

        var originalOut = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            (await App.RunAsync(["prime", "--json"])).ShouldBe(0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var cli = JsonDocument.Parse(output.ToString());
        var mcpPayload = mcp.structuredContent!.Value;
        cli.RootElement.GetProperty("gate").GetString()
            .ShouldBe(mcpPayload.GetProperty("gate").GetString());
        cli.RootElement.GetProperty("entry").GetString()
            .ShouldBe(mcpPayload.GetProperty("entry").GetString());
        cli.RootElement.GetProperty("note").GetString()
            .ShouldBe(mcpPayload.GetProperty("note").GetString());
        cli.RootElement.GetProperty("hotCommands").EnumerateArray()
            .Select(item => item.GetString())
            .ShouldBe(mcpPayload.GetProperty("hotCommands").EnumerateArray()
                .Select(item => item.GetString()));
    }

    [Fact]
    public async Task structured_describe_preserves_defaults_output_and_errors_across_hosts()
    {
        const string sourceText = "namespace TestApp; public class Widget { public void Run() { } }";
        using var builder = new AdhocProjectBuilder("TestApp")
            .AddSource("Widget.cs", sourceText);
        var (workspace, solution) = builder.Build();
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory));
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            symbol = "TestApp.Widget",
            solution = builder.SolutionPath
        }));

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var mcp = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync("describe", arguments.RootElement);
            mcp.isError.ShouldBeFalse(mcp.content);

            var originalOut = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                (await App.RunAsync(
                    ["describe", "TestApp.Widget", "--solution", builder.SolutionPath]))
                    .ShouldBe(0);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            using var cli = JsonDocument.Parse(output.ToString());
            var cliPayload = cli.RootElement;
            var mcpPayload = mcp.structuredContent!.Value;
            cliPayload.GetProperty("fqn").GetString()
                .ShouldBe(mcpPayload.GetProperty("fqn").GetString());
            cliPayload.GetProperty("kind").GetString()
                .ShouldBe(mcpPayload.GetProperty("kind").GetString());
            cliPayload.GetProperty("signatures").EnumerateArray()
                .Select(item => item.GetString())
                .ShouldBe(mcpPayload.GetProperty("signatures").EnumerateArray()
                    .Select(item => item.GetString()));
            mcpPayload.GetProperty("signatures").EnumerateArray()
                .Select(item => item.GetString())
                .ShouldContain("void Run()");

            using var missing = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                symbol = "TestApp.Missing",
                solution = builder.SolutionPath
            }));
            var mcpError = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync("describe", missing.RootElement);
            mcpError.isError.ShouldBeTrue();
            mcpError.content.ShouldContain("TestApp.Missing");

            var originalError = Console.Error;
            using var diagnostics = new StringWriter();
            Console.SetError(diagnostics);
            try
            {
                (await App.RunAsync(
                    ["describe", "TestApp.Missing", "--solution", builder.SolutionPath]))
                    .ShouldNotBe(0);
            }
            finally
            {
                Console.SetError(originalError);
            }

            diagnostics.ToString().ShouldContain("TestApp.Missing");
            File.ReadAllText(Path.Combine(builder.ProjectDir, "Widget.cs")).ShouldBe(sourceText);
        }
    }

    [Fact]
    public async Task cli_only_operational_help_remains_available_without_mcp_exposure()
    {
        using var provider = McpTestHost.CreateProvider();
        var tools = provider.GetRequiredService<ToolRegistry>().GetTools();
        tools.ShouldContain(tool => tool.name == "prime");
        tools.ShouldNotContain(tool => tool.name == "dump_help" || tool.name == "daemon");

        (await App.RunAsync(["dump-help", "--help"])).ShouldBe(0);
    }
}
