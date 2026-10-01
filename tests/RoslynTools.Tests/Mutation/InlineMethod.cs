using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class InlineMethod : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public InlineMethod(RtServicesFixture services) => _services = services;

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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("MathHelper", "Double");
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        content.ShouldContain("var a = 5 * 2;", Case.Sensitive);
        content.ShouldContain("var b = 10 * 2;", Case.Sensitive);
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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("MathHelper", "Triple");
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        content.ShouldContain("4 * 3", Case.Sensitive);
    }

    [Fact]
    public async Task deletes_the_inlined_method()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("MathHelper", "Double");
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        content.ShouldNotContain("public int Double(", Case.Sensitive);
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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("MathHelper", "Square");
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        content.ShouldContain("2 * 2", Case.Sensitive);
        content.ShouldContain("3 * 3", Case.Sensitive);
        content.ShouldContain("4 * 4", Case.Sensitive);
        content.ShouldNotContain("public int Square(", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            var execution = await ExecuteAsync("MathHelper", "Double", dryRun: true);
            execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
            execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("MathHelper.cs");
        }

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        afterContent.ShouldBe(originalContent);
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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            var result = await ExecuteResultAsync("MathHelper", "Complex");
            result.IsErr.ShouldBeTrue();
        }

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        // Multi-statement block body is not inlinable — file must be unchanged
        afterContent.ShouldBe(originalContent);
    }

    [Fact]
    public async Task does_not_modify_files_when_method_not_found()
    {
        using var project = new AdhocProjectBuilder("MathApp");
        project.AddSource("MathHelper.cs", MathHelperSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("MathHelper.cs");

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            var result = await ExecuteResultAsync("MathHelper", "NonExistent");
            result.IsErr.ShouldBeTrue();
        }

        var afterContent = await project.ReadSourceAsync("MathHelper.cs");
        afterContent.ShouldBe(originalContent);
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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("MathHelper", "Double");
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        content.ShouldNotContain("public static int Double", Case.Sensitive);
        content.ShouldContain("public int Double(int x) => x + x;", Case.Sensitive);
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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("MathHelper", "Double");
        }

        var content = await project.ReadSourceAsync("MathHelper.cs");
        content.ShouldContain("(3 + 4) * 2", Case.Sensitive);
    }

    private async Task<MutationExecutionResult<InlineMethodResult>> ExecuteAsync(
        string type,
        string method,
        bool dryRun = false)
    {
        return (await ExecuteResultAsync(type, method, dryRun)).Unwrap();
    }

    private Task<Result<MutationExecutionResult<InlineMethodResult>, RtError>> ExecuteResultAsync(
        string type,
        string method,
        bool dryRun = false)
    {
        return _services.Resolve<InlineMethodOperation>().ExecuteAsync(
            new InlineMethodRequest(type, method, "ignored", dryRun));
    }
}
