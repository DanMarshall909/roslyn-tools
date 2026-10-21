using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

[Collection("mcp-bridge")]
public sealed class SavedTrustAdapterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-trust-adapter-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("malformed")]
    [InlineData("version")]
    [InlineData("oversized")]
    [InlineData("relative-root")]
    public async Task invalid_saved_registry_returns_tool_error_before_workspace_loading(string scenario)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "trusted-roots.json");
        var contents = scenario switch
        {
            "malformed" => "invalid JSON",
            "version" => "{\"version\":2,\"roots\":[]}",
            "oversized" => new string(' ', 1_048_577),
            _ => "{\"version\":1,\"roots\":[\"relative\"]}"
        };
        await File.WriteAllTextAsync(path, contents);
        var services = new ServiceCollection();
        Bootstrap.RegisterServices(services);
        services.AddSingleton(new RepositoryTrustStore(path));
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter(), new McpHostOptions(useTrustedRoots: true));
        using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<McpCommandAdapter>().InvokeAsync("find_unused",
            JsonSerializer.SerializeToElement(new { solution = Path.Combine(_directory, "MustNotLoad.slnx") }));

        result.isError.ShouldBeTrue();
        using var document = JsonDocument.Parse(result.content);
        document.RootElement.GetProperty("kind").GetString().ShouldBe("TrustRequired");
        document.RootElement.GetProperty("message").GetString().ShouldContain("Saved trust could not be loaded");
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
        File.ReadAllText(path).ShouldBe(contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
