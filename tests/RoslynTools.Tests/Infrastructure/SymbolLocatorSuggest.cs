using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     US-135 Batch 2: drill-in member lookups that miss the target should append a
///     "Did you mean: ..." tail with the closest member names (Damerau-Levenshtein,
///     length-relative threshold). The tail is suppressed when the user passes the
///     global <c>--no-suggest</c> opt-out (<see cref="RtContext.NoSuggest"/>).
/// </summary>
public sealed class SymbolLocatorSuggest
{
    [Fact]
    public async Task drill_in_member_not_found_with_close_match_appends_did_you_mean_tail()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public sealed class Service
                                        {
                                            public void MisspelledName(int x) { }
                                            public void OtherMember() { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindMemberBySignatureAsync(
                solution, "TestApp.Service.MisspelledNamee(int)");

            result.IsErr.ShouldBeTrue();
            var message = result.Match(_ => string.Empty, e => e.Message);
            message.ShouldContain("Did you mean", Case.Sensitive);
            message.ShouldContain("MisspelledName", Case.Sensitive);
        }
    }

    [Fact]
    public async Task drill_in_member_not_found_without_close_match_omits_did_you_mean_tail()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public sealed class Service
                                        {
                                            public void Foo() { }
                                            public void Bar() { }
                                            public void Baz() { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            // Target "XQZ" (len 3) — length-relative threshold caps edit distance at 1.
            // Distances to Foo/Bar/Baz are all >= 2, so no suggestion should appear.
            var result = await Core.SymbolLocator.TryFindMemberBySignatureAsync(
                solution, "TestApp.Service.XQZ()");

            result.IsErr.ShouldBeTrue();
            var message = result.Match(_ => string.Empty, e => e.Message);
            message.ShouldNotContain("Did you mean",
                customMessage: "no member is within the length-relative threshold; the tail must be omitted");
        }
    }

    [Fact]
    public async Task drill_in_member_not_found_with_suggestions_disabled_omits_did_you_mean_tail()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public sealed class Service
                                        {
                                            public void MisspelledName(int x) { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var result = await Core.SymbolLocator.TryFindMemberBySignatureAsync(
                solution, "TestApp.Service.MisspelledNamee(int)", includeSuggestions: false);

            result.IsErr.ShouldBeTrue();
            var message = result.Match(_ => string.Empty, e => e.Message);
            message.ShouldNotContain("Did you mean",
                customMessage: "The explicit lookup option must suppress the suggestion tail.");
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task daemon_describe_respects_the_per_request_suggestion_choice(
        bool noSuggest, bool expectSuggestion)
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public sealed class Service
                                        {
                                            public void Calculate(int x) { }
                                        }
                                        """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var dispatcher = new DaemonCommandDispatcher(
                [new DescribeOperation(new FixedSessions(solution), new InspectService())]);
            var arguments = JsonSerializer.SerializeToElement(
                new DescribeRequest("TestApp.Service.Calclate(int)", NoSuggest: noSuggest),
                DaemonRpcJson.RequestOptions);
            var request = new DaemonRpcRequest(
                DaemonRpcProtocol.CurrentVersion,
                "suggestion-request",
                DaemonRpcProtocol.ExecuteCommandMethod,
                "describe",
                arguments);

            var response = await dispatcher.ExecuteAsync(request);

            response.Error.ShouldNotBeNull();
            response.Error.Message.Contains("Did you mean", StringComparison.Ordinal)
                .ShouldBe(expectSuggestion);
        }
    }

    [Fact]
    public async Task describe_drill_in_with_typo_prints_did_you_mean_suggestion_on_stderr()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public sealed class Service
                                        {
                                            public void Calculate(int x) { }
                                            public void Render() { }
                                        }
                                        """);
        var (workspace, sln) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var sc = new ServiceCollection();
            RoslynTools.Bootstrap.RegisterServices(sc);
            using var services = sc.BuildServiceProvider();

            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            int exitCode;
            try
            {
                exitCode = await App.RunAsync(
                    ["inspect", "describe", "TestApp.Service.Calclate(int)", "--solution", "ignored"],
                    services);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            exitCode.ShouldBe(1);
            stderr.ToString().ShouldContain("Did you mean", Case.Sensitive);
            stderr.ToString().ShouldContain("Calculate", Case.Sensitive);
        }
    }

    [Fact]
    public async Task describe_drill_in_with_typo_and_no_suggest_flag_omits_did_you_mean_tail()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public sealed class Service
                                        {
                                            public void Calculate(int x) { }
                                        }
                                        """);
        var (workspace, sln) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var sc = new ServiceCollection();
            RoslynTools.Bootstrap.RegisterServices(sc);
            using var services = sc.BuildServiceProvider();

            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);

            int exitCode;
            try
            {
                exitCode = await App.RunAsync(
                    ["--no-suggest", "inspect", "describe", "TestApp.Service.Calclate(int)", "--solution", "ignored"],
                    services);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            exitCode.ShouldBe(1);
            stderr.ToString().ShouldNotContain("Did you mean",
                customMessage: "--no-suggest must suppress the suggestion tail end-to-end.");
        }
    }

    private sealed class FixedSessions(Solution solution) : IReadOnlyWorkspaceSessions
    {
        public Task<ReadOnlyWorkspaceLease> AcquireAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ReadOnlyWorkspaceLease(solution, "test-revision"));
        }
    }
}
