using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class WriteFixesAsync_dry_run
{
    [Fact]
    public async Task emits_fix_id_header_above_each_diff()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace TestProject;
                                    public class Foo
                                    {
                                        public int Value => 1;
                                    }
                                    """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var doc = sln.Projects.First().Documents.First();
            var newText = (await doc.GetTextAsync()).ToString().Replace("=> 1", "=> 42", StringComparison.Ordinal);
            var fix = new Fix(
                Id: "abcdef01",
                Command: "test-cmd",
                Target: "TestProject.Foo",
                MutationKind: "demo",
                Files: new[] { new FixFile(doc.Id, doc.FilePath!, newText) });

            var output = await StdoutCapture.CaptureAsync(() =>
                new SolutionChangeWriter(new ConsoleSolutionWriteOutput())
                    .WriteFixesAsync(sln, [fix], dryRun: true));

            output.ShouldContain("[fix abcdef01]");
            output.IndexOf("[fix abcdef01]", StringComparison.Ordinal)
                .ShouldBeLessThan(output.IndexOf("--- ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task does_not_write_to_disk()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace TestProject;
                                    public class Foo { public int Value => 1; }
                                    """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var doc = sln.Projects.First().Documents.First();
            var originalDisk = await File.ReadAllTextAsync(doc.FilePath!);
            var newText = originalDisk.Replace("=> 1", "=> 42", StringComparison.Ordinal);
            var fix = new Fix(
                Id: "abcdef01",
                Command: "test-cmd",
                Target: "TestProject.Foo",
                MutationKind: "demo",
                Files: new[] { new FixFile(doc.Id, doc.FilePath!, newText) });

            await StdoutCapture.CaptureAsync(() =>
                new SolutionChangeWriter(new ConsoleSolutionWriteOutput())
                    .WriteFixesAsync(sln, [fix], dryRun: true));

            (await File.ReadAllTextAsync(doc.FilePath!)).ShouldBe(originalDisk);
        }
    }
}

public class WriteFixesAsync_apply
{
    [Fact]
    public async Task writes_new_text_to_disk()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace TestProject;
                                    public class Foo { public int Value => 1; }
                                    """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var doc = sln.Projects.First().Documents.First();
            var originalDisk = await File.ReadAllTextAsync(doc.FilePath!);
            var newText = originalDisk.Replace("=> 1", "=> 42", StringComparison.Ordinal);
            var fix = new Fix(
                Id: "abcdef01",
                Command: "test-cmd",
                Target: "TestProject.Foo",
                MutationKind: "demo",
                Files: new[] { new FixFile(doc.Id, doc.FilePath!, newText) });

            await new SolutionChangeWriter().WriteFixesAsync(sln, [fix], dryRun: false);

            (await File.ReadAllTextAsync(doc.FilePath!)).ShouldContain("=> 42");
        }
    }
}

public class WriteFixesAsync_empty
{
    [Fact]
    public async Task emits_no_changes_and_writes_nothing()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace TestProject;
                                    public class Foo { public int Value => 1; }
                                    """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var doc = sln.Projects.First().Documents.First();
            var originalDisk = await File.ReadAllTextAsync(doc.FilePath!);

            using var services = new RtServicesFixture();
            var writer = services.Resolve<ISolutionChangeWriter>();
            var output = await StdoutCapture.CaptureAsync(() =>
                writer.WriteFixesAsync(sln, Array.Empty<Fix>(), dryRun: false));

            output.ShouldContain("No changes.");
            (await File.ReadAllTextAsync(doc.FilePath!)).ShouldBe(originalDisk);
        }
    }

    [Fact]
    public async Task empty_short_circuits_before_applying()
    {
        // Pinpoints the early-return on the empty-fix path: if the guard falls through,
        // ApplyChangesAsync runs and emits its own "No changes." message — so we'd see
        // the message twice instead of once.
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace TestProject;
                                    public class Foo { public int Value => 1; }
                                    """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var output = await StdoutCapture.CaptureAsync(() =>
                new SolutionChangeWriter(new ConsoleSolutionWriteOutput())
                    .WriteFixesAsync(sln, Array.Empty<Fix>(), dryRun: false));

            var occurrences = output.Split("No changes.", StringSplitOptions.None).Length - 1;
            occurrences.ShouldBe(1);
        }
    }
}

public class WriteFixesAsync_invalid_input
{
    [Fact]
    public async Task throws_when_fix_references_unknown_document()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Foo.cs", """
                                    namespace TestProject;
                                    public class Foo { public int Value => 1; }
                                    """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var bogusProjectId = ProjectId.CreateNewId();
            var bogusDocId = DocumentId.CreateNewId(bogusProjectId);
            var fix = new Fix(
                Id: "deadbeef",
                Command: "test-cmd",
                Target: "TestProject.Foo",
                MutationKind: "demo",
                Files: new[] { new FixFile(bogusDocId, "/fake/path.cs", "new text") });

            await Should.ThrowAsync<InvalidOperationException>(() =>
                new SolutionChangeWriter().WriteFixesAsync(sln, [fix], dryRun: true));
        }
    }
}

internal static class StdoutCapture
{
    public static async Task<string> CaptureAsync(Func<Task> action)
    {
        var originalOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        return sw.ToString();
    }
}
