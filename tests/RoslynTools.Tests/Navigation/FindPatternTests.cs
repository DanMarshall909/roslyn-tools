using RoslynTools.Core;

namespace RoslynTools.Tests.Navigation;

public class FindPatternTests
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
    {
        return Path.Combine(project.ProjectDir, relativePath);
    }

    // ── Exact duplicate found ─────────────────────────────────────────────────────

    [Fact]
    public async Task Finds_exact_cross_file_matches()
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
                7,
                9,
                null);

            Assert.Single(matches);
            Assert.Contains("B.cs", matches[0].File, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Identifier-only-different match found ─────────────────────────────────────

    [Fact]
    public async Task Finds_matches_with_different_identifiers()
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
                7,
                9,
                null);

            Assert.Single(matches);
            var subs = matches[0].Substitutions;
            // x → alpha, y → beta (may also include literal tokens as self-mappings)
            Assert.True(subs.ContainsKey("x") || subs.ContainsKey("alpha"),
                "Expected substitution entry for identifier x or alpha");
        }
    }

    // ── Structurally different code is NOT matched ────────────────────────────────

    [Fact]
    public async Task Does_not_return_structurally_different_matches()
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
                7,
                9,
                null);

            Assert.Empty(matches);
        }
    }

    // ── Pattern origin is excluded from results ───────────────────────────────────

    [Fact]
    public async Task Excludes_the_pattern_origin_from_the_results()
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
                7,
                9,
                null);

            // The only occurrence is the source itself — should be excluded
            Assert.Empty(matches);
        }
    }

    // ── Scope filter excludes out-of-scope matches ────────────────────────────────

    [Fact]
    public async Task Excludes_matches_outside_the_scope()
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
                7,
                9,
                "NonExistentScope.Does.Not.Exist");

            Assert.Empty(matches);
        }
    }

    // ── Cross-file match returns correct metadata ─────────────────────────────────

    [Fact]
    public async Task Returns_the_correct_line_and_preview()
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
                7,
                9,
                null);

            Assert.Single(matches);
            var m = matches[0];
            Assert.True(m.Line > 0);
            Assert.True(m.EndLine >= m.Line);
            Assert.NotEmpty(m.Preview);
        }
    }
}
