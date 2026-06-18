using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

/// <summary>
///     US-135 Batch 1 polish — MCP-side coverage for the unified JSON encoder helper.
///     Mirrors the CLI tests in <c>RoslynTools.Tests.Infrastructure.JsonEncoderUnification</c>:
///     <list type="bullet">
///         <item>Default behaviour escapes non-ASCII + HTML chars (downstream-deserialiser safe).</item>
///         <item><see cref="RtContext.RelaxJson" /> = true emits raw output.</item>
///         <item>The MCP describe handler keeps camelCase property keys after the helper migration.</item>
///     </list>
/// </summary>
[Collection("mcp-bridge")]
public class JsonEncoderUnificationTests
{
    [Fact]
    public async Task mcp_describe_handler_mirrors_cli_encoder_behaviour_strict_by_default()
    {
        // Test C — the native MCP DescribeHandler reads RtContext.RelaxJson; default false
        // means non-ASCII / HTML-sensitive chars come back as \uXXXX.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                           public System.Threading.Tasks.Task<string> WorkAsync() =>
                                               System.Threading.Tasks.Task.FromResult("a→b");
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"TestApp.Widget","solution":"ignored.slnx"}
                                                """);

            var previousRelax = RtContext.RelaxJson;
            try
            {
                RtContext.RelaxJson = false;
                var result = await registry.InvokeAsync("describe", args.RootElement);

                result.isError.ShouldBeFalse();
                // Task<string> in the signature exercises the < escape.
                result.content.ShouldContain("\\u003C");
                result.content.ShouldNotContain("Task<string>");
            }
            finally
            {
                RtContext.RelaxJson = previousRelax;
            }
        }
    }

    [Fact]
    public async Task mcp_describe_handler_emits_raw_chars_when_relax_json_is_set()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                           public System.Threading.Tasks.Task<string> WorkAsync() =>
                                               System.Threading.Tasks.Task.FromResult("a");
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"TestApp.Widget","solution":"ignored.slnx"}
                                                """);

            var previousRelax = RtContext.RelaxJson;
            try
            {
                RtContext.RelaxJson = true;
                var result = await registry.InvokeAsync("describe", args.RootElement);

                result.isError.ShouldBeFalse();
                result.content.ShouldContain("Task<string>");
                result.content.ShouldNotContain("\\u003C");
            }
            finally
            {
                RtContext.RelaxJson = previousRelax;
            }
        }
    }

    [Fact]
    public async Task mcp_describe_handler_keeps_camelcase_keys_after_helper_migration()
    {
        // Regression guard: DescribeHandler must still emit camelCase keys
        // (fqn / kind / signatures) — it was the only handler with that policy.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget
                                       {
                                           public void Activate() { }
                                       }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"TestApp.Widget","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("describe", args.RootElement);

            result.isError.ShouldBeFalse();
            result.content.ShouldContain("\"fqn\"", Case.Sensitive);
            result.content.ShouldContain("\"signatures\"", Case.Sensitive);
            result.content.ShouldNotContain("\"Fqn\"", Case.Sensitive);
            result.content.ShouldNotContain("\"Signatures\"", Case.Sensitive);
        }
    }
}
