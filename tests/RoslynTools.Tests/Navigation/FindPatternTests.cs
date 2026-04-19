using RoslynTools.Core;

namespace RoslynTools.Tests.Navigation;

public class FindPatternTests
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
        => Path.Combine(project.ProjectDir, relativePath);

    // ── Exact duplicate found ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExactDuplicate_CrossFile_IsFound()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                startLine: 7,
                endLine: 9,
                scope: null);

            Assert.Single(matches);
            Assert.Contains("B.cs", matches[0].File, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Identifier-only-different match found ─────────────────────────────────────

    [Fact]
    public async Task IdentifierOnlyDifferent_IsFound()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    int alpha = 1;
                    int beta = 2;
                    System.Console.WriteLine(alpha + beta);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                startLine: 7,
                endLine: 9,
                scope: null);

            Assert.Single(matches);
            var subs = matches[0].Substitutions;
            // x → alpha, y → beta (may also include literal tokens as self-mappings)
            Assert.True(subs.ContainsKey("x") || subs.ContainsKey("alpha"),
                "Expected substitution entry for identifier x or alpha");
        }
    }

    // ── Structurally different code is NOT matched ────────────────────────────────

    [Fact]
    public async Task StructurallyDifferent_IsNotFound()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    int x = 1;
                    System.Console.WriteLine(x);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                startLine: 7,
                endLine: 9,
                scope: null);

            Assert.Empty(matches);
        }
    }

    // ── Pattern origin is excluded from results ───────────────────────────────────

    [Fact]
    public async Task PatternOrigin_IsExcludedFromResults()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                startLine: 7,
                endLine: 9,
                scope: null);

            // The only occurrence is the source itself — should be excluded
            Assert.Empty(matches);
        }
    }

    // ── Scope filter excludes out-of-scope matches ────────────────────────────────

    [Fact]
    public async Task ScopeFilter_ExcludesOutOfScopeMatches()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            // Use a scope that matches no project or folder — expect no results
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                startLine: 7,
                endLine: 9,
                scope: "NonExistentScope.Does.Not.Exist");

            Assert.Empty(matches);
        }
    }

    // ── Cross-file match returns correct metadata ─────────────────────────────────

    [Fact]
    public async Task CrossFileMatch_ReturnsCorrectLineAndPreview()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void Run()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }
            }
            """);
        project.AddSource("B.cs", """
            namespace TestApp;

            public class B
            {
                public void Execute()
                {
                    int a = 1;
                    int b = 2;
                    System.Console.WriteLine(a + b);
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var matches = await PatternMatcher.FindMatchesAsync(
                sln,
                FilePath(project, "A.cs"),
                startLine: 7,
                endLine: 9,
                scope: null);

            Assert.Single(matches);
            var m = matches[0];
            Assert.True(m.Line > 0);
            Assert.True(m.EndLine >= m.Line);
            Assert.NotEmpty(m.Preview);
        }
    }
}
