using RoslynTools.Core;

namespace RoslynTools.Tests.Analysis;

public class FindDuplicatesTests
{
    // ── Exact duplicates within a single file detected ────────────────────────────

    [Fact]
    public async Task Detects_exact_duplicates_inside_one_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
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
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First();
            Assert.Equal(2, g.Occurrences.Count);
            Assert.Equal(3, g.StatementCount);
        }
    }

    // ── Exact duplicates across files detected ────────────────────────────────────

    [Fact]
    public async Task Detects_exact_duplicates_across_files()
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
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.NotEmpty(groups);
            var g = groups.First(g => g.StatementCount == 3);
            Assert.Equal(2, g.Occurrences.Count);
            var files = g.Occurrences.Select(o => Path.GetFileName(o.FilePath)).ToHashSet();
            Assert.Contains("A.cs", files);
            Assert.Contains("B.cs", files);
        }
    }

    // ── No duplicates returns empty list ──────────────────────────────────────────

    [Fact]
    public async Task Returns_empty_when_no_duplicates()
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
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions());

            Assert.Empty(groups);
        }
    }

    // ── min-statements filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task MinStatements_filters_shorter_duplicates()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
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
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 5));

            Assert.Empty(groups);
        }
    }

    // ── min-occurrences filter ────────────────────────────────────────────────────

    [Fact]
    public async Task MinOccurrences_filters_groups_with_too_few_occurrences()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    int x = 1;
                    int y = 2;
                    System.Console.WriteLine(x + y);
                }

                public void Second()
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
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinOccurrences: 3));

            Assert.Empty(groups);
        }
    }

    // ── Boilerplate-only blocks not reported ──────────────────────────────────────

    [Fact]
    public async Task Does_not_report_boilerplate_only_blocks()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
            namespace TestApp;

            public class A
            {
                public void First()
                {
                    throw new System.NotImplementedException();
                }

                public void Second()
                {
                    throw new System.NotImplementedException();
                }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var groups = await FindDuplicatesCore.RunAsync(sln, new FindDuplicatesOptions(MinStatements: 1));

            Assert.Empty(groups);
        }
    }
}
