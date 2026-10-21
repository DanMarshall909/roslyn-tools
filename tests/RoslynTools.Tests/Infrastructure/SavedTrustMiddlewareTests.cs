using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class SavedTrustMiddlewareTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rt-trust-middleware-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("malformed")]
    [InlineData("version")]
    [InlineData("oversized")]
    [InlineData("relative-root")]
    public async Task invalid_saved_registry_returns_typed_error_without_running_command(string scenario)
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
        using var provider = services.BuildServiceProvider();
        var invoked = false;
        var command = new Command("trust-boundary-probe");
        command.SetHandler(() => invoked = true);
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var previousFormat = RtContext.Format;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = await App.RunAsync(["trust-boundary-probe", "--use-trusted-roots", "--json"], provider, [command]);
            exitCode.ShouldBe(1);
            invoked.ShouldBeFalse();
            using var document = JsonDocument.Parse(output.ToString());
            document.RootElement.GetProperty("kind").GetString().ShouldBe("TrustRequired");
            document.RootElement.GetProperty("message").GetString().ShouldContain("Saved trust could not be loaded");
            document.RootElement.GetProperty("message").GetString().ShouldContain("--trust-repository");
            RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
            File.ReadAllText(path).ShouldBe(contents);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
            RtContext.Format = previousFormat;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
