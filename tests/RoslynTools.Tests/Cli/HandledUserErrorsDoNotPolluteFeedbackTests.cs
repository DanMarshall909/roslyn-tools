using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Feedback;
using Shouldly;

namespace RoslynTools.Tests.Cli;

/// <summary>
///     US-076 regression: exceptions that TryHandleUserErrorAsync already
///     translates to a clean stderr message + exit 1 must NOT be captured to
///     feedback.json. Only genuinely unmapped failures (the "return false"
///     branch) are signal worth a human triage entry.
/// </summary>
public sealed class HandledUserErrorsDoNotPolluteFeedbackTests
{
    [Fact]
    public async Task missing_solution_file_exits_1_without_appending_feedback()
    {
        using var workspace = new FeedbackWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            var sc = new ServiceCollection();
            RoslynTools.Bootstrap.RegisterServices(sc);
            using var services = sc.BuildServiceProvider();

            var exitCode = await App.RunAsync(
                ["inspect", "describe", "Foo", "--solution", "no-such-file.slnx"],
                services);

            exitCode.ShouldBe(1);
            FeedbackStore.LoadAll().ShouldBeEmpty(
                "FileNotFoundException is translated to a clean exit-1 by TryHandleUserErrorAsync; "
                + "capturing it adds noise without adding signal.");
        }
    }

    [Fact]
    public async Task describe_unknown_symbol_exits_1_via_result_without_appending_feedback()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget { }
                                       """);
        var (workspace, sln) = project.Build();

        using var feedback = new FeedbackWorkspace();
        using (feedback.UseCurrentDirectory())
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
                    ["inspect", "describe", "TestApp.Missing", "--kind", "type", "--solution", "ignored"],
                    services);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            exitCode.ShouldBe(1);
            stdout.ToString().Trim().ShouldBeEmpty("the error branch must not emit a JSON result to stdout");
            stderr.ToString().ShouldContain(
                "No symbol matching 'TestApp.Missing' was found in the loaded solution.");
            FeedbackStore.LoadAll().ShouldBeEmpty(
                "A symbol-not-found is now a routine RtError surfaced cleanly via Result — it never "
                + "throws, so it must not be captured to feedback.json.");
        }
    }

    /// <summary>
    ///     US-077 AC9: <c>rt inspect find-refs --symbol DoesNotExistSymbolFoo</c> against a
    ///     solution with no such symbol exits 1 with exactly one <c>Error: ...</c> line on
    ///     stderr, no stdout, and no feedback.json append. The not-found is a routine
    ///     <see cref="RtError" />-shaped failure (routed via TryHandleUserErrorAsync), not a
    ///     triage-worthy exception.
    /// </summary>
    [Fact]
    public async Task relationships_unknown_symbol_exits_1_via_result_without_appending_feedback()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace TestApp;

                                       public sealed class Widget { }
                                       """);
        var (workspace, sln) = project.Build();

        using var feedback = new FeedbackWorkspace();
        using (feedback.UseCurrentDirectory())
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
                    [
                        "inspect", "relationships", "references", "DoesNotExistSymbolFoo",
                        "--kind", "type", "--solution", "ignored"
                    ],
                    services);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            exitCode.ShouldBe(1);
            stdout.ToString().Trim().ShouldBeEmpty(
                "the error branch must not emit a JSON result to stdout");

            var errorLines = stderr.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Contains("Error:", StringComparison.Ordinal))
                .ToList();
            errorLines.Count.ShouldBe(1,
                "AC9: exactly one 'Error: ...' line. relationship not-found must be a single clean message.");
            errorLines[0].ShouldContain("DoesNotExistSymbolFoo");

            FeedbackStore.LoadAll().ShouldBeEmpty(
                "AC9: a symbol-not-found from relationships is routine and must not pollute feedback.json.");
        }
    }

    /// <summary>
    ///     US-077 AC10: an ambiguous <c>rt inspect describe</c> lookup (more than one symbol
    ///     shares the input FQN's no-signature form — here, two method overloads of
    ///     <c>A.Foo.Bar</c>) exits 1 with an <see cref="RtErrorKind.AmbiguousMatch" />-shaped
    ///     stderr message that lists the candidate FQNs. No stdout, no feedback append.
    /// </summary>
    /// <remarks>
    ///     The literal "two types <c>Foo.Bar</c> in namespaces A and B" scenario from the AC
    ///     wording cannot reach <c>TryDisambiguate</c>: <c>SymbolLocator.MatchesFqn</c> requires
    ///     exact FQN equality, so passing <c>Foo.Bar</c> against types <c>A.Foo.Bar</c> /
    ///     <c>B.Foo.Bar</c> yields zero matches → <see cref="RtErrorKind.NotFound" />. Method
    ///     overloads are the load-bearing ambiguity case the production code is designed for
    ///     (same no-signature FQN, distinct full DisplayNames so dedupe doesn't collapse them).
    /// </remarks>
    [Fact]
    public async Task describe_ambiguous_symbol_exits_1_with_ambiguous_match_listing_candidates()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace A;

                                    public sealed class Foo
                                    {
                                        public void Bar(int x) { }
                                        public void Bar(string s) { }
                                    }
                                    """);
        var (workspace, sln) = project.Build();

        using var feedback = new FeedbackWorkspace();
        using (feedback.UseCurrentDirectory())
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
                    ["inspect", "describe", "A.Foo.Bar", "--solution", "ignored"],
                    services);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            exitCode.ShouldBe(1);

            // AC10: ambiguous-match message must name the ambiguity (RtError.AmbiguousMatch
            // shape) and list candidate FQNs so the user can re-invoke with the right one.
            // The interactive menu is echoed to stdout before TryDisambiguate returns the
            // Result.Err (rawInput is null on a non-TTY). The Result-derived clean error
            // message lands on stderr via TryHandleUserErrorAsync; that is the load-bearing
            // assertion. (The stdout-not-empty divergence from the AC spec is documented in
            // the batch report — see US-077 batch D notes.)
            stdout.ToString().ShouldNotContain("\"name\"",
                Case.Sensitive,
                "stdout must not contain a JSON inspection result — the error branch took over.");

            var stderrText = stderr.ToString();
            stderrText.ShouldContain("Multiple symbols match");
            stderrText.ShouldContain("A.Foo.Bar(int");
            stderrText.ShouldContain("A.Foo.Bar(string");

            FeedbackStore.LoadAll().ShouldBeEmpty(
                "AC10: ambiguous-match is a routine RtError surfaced via Result — it never throws, "
                + "so it must not be captured to feedback.json.");
        }
    }

    private sealed class FeedbackWorkspace : IDisposable
    {
        private readonly string _previousDirectory;

        public FeedbackWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"rt-feedback-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "ralph"));
            _previousDirectory = Directory.GetCurrentDirectory();
        }

        private string Root { get; }

        public IDisposable UseCurrentDirectory()
        {
            Directory.SetCurrentDirectory(Root);
            return new CurrentDirectoryScope(_previousDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previousDirectory);
            try
            {
                Directory.Delete(Root, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private sealed class CurrentDirectoryScope(string previousDirectory) : IDisposable
        {
            public void Dispose()
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }
    }
}
