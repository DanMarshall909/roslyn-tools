using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public class FindCallersToolTests
{
    [Fact]
    public async Task find_callers_returns_structured_ambiguity_error_without_prompt()
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
                                                {"symbol":"Decide","solution":"ignored.slnx"}
                                                """);

            var result = await registry.InvokeAsync("find_callers", args.RootElement);

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
        }
    }
}
