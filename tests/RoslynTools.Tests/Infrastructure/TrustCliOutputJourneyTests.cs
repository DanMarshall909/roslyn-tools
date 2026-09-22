using System.Diagnostics;
using System.CommandLine;
using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class TrustCliOutputJourneyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task trust_json_lifecycle_uses_the_global_encoder_policy(bool relaxJson)
    {
        using var fixture = TrustJourneyFixture.Create();
        var options = relaxJson ? "--json --relax-json" : "--json";

        var add = await RunTrustAsync(fixture, $"{options} trust add \"{fixture.RepositoryPath}\"");
        var status = await RunTrustAsync(fixture, $"{options} trust status \"{fixture.RepositoryPath}\"");
        var list = await RunTrustAsync(fixture, $"{options} trust list");
        var remove = await RunTrustAsync(fixture, $"{options} trust remove \"{fixture.RepositoryPath}\"");

        AssertStatus(add, fixture.RepositoryPath, "Trusted", relaxJson);
        AssertStatus(status, fixture.RepositoryPath, "Trusted", relaxJson);
        AssertList(list, fixture.RepositoryPath, relaxJson);
        AssertStatus(remove, fixture.RepositoryPath, "Untrusted", relaxJson);
    }

    [Fact]
    public async Task trust_text_and_error_rendering_remain_on_the_shared_cli_channels()
    {
        using var fixture = TrustJourneyFixture.Create(createRepository: false);

        var list = await RunTrustAsync(fixture, "trust list");
        var missing = await RtCliHost.RunAsync(
            $"trust status \"{fixture.RepositoryPath}\"",
            environment: fixture.Environment);

        list.ExitCode.ShouldBe(0);
        list.Stdout.Trim().ShouldBe("No repositories are persistently trusted.");
        list.Stderr.ShouldBeEmpty();
        missing.ExitCode.ShouldBe(1);
        missing.Stdout.ShouldBeEmpty();
        missing.Stderr.ShouldContain("does not exist");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task trust_command_argument_boundary_uses_the_shared_json_renderer(bool relaxJson)
    {
        using var fixture = TrustJourneyFixture.Create();
        var registry = new RepositoryTrustRegistry(fixture.RegistryPath);
        var command = new TrustCommand(
            new TrustAddOperation(registry),
            new TrustListOperation(registry),
            new TrustStatusOperation(registry),
            new TrustRemoveOperation(registry)).Build();
        using var console = new CapturingConsole();
        var previousFormat = RtContext.Format;
        var previousRelaxJson = RtContext.RelaxJson;
        try
        {
            RtContext.Format = OutputFormat.Json;
            RtContext.RelaxJson = relaxJson;
            using var scope = RoslynTools.Core.RtConsole.UseConsole(console);

            (await command.InvokeAsync(["add", fixture.RepositoryPath])).ShouldBe(0);
            AssertStatus(console.TakeOutput(), fixture.RepositoryPath, "Trusted", relaxJson);
            (await command.InvokeAsync(["status", fixture.RepositoryPath])).ShouldBe(0);
            AssertStatus(console.TakeOutput(), fixture.RepositoryPath, "Trusted", relaxJson);
            (await command.InvokeAsync(["list"])).ShouldBe(0);
            AssertList(console.TakeOutput(), fixture.RepositoryPath, relaxJson);
            (await command.InvokeAsync(["remove", fixture.RepositoryPath])).ShouldBe(0);
            AssertStatus(console.TakeOutput(), fixture.RepositoryPath, "Untrusted", relaxJson);
        }
        finally
        {
            RtContext.Format = previousFormat;
            RtContext.RelaxJson = previousRelaxJson;
        }
    }

    [Fact]
    public async Task trust_command_argument_boundary_preserves_text_rendering()
    {
        using var fixture = TrustJourneyFixture.Create();
        var registry = new RepositoryTrustRegistry(fixture.RegistryPath);
        var command = new TrustCommand(
            new TrustAddOperation(registry),
            new TrustListOperation(registry),
            new TrustStatusOperation(registry),
            new TrustRemoveOperation(registry)).Build();
        using var console = new CapturingConsole();
        var previousFormat = RtContext.Format;
        try
        {
            RtContext.Format = OutputFormat.Text;
            using var scope = RoslynTools.Core.RtConsole.UseConsole(console);

            (await command.InvokeAsync(["list"])).ShouldBe(0);
            console.TakeOutput().Trim().ShouldBe("No repositories are persistently trusted.");
            console.TakeError().ShouldBeEmpty();
            (await command.InvokeAsync(["add", fixture.RepositoryPath])).ShouldBe(0);
            console.TakeOutput().ShouldContain($"Trusted: {fixture.RepositoryPath}");
            console.TakeError().ShouldBeEmpty();
            (await command.InvokeAsync(["list"])).ShouldBe(0);
            console.TakeOutput().ShouldContain($"Trusted: {fixture.RepositoryPath}");
            console.TakeError().ShouldBeEmpty();
            (await command.InvokeAsync(["remove", fixture.RepositoryPath])).ShouldBe(0);
            console.TakeOutput().ShouldContain($"Untrusted: {fixture.RepositoryPath}");
            console.TakeError().ShouldContain("Persistent repository trust was removed");
        }
        finally
        {
            RtContext.Format = previousFormat;
        }
    }

    private static async Task<CommandResult> RunTrustAsync(
        TrustJourneyFixture fixture,
        string arguments)
    {
        var result = await RtCliHost.RunAsync(arguments, environment: fixture.Environment);
        result.ExitCode.ShouldBe(0, result.Stderr);
        result.Stderr.ShouldBeEmpty();
        return result;
    }

    private static void AssertStatus(
        CommandResult result,
        string repositoryPath,
        string expectedState,
        bool relaxJson) => AssertStatus(result.Stdout, repositoryPath, expectedState, relaxJson);

    private static void AssertStatus(
        string output,
        string repositoryPath,
        string expectedState,
        bool relaxJson)
    {
        using var document = JsonDocument.Parse(output);
        document.RootElement.GetProperty("worktreeRoot").GetString().ShouldBe(repositoryPath);
        document.RootElement.GetProperty("state").GetString().ShouldBe(expectedState);
        AssertEncoding(output, repositoryPath, relaxJson);
    }

    private static void AssertList(
        CommandResult result,
        string repositoryPath,
        bool relaxJson) => AssertList(result.Stdout, repositoryPath, relaxJson);

    private static void AssertList(
        string output,
        string repositoryPath,
        bool relaxJson)
    {
        using var document = JsonDocument.Parse(output);
        var repository = document.RootElement.GetProperty("repositories").EnumerateArray().ShouldHaveSingleItem();
        repository.GetProperty("worktreeRoot").GetString().ShouldBe(repositoryPath);
        repository.GetProperty("state").GetString().ShouldBe("Trusted");
        AssertEncoding(output, repositoryPath, relaxJson);
    }

    private static void AssertEncoding(string output, string repositoryPath, bool relaxJson)
    {
        if (relaxJson)
        {
            output.ShouldContain(repositoryPath);
            output.ShouldNotContain("\\u00E9");
            output.ShouldNotContain("\\u0026");
            output.ShouldNotContain("\\u0027");
            return;
        }

        output.ShouldNotContain(repositoryPath);
        output.ShouldContain("\\u00E9");
        output.ShouldContain("\\u0026");
        output.ShouldContain("\\u0027");
    }

    private sealed class TrustJourneyFixture : IDisposable
    {
        private TrustJourneyFixture(string root, string repositoryPath)
        {
            Root = root;
            RepositoryPath = repositoryPath;
            var profile = Path.Combine(root, "profile");
            Environment = new Dictionary<string, string?>
            {
                ["XDG_DATA_HOME"] = profile,
                ["LOCALAPPDATA"] = profile,
                ["RT_NO_DAEMON"] = "1"
            };
            RegistryPath = Path.Combine(profile, "RoslynTools", "repository-trust.json");
        }

        private string Root { get; }

        internal string RepositoryPath { get; }

        internal string RegistryPath { get; }

        internal IReadOnlyDictionary<string, string?> Environment { get; }

        internal static TrustJourneyFixture Create(bool createRepository = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"rt-trust-output-{Guid.NewGuid():N}");
            var repositoryPath = Path.Combine(root, "r\u00E9po-&'trusted");
            Directory.CreateDirectory(root);
            var fixture = new TrustJourneyFixture(root, repositoryPath);
            if (createRepository)
                fixture.InitializeRepository();
            return fixture;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private void InitializeRepository()
        {
            Directory.CreateDirectory(RepositoryPath);
            RunGit("init", "--quiet");
            RunGit("config", "user.email", "tests@example.invalid");
            RunGit("config", "user.name", "Roslyn Tools Tests");
            File.WriteAllText(Path.Combine(RepositoryPath, "App.slnx"), "<Solution />");
            RunGit("add", "App.slnx");
            RunGit("commit", "--quiet", "-m", "Initial commit");
        }

        private void RunGit(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = RepositoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Failed to start git.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}{stdout}");
        }
    }

    private sealed class CapturingConsole : IRtConsole, IDisposable
    {
        private readonly StringWriter _stdout = new();
        private readonly StringWriter _stderr = new();

        public bool IsOutputRedirected => true;

        public TextWriter Out => _stdout;

        public void Log(RtLogLevel level, string template, params object?[] args) =>
            _stderr.WriteLine(RoslynTools.Core.RtConsole.RenderTemplate(template, args));

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null) =>
            Writer(context).WriteLine(message);

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null) =>
            Writer(context).Write(message);

        internal string TakeOutput()
        {
            var output = _stdout.ToString();
            _stdout.GetStringBuilder().Clear();
            return output;
        }

        internal string TakeError()
        {
            var output = _stderr.ToString();
            _stderr.GetStringBuilder().Clear();
            return output;
        }

        public void Dispose()
        {
            _stdout.Dispose();
            _stderr.Dispose();
        }

        private StringWriter Writer(RtOutputContext context) =>
            context.HasFlag(RtOutputContext.Diagnostic) ? _stderr : _stdout;
    }
}
