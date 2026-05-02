using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public class MoveMethodTests
{
    // ─── helpers ─────────────────────────────────────────────────────────────

    private static async Task<string> TextAsync(Microsoft.CodeAnalysis.Solution sln, string name)
    {
        var doc = sln.Projects
            .SelectMany(p => p.Documents)
            .Single(d => d.Name == name);
        return (await doc.GetTextAsync()).ToString();
    }

    // ─── happy-path: single call site ────────────────────────────────────────

    [Fact]
    public async Task Moves_static_method_and_rewrites_one_call_site()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public static class Source
            {
                public static int Add(int a, int b) => a + b;
            }
            """);
        project.AddSource("Target.cs", """
            namespace App;
            public static class Target { }
            """);
        project.AddSource("Caller.cs", """
            namespace App;
            public class Caller
            {
                public int Run() => Source.Add(1, 2);
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await MoveMethodService.RunAsync(
                sln, "App.Source.Add", "App.Target", createFile: false, dryRun: false);

            Assert.True(result);
        }

        // Source no longer has Add
        var src = await project.ReadSourceAsync("Source.cs");
        Assert.DoesNotContain("Add", src);

        // Target now has Add
        var tgt = await project.ReadSourceAsync("Target.cs");
        Assert.Contains("Add", tgt);

        // Caller uses Target.Add
        var caller = await project.ReadSourceAsync("Caller.cs");
        Assert.Contains("Target.Add", caller);
        Assert.DoesNotContain("Source.Add", caller);
    }

    // ─── happy-path: multiple call sites ─────────────────────────────────────

    [Fact]
    public async Task Moves_static_method_and_rewrites_multiple_call_sites()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public static class Source
            {
                public static string Greet(string name) => "Hello " + name;
            }
            """);
        project.AddSource("Target.cs", """
            namespace App;
            public static class Target { }
            """);
        project.AddSource("Caller.cs", """
            namespace App;
            public class Caller
            {
                public string A() => Source.Greet("Alice");
                public string B() => Source.Greet("Bob");
                public string C() => Source.Greet("Carol");
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await MoveMethodService.RunAsync(
                sln, "App.Source.Greet", "App.Target", createFile: false, dryRun: false);

            Assert.True(result);
        }

        var caller = await project.ReadSourceAsync("Caller.cs");
        Assert.Equal(3, CountOccurrences(caller, "Target.Greet"));
        Assert.DoesNotContain("Source.Greet", caller);
    }

    // ─── happy-path: internal caller (unqualified) ────────────────────────────

    [Fact]
    public async Task Rewrites_unqualified_internal_caller_within_source_class()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public static class Source
            {
                public static int Double(int x) => x * 2;
                public static int Quad(int x) => Double(x) + Double(x);
            }
            """);
        project.AddSource("Target.cs", """
            namespace App;
            public static class Target { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await MoveMethodService.RunAsync(
                sln, "App.Source.Double", "App.Target", createFile: false, dryRun: false);

            Assert.True(result);
        }

        var src = await project.ReadSourceAsync("Source.cs");
        Assert.DoesNotContain("static int Double", src);
        // internal callers should now say Target.Double
        Assert.Contains("Target.Double", src);
    }

    // ─── error: non-static method → returns false ─────────────────────────────

    [Fact]
    public async Task Returns_false_for_non_static_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public class Source
            {
                public int Compute(int x) => x + 1;
            }
            """);
        project.AddSource("Target.cs", """
            namespace App;
            public class Target { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await MoveMethodService.RunAsync(
                sln, "App.Source.Compute", "App.Target", createFile: false, dryRun: false);

            Assert.False(result);
        }
    }

    // ─── error: method not found → throws ─────────────────────────────────────

    [Fact]
    public async Task Throws_when_method_not_found()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public static class Source { }
            """);
        project.AddSource("Target.cs", """
            namespace App;
            public static class Target { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MoveMethodService.RunAsync(
                    sln, "App.Source.Missing", "App.Target", createFile: false, dryRun: false));
        }
    }

    // ─── error: target type not found (no --create-file) → returns false ──────

    [Fact]
    public async Task Returns_false_when_target_type_not_found_and_no_create_file()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public static class Source
            {
                public static int Add(int a, int b) => a + b;
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await MoveMethodService.RunAsync(
                sln, "App.Source.Add", "App.NonExistent", createFile: false, dryRun: false);

            Assert.False(result);
        }
    }

    // ─── error: name collision → returns false ────────────────────────────────

    [Fact]
    public async Task Returns_false_when_name_collision_in_target()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public static class Source
            {
                public static int Add(int a, int b) => a + b;
            }
            """);
        project.AddSource("Target.cs", """
            namespace App;
            public static class Target
            {
                public static int Add(int a, int b) => a + b;
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await MoveMethodService.RunAsync(
                sln, "App.Source.Add", "App.Target", createFile: false, dryRun: false);

            Assert.False(result);
        }
    }

    // ─── dry-run does not write files ─────────────────────────────────────────

    [Fact]
    public async Task DryRun_does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
            namespace App;
            public static class Source
            {
                public static int Add(int a, int b) => a + b;
            }
            """);
        project.AddSource("Target.cs", """
            namespace App;
            public static class Target { }
            """);
        project.AddSource("Caller.cs", """
            namespace App;
            public class Caller { public int Run() => Source.Add(1, 2); }
            """);

        // Build() writes files to disk — capture originals after Build.
        var (workspace, sln) = project.Build();

        var originalSource = await project.ReadSourceAsync("Source.cs");
        var originalTarget = await project.ReadSourceAsync("Target.cs");
        var originalCaller = await project.ReadSourceAsync("Caller.cs");

        using (workspace)
        {
            var result = await MoveMethodService.RunAsync(
                sln, "App.Source.Add", "App.Target", createFile: false, dryRun: true);

            Assert.True(result);
        }

        Assert.Equal(originalSource, await project.ReadSourceAsync("Source.cs"));
        Assert.Equal(originalTarget, await project.ReadSourceAsync("Target.cs"));
        Assert.Equal(originalCaller, await project.ReadSourceAsync("Caller.cs"));
    }

    // ─── utility ─────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
