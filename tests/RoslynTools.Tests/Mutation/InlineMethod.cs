using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class InlineMethod
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
    public async Task replaces_call_sites_with_the_expression()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", false);
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("var a = 5 * 2;", content);
        Assert.Contains("var b = 10 * 2;", content);
    }

    [Fact]
    public async Task replaces_call_sites_with_the_return_expression()
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
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Triple", "ignored", false);
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("4 * 3", content);
    }

    [Fact]
    public async Task deletes_the_inlined_method()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", false);
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.DoesNotContain("public int Double(", content);
    }

    [Fact]
    public async Task inlines_every_call_site()
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
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Square", "ignored", false);
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("2 * 2", content);
        Assert.Contains("3 * 3", content);
        Assert.Contains("4 * 4", content);
        Assert.DoesNotContain("public int Square(", content);
    }

    [Fact]
    public async Task does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", true);
        }

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Equal(originalContent, afterContent);
    }

    [Fact]
    public async Task does_not_modify_files_for_multi_statement_body()
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
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Complex", "ignored", false);
        }

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        // Multi-statement block body is not inlinable — file must be unchanged
        Assert.Equal(originalContent, afterContent);
    }

    [Fact]
    public async Task does_not_modify_files_when_method_not_found()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "NonExistent", "ignored", false);
        }

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Equal(originalContent, afterContent);
    }

    // Bug: tasks/dogfood-gaps.md:35 — method removal matches by identifier text only.
    // Order matters: OtherHelper is declared first so a name-only search hits it before MathHelper.
    [Fact]
    public async Task removes_only_the_method_in_the_target_type_when_name_collides()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", """
                                           namespace MathApp;

                                           public class OtherHelper
                                           {
                                               public int Double(int x) => x + x;
                                           }

                                           public class MathHelper
                                           {
                                               public static int Double(int x) => x * 2;
                                           }

                                           public class Calc
                                           {
                                               void Run() { var a = MathHelper.Double(5); }
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", false);
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.DoesNotContain("public static int Double", content);
        Assert.Contains("public int Double(int x) => x + x;", content);
    }

    // Bug: tasks/dogfood-gaps.md:36 — substituted compound args not parenthesised.
    // Without parens "3 + 4 * 2" = 11 (wrong precedence). With parens "(3 + 4) * 2" = 14.
    [Fact]
    public async Task parenthesises_compound_arguments_to_preserve_precedence()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", """
                                           namespace MathApp;

                                           public class MathHelper
                                           {
                                               public static int Double(int x) => x * 2;
                                           }

                                           public class Calc
                                           {
                                               int Run() => MathHelper.Double(3 + 4);
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await InlineMethodCommand.ExecuteAsync("MathHelper", "Double", "ignored", false);
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        Assert.Contains("(3 + 4) * 2", content);
    }
}
