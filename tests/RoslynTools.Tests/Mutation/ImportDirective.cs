using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ImportDirective
{
    [Fact]
    public async Task analyzes_reports_global_and_static_imports()
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

            all.ShouldContain(i => i.Kind == "global" && i.Name == "System");
            all.ShouldContain(i => i.Kind == "global-static" && i.Name == "System.Math");
            all.ShouldContain(i => i.Kind == "static" && i.Name == "System.Console");
            all.ShouldContain(i => i.Kind == "alias" && i.Alias == "Alias");
            globals.Count.ShouldBe(2);
            statics.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task analyzes_skips_generated_and_intermediate_files()
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
            only.Name.ShouldBe("Xunit");
        }
    }

    [Fact]
    public async Task analyzes_respects_rtignore_patterns()
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
            only.Name.ShouldBe("Xunit");
        }
    }

    [Fact]
    public async Task analyzes_respects_command_scoped_rtignore_patterns()
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
            only.Name.ShouldBe("Xunit");
        }
    }

    [Fact]
    public async Task adds_static_import_to_existing_file()
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
                false);
        }

        var content = await project.ReadSourceAsync("App.cs");
        content.ShouldContain("using static System.Console;", Case.Sensitive);
    }

    [Fact]
    public async Task adds_global_import_to_existing_file()
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
                false);
        }

        var content = await project.ReadSourceAsync("GlobalUsings.cs");
        content.ShouldContain("global using System.Text;", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_duplicate_existing_imports()
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
                false);

            await new ImportDirectiveService().AddStaticUsingAsync(
                solution,
                filePath,
                "System.Console",
                false);
        }

        var content = await project.ReadSourceAsync("GlobalUsings.cs");
        CountOccurrences(content, "global using System;").ShouldBe(1);
        CountOccurrences(content, "using static System.Console;").ShouldBe(1);
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
