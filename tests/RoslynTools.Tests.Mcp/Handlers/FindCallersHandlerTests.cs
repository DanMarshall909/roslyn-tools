using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class RelationshipsCallersToolTests
{
    [Fact]
    public async Task relationships_bounds_the_serialized_ambiguity_error_for_many_matches()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Workers.cs", string.Join(Environment.NewLine,
            Enumerable.Range(0, 80).Select(index =>
                $"namespace N{index:D3} {{ public sealed class Worker {{ public void ExecuteAsync() {{ }} }} }}")));
        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"ExecuteAsync","mode":"Callers","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("relationships", args.RootElement);

            result.isError.ShouldBeTrue();
            Encoding.UTF8.GetByteCount(result.content).ShouldBeLessThanOrEqualTo(8_192);
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe("AmbiguousMatch");
            var context = payload.RootElement.GetProperty("context");
            context.GetProperty("candidateCount").GetString().ShouldBe("80");
            context.GetProperty("candidatesTruncated").GetString().ShouldBe("true");
            context.GetProperty("candidates").GetString()
                !.ShouldContain("N000.Worker.ExecuteAsync()");
            context.GetProperty("candidates").GetString()
                !.ShouldNotContain("N079.Worker.ExecuteAsync()");
        }
    }

    [Fact]
    public async Task relationships_returns_structured_ambiguity_error_without_prompt()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Decider.cs", """
                                        namespace TestApp;

                                        public class First
                                        {
                                            public void Decide()
                                            {
                                            }
                                        }

                                        public class Second
                                        {
                                            public void Decide()
                                            {
                                            }
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {"symbol":"Decide","mode":"Callers","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("relationships", args.RootElement);

            result.isError.ShouldBeTrue();
            result.content.ShouldNotContain("Enter number", Case.Sensitive);
            using var payload = JsonDocument.Parse(result.content);
            payload.RootElement.GetProperty("kind").GetString().ShouldBe("AmbiguousMatch");
            payload.RootElement.GetProperty("message").GetString()
                !.ShouldContain("TestApp.First.Decide()", Case.Sensitive);
            payload.RootElement.GetProperty("message").GetString()
                !.ShouldContain("TestApp.Second.Decide()", Case.Sensitive);
            payload.RootElement.GetProperty("context").GetProperty("candidates").GetString()
                !.ShouldContain("TestApp.First.Decide()", Case.Sensitive);
            payload.RootElement.GetProperty("context").GetProperty("candidates").GetString()
                !.ShouldContain("TestApp.Second.Decide()", Case.Sensitive);
            var context = payload.RootElement.GetProperty("context");
            context.GetProperty("candidateCount").GetString().ShouldBe("2");
            context.GetProperty("displayedCandidateCount").GetString().ShouldBe("2");
            context.GetProperty("candidatesTruncated").GetString().ShouldBe("false");
            context.GetProperty("disambiguateWith").GetString()
                !.ShouldContain("search-symbols");
        }
    }
}
