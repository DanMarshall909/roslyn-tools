using System.CommandLine.Parsing;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Suggest;

public sealed class DiscoveryCatalogTests
{
    [Theory]
    [InlineData("inspect a type public members", "outline", "describe")]
    [InlineData("find callers of a method", "relationships", "relationships")]
    [InlineData("move a type across projects", "move-slice", "move")]
    [InlineData("extract an interface from a class", "extract-interface", "extract-interface")]
    [InlineData("replace constructor calls with a factory method", "replace-new", "replace-new")]
    [InlineData("enforce architecture boundaries", "check-architecture", "check")]
    [InlineData("record feedback about a tool problem", "feedback", "feedback")]
    public void common_intents_find_a_relevant_command_in_the_real_catalogue(
        string query, string expected, string alternative)
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var response = provider.GetRequiredService<SuggestService>().Suggest(new SuggestRequest(query, Top: 3));

        response.Results.Count.ShouldBeLessThanOrEqualTo(3);
        response.Results.ShouldContain(result => result.Name == expected || result.Name == alternative);
    }

    [Fact]
    public void implementation_search_uses_the_real_read_route_before_interface_mutators()
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var response = provider.GetRequiredService<SuggestService>().Suggest(new SuggestRequest(
            "Find every implementation of IRtCommand before changing the interface", 3,
            Path.GetFullPath("RoslynTools.slnx")));

        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldNotBeNull();
        response.Workflow.Steps.Single().McpTool.ShouldBe("relationships");
    }

    [Fact]
    public async Task advertised_feedback_examples_parse_on_the_real_cli_without_execution()
    {
        foreach (var example in HostCommandMetadata.Feedback.Examples)
        {
            var arguments = CommandLineStringSplitter.Instance.Split(example).Skip(1).ToArray();
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { typeof(App).Assembly.Location, "[parse]" }.Concat(arguments))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);

            process.ExitCode.ShouldBe(0, example + Environment.NewLine + await stdout + await stderr);
        }
    }
}
