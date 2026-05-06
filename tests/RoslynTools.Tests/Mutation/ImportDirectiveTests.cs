using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public class ImportDirectiveTests
{
    [Fact]
    public async Task Analyze_reports_global_and_static_imports()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", """
            global using System;
            global using static System.Math;
            """);
        project.AddSource("App.cs", """
            using Alias = System.Text.StringBuilder;
            using static System.Console;

            class App { }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var all = await new ImportDirectiveService().AnalyzeAsync(solution, "all");
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");
            var statics = await new ImportDirectiveService().AnalyzeAsync(solution, "static");

            Assert.Contains(all, i => i.Kind == "global" && i.Name == "System");
            Assert.Contains(all, i => i.Kind == "global-static" && i.Name == "System.Math");
            Assert.Contains(all, i => i.Kind == "static" && i.Name == "System.Console");
            Assert.Contains(all, i => i.Kind == "alias" && i.Alias == "Alias");
            Assert.Equal(2, globals.Count);
            Assert.Equal(2, statics.Count);
        }
    }

    [Fact]
    public async Task Analyze_skips_generated_and_intermediate_files()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("obj/Debug/ImportApp.GlobalUsings.g.cs", """
            global using System;
            """);
        project.AddSource("GlobalUsings.cs", """
            global using Xunit;
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            var only = Assert.Single(globals);
            Assert.Equal("Xunit", only.Name);
        }
    }

    [Fact]
    public async Task Analyze_respects_rtignore_patterns()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Ignored/IgnoredImports.cs", """
            global using System;
            """);
        project.AddSource("GlobalUsings.cs", """
            global using Xunit;
            """);
        var (workspace, solution) = project.Build();
        File.WriteAllText(Path.Combine(project.ProjectDir, ".rtignore"), "Ignored");

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            var only = Assert.Single(globals);
            Assert.Equal("Xunit", only.Name);
        }
    }

    [Fact]
    public async Task Analyze_respects_command_scoped_rtignore_patterns()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("Ignored/IgnoredImports.cs", """
            global using System;
            """);
        project.AddSource("GlobalUsings.cs", """
            global using Xunit;
            """);
        var (workspace, solution) = project.Build();
        File.WriteAllText(Path.Combine(project.ProjectDir, ".rtignore"), "list-imports:Ignored");

        using (workspace)
        {
            var globals = await new ImportDirectiveService().AnalyzeAsync(solution, "global");

            var only = Assert.Single(globals);
            Assert.Equal("Xunit", only.Name);
        }
    }

    [Fact]
    public async Task Adds_static_import_to_existing_file()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("App.cs", """
            using System;

            class App { }
            """);
        var (workspace, solution) = project.Build();
        var filePath = Path.Combine(project.ProjectDir, "App.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                dryRun: false);
        }

        var content = await project.ReadSourceAsync("App.cs");
        Assert.Contains("using static System.Console;", content);
    }

    [Fact]
    public async Task Adds_global_import_to_existing_file()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", """
            global using System;
            """);
        var (workspace, solution) = project.Build();
        var filePath = Path.Combine(project.ProjectDir, "GlobalUsings.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddGlobalUsingAsync(
                solution,
                filePath,
                "System.Text",
                dryRun: false);
        }

        var content = await project.ReadSourceAsync("GlobalUsings.cs");
        Assert.Contains("global using System.Text;", content);
    }

    [Fact]
    public async Task Does_not_duplicate_existing_imports()
    {
        using var project = new AdhocProjectBuilder("ImportApp");
        project.AddSource("GlobalUsings.cs", """
            global using System;
            using static System.Console;
            """);
        var (workspace, solution) = project.Build();
        var filePath = Path.Combine(project.ProjectDir, "GlobalUsings.cs");

        using (workspace)
        {
            await new ImportDirectiveService().AddGlobalUsingAsync(
                solution,
                filePath,
                "System",
                dryRun: false);

            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                dryRun: false);
        }

        var content = await project.ReadSourceAsync("GlobalUsings.cs");
        Assert.Equal(1, CountOccurrences(content, "global using System;"));
        Assert.Equal(1, CountOccurrences(content, "using static System.Console;"));
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
