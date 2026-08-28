using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class MoveMethod
{

    // ─── happy-path: single call site ────────────────────────────────────────

    [Fact]
    public async Task moves_static_method_and_rewrites_one_call_site()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Add", "App.Target", false, false);

            result.ShouldBeTrue();
        }

        // Source no longer has Add
        var src = await project.ReadSourceAsync("Source.cs");
        src.ShouldNotContain("Add", Case.Sensitive);

        // Target now has Add
        var tgt = await project.ReadSourceAsync("Target.cs");
        tgt.ShouldContain("Add", Case.Sensitive);

        // Caller uses Target.Add
        var caller = await project.ReadSourceAsync("Caller.cs");
        caller.ShouldContain("Target.Add", Case.Sensitive);
        caller.ShouldNotContain("Source.Add", Case.Sensitive);
    }

    // ─── happy-path: multiple call sites ─────────────────────────────────────

    [Fact]
    public async Task moves_static_method_and_rewrites_multiple_call_sites()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Greet", "App.Target", false, false);

            result.ShouldBeTrue();
        }

        var caller = await project.ReadSourceAsync("Caller.cs");
        CountOccurrences(caller, "Target.Greet").ShouldBe(3);
        caller.ShouldNotContain("Source.Greet", Case.Sensitive);
    }

    // ─── happy-path: internal caller (unqualified) ────────────────────────────

    [Fact]
    public async Task rewrites_unqualified_internal_caller_within_source_class()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Double", "App.Target", false, false);

            result.ShouldBeTrue();
        }

        var src = await project.ReadSourceAsync("Source.cs");
        src.ShouldNotContain("static int Double", Case.Sensitive);
        // internal callers should now say Target.Double
        src.ShouldContain("Target.Double", Case.Sensitive);
    }

    // ─── error: non-static method → returns false ─────────────────────────────

    [Fact]
    public async Task returns_false_for_non_static_method()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Compute", "App.Target", false, false);

            result.ShouldBeFalse();
        }
    }

    // ─── error: method not found → returns false ──────────────────────────────

    [Fact]
    public async Task returns_false_when_method_not_found()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Missing", "App.Target", false, false);

            result.ShouldBeFalse();
        }
    }

    // ─── error: target type not found (no --create-file) → returns false ──────

    [Fact]
    public async Task returns_false_when_target_type_not_found_and_no_create_file()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Add", "App.NonExistent", false, false);

            result.ShouldBeFalse();
        }
    }

    // ─── happy-path: --create-file creates new target file ───────────────────

    [Fact]
    public async Task create_file_creates_new_file_with_moved_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
                                       namespace App;
                                       public static class Source
                                       {
                                           public static int Add(int a, int b) => a + b;
                                       }
                                       """);
        project.AddSource("Caller.cs", """
                                       namespace App;
                                       public class Caller { public int Run() => Source.Add(1, 2); }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Add", "App.NewUtils", true, false);

            result.ShouldBeTrue();
        }

        // Source no longer has Add
        var src = await project.ReadSourceAsync("Source.cs");
        src.ShouldNotContain("Add", Case.Sensitive);

        // NewUtils.cs was created alongside Source.cs and contains Add
        var newFilePath = Path.Combine(project.ProjectDir, "NewUtils.cs");
        File.Exists(newFilePath).ShouldBeTrue();
        var newFile = await File.ReadAllTextAsync(newFilePath);
        newFile.ShouldContain("class NewUtils", Case.Sensitive);
        newFile.ShouldContain("Add", Case.Sensitive);

        // Caller was rewritten to use NewUtils.Add
        var caller = await project.ReadSourceAsync("Caller.cs");
        caller.ShouldContain("NewUtils.Add", Case.Sensitive);
        caller.ShouldNotContain("Source.Add", Case.Sensitive);
    }

    // ─── error: name collision → returns false ────────────────────────────────

    [Fact]
    public async Task returns_false_when_name_collision_in_target()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Add", "App.Target", false, false);

            result.ShouldBeFalse();
        }
    }

    // ─── dry-run does not write files ─────────────────────────────────────────

    [Fact]
    public async Task handles_dry_run_does_not_modify_files()
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
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Add", "App.Target", false, true);

            result.ShouldBeTrue();
        }

        (await project.ReadSourceAsync("Source.cs")).ShouldBe(originalSource);
        (await project.ReadSourceAsync("Target.cs")).ShouldBe(originalTarget);
        (await project.ReadSourceAsync("Caller.cs")).ShouldBe(originalCaller);
    }

    // ─── xml doc comments are preserved ──────────────────────────────────────

    [Fact]
    public async Task xml_doc_comment_is_preserved_on_moved_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Source.cs", """
                                       namespace App;
                                       public static class Source
                                       {
                                           /// <summary>Adds two integers.</summary>
                                           public static int Add(int a, int b) => a + b;
                                       }
                                       """);
        project.AddSource("Target.cs", """
                                       namespace App;
                                       public static class Target { }
                                       """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await ExecuteAsync(
                workspace, sln, "App.Source.Add", "App.Target", false, false);

            result.ShouldBeTrue();
        }

        var tgt = await project.ReadSourceAsync("Target.cs");
        tgt.ShouldContain("/// <summary>Adds two integers.</summary>", Case.Sensitive);
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

    private static async Task<bool> ExecuteAsync(
        Workspace workspace,
        Solution solution,
        string methodFqn,
        string toType,
        bool createFile,
        bool dryRun)
    {
        var operation = new MoveMethodOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new MoveMethodService(),
            new SolutionChangeWriter());
        var result = await operation.ExecuteAsync(new MoveMethodRequest(
            methodFqn,
            toType,
            createFile,
            DryRun: dryRun));
        return result.Match(_ => true, _ => false);
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
