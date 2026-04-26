using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class InlineMethodTests
{
    private const string MathHelperSource = """
        namespace MathApp;

        public class MathHelper
        {
            public static int Double(int x) => x * 2;
        }

        public class Calc
        {
            void Run()
            {
                var a = MathHelper.Double(5);
                var b = MathHelper.Double(10);
            }
        }
        """;

    [Fact]
    public async Task Replaces_call_sites_with_the_expression()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("var a = 5 * 2;", content);
        Assert.Contains("var b = 10 * 2;", content);
    }

    [Fact]
    public async Task Replaces_call_sites_with_the_return_expression()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", """
            namespace MathApp;

            public class MathHelper
            {
                public static int Triple(int x) { return x * 3; }
            }

            public class Calc
            {
                void Run()
                {
                    var result = MathHelper.Triple(4);
                }
            }
            """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Triple", "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("4 * 3", content);
    }

    [Fact]
    public async Task Deletes_the_inlined_method()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.DoesNotContain("public int Double(", content);
    }

    [Fact]
    public async Task Inlines_every_call_site()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", """
            namespace MathApp;

            public class MathHelper
            {
                public static int Square(int x) => x * x;
            }

            public class Calc
            {
                void Run()
                {
                    var a = MathHelper.Square(2);
                    var b = MathHelper.Square(3);
                    var c = MathHelper.Square(4);
                }
            }
            """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Square", "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("2 * 2", content);
        Assert.Contains("3 * 3", content);
        Assert.Contains("4 * 4", content);
        Assert.DoesNotContain("public int Square(", content);
    }

    [Fact]
    public async Task Does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", dryRun: true);

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Equal(originalContent, afterContent);
    }

    [Fact]
    public async Task Does_not_modify_files_for_multi_statement_body()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", """
            namespace MathApp;

            public class MathHelper
            {
                public static int Complex(int x)
                {
                    var y = x + 1;
                    return y * 2;
                }
            }

            public class Calc
            {
                void Run() { var r = MathHelper.Complex(3); }
            }
            """);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Complex", "ignored", dryRun: false);

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        // Multi-statement block body is not inlinable — file must be unchanged
        Assert.Equal(originalContent, afterContent);
    }

    [Fact]
    public async Task Does_not_modify_files_when_method_not_found()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await InlineMethodCommand.ExecuteAsync("MathHelper", "NonExistent", "ignored", dryRun: false);

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Equal(originalContent, afterContent);
    }
}
