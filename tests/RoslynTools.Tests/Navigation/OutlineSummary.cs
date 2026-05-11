using RoslynTools.Services;

namespace RoslynTools.Tests.Navigation;

public class OutlineSummary
{
    [Fact]
    public async Task summary_mode_omits_members_and_returns_fqn_kind_lines()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
            namespace TestApp;
            public class Widget
            {
                public string Name { get; set; } = "";
                public void DoWork() { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService().OutlineFileAsync(sln,
                Path.Combine(project.ProjectDir, "Widget.cs"), summary: true);

            var entry = Assert.Single(result);
            Assert.Equal("TestApp.Widget", entry["fqn"]);
            Assert.Equal("Class", entry["kind"]);
            Assert.NotNull(entry["lineFrom"]);
            Assert.NotNull(entry["lineTo"]);
            Assert.False(entry.ContainsKey("members"), "summary mode must not include member detail");
            Assert.False(entry.ContainsKey("name"), "summary mode must not include short name");
        }
    }

    [Fact]
    public async Task summary_mode_includes_description_when_xml_doc_present()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
            namespace TestApp;
            /// <summary>Loads things from the database.</summary>
            public class LoaderService { }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService().OutlineFileAsync(sln,
                Path.Combine(project.ProjectDir, "Service.cs"), summary: true);

            var entry = Assert.Single(result);
            Assert.True(entry.ContainsKey("description"), "should include description when XML doc exists");
            Assert.Contains("Loads things from the database", (string)entry["description"]!);
        }
    }

    [Fact]
    public async Task summary_mode_omits_description_when_no_xml_doc()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Plain.cs", "namespace TestApp; public class Plain { }");

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService().OutlineFileAsync(sln,
                Path.Combine(project.ProjectDir, "Plain.cs"), summary: true);

            var entry = Assert.Single(result);
            Assert.False(entry.ContainsKey("description"), "should omit description when no XML doc");
        }
    }

    [Fact]
    public async Task summary_mode_filters_nested_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Outer.cs", """
            namespace TestApp;
            public class Outer
            {
                public class Inner { }
            }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService().OutlineFileAsync(sln,
                Path.Combine(project.ProjectDir, "Outer.cs"), summary: true);

            Assert.Single(result);
            Assert.Equal("TestApp.Outer", result[0]["fqn"]);
        }
    }

    [Fact]
    public async Task directory_mode_returns_types_from_all_files_in_dir()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Alpha.cs", "namespace TestApp; public class Alpha { }");
        project.AddSource("Beta.cs", "namespace TestApp; public class Beta { }");

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService().OutlineDirectoryAsync(sln, project.ProjectDir);
            var fqns = result.Select(r => (string)r["fqn"]!).ToHashSet();

            Assert.Contains("TestApp.Alpha", fqns);
            Assert.Contains("TestApp.Beta", fqns);
        }
    }

    [Fact]
    public async Task directory_summary_mode_omits_members_and_nested_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Things.cs", """
            namespace TestApp;
            public class Parent { public class Child { } }
            public class Sibling { public int Value { get; set; } }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService().OutlineDirectoryAsync(sln, project.ProjectDir, summary: true);
            var fqns = result.Select(r => (string)r["fqn"]!).ToHashSet();

            Assert.Contains("TestApp.Parent", fqns);
            Assert.Contains("TestApp.Sibling", fqns);
            Assert.DoesNotContain("TestApp.Parent.Child", fqns);
            Assert.True(result.All(r => !r.ContainsKey("members")));
        }
    }

    [Fact]
    public async Task namespace_summary_mode_omits_members()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Svc.cs", """
            namespace TestApp;
            public class MyService { public void Run() { } }
            """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await new OutlineService().OutlineNamespaceAsync(sln, "TestApp", summary: true);

            var entry = Assert.Single(result);
            Assert.Equal("TestApp.MyService", entry["fqn"]);
            Assert.False(entry.ContainsKey("members"));
        }
    }
}
