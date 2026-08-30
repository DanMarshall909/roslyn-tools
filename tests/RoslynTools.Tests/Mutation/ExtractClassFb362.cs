using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

/// <summary>
/// Regression coverage for FB-362: extract-class produced output that was
/// indentation-mangled, under-promoted moved members, and dropped nested types.
/// </summary>
public class ExtractClassFb362 : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ExtractClassFb362(RtServicesFixture services) => _services = services;

    [Fact]
    public async Task moved_member_bodies_keep_their_nested_indentation()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               public static int Score(int[] values)
                                               {
                                                   var total = 0;
                                                   foreach (var value in values)
                                                   {
                                                       if (value > 0)
                                                       {
                                                           total += value;
                                                       }
                                                   }

                                                   return total;
                                               }
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExecuteExtractClassAsync(
                "BigService", "Score", "Scoring", true, "ignored", null, false);
        }

        var extracted = await project.ReadSourceAsync("Scoring.cs");

        // The innermost statement must be indented deeper than the foreach,
        // which is deeper than the method signature. A flat single-indent
        // (the bug) would put them all at the same column.
        extracted.ShouldContain("                total += value;", Case.Sensitive);
        extracted.ShouldContain("        foreach (var value in values)", Case.Sensitive);
    }

    [Fact]
    public async Task moved_member_with_verbatim_string_preserves_literal_whitespace()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """"
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               public static string Template()
                                               {
                                                   return @"line one
                                                       indented line two";
                                               }
                                           }
                                           """");
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExecuteExtractClassAsync(
                "BigService", "Template", "Templates", true, "ignored", null, false);
        }

        var extracted = await project.ReadSourceAsync("Templates.cs");

        // The second line of the verbatim string is part of the literal's
        // value. Its leading whitespace must survive byte-for-byte — the
        // formatter must NOT re-indent inside a verbatim literal (doing so would
        // change the runtime string). In the source raw-string fixture the
        // continuation de-indents to 12 spaces; the extraction must preserve
        // exactly that, NOT bump it to the new method's code-nesting depth.
        extracted.ShouldContain("            indented line two\";", Case.Sensitive);
    }

    [Fact]
    public async Task extracted_class_is_already_formatting_clean()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               public static int Score(int[] values)
                                               {
                                                   var total = 0;
                                                   foreach (var value in values)
                                                   {
                                                       if (value > 0)
                                                       {
                                                           total += value;
                                                       }
                                                   }

                                                   return total;
                                               }
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExecuteExtractClassAsync(
                "BigService", "Score", "Scoring", true, "ignored", null, false);
        }

        var extracted = await project.ReadSourceAsync("Scoring.cs");

        // Running the Roslyn formatter over the emitted text must be a no-op:
        // if the output were mis-indented, the formatter would change it.
        using var formatWorkspace = new AdhocWorkspace();
        var tree = CSharpSyntaxTree.ParseText(extracted);
        var formatted = Formatter.Format(tree.GetRoot(), formatWorkspace).ToFullString();
        formatted.ShouldBe(extracted);
    }

    [Fact]
    public async Task moved_member_still_called_by_origin_is_not_left_private()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               public static int PublicEntry(int x) => Helper(x);

                                               private static int Helper(int x) => x * 2;
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExecuteExtractClassAsync(
                "BigService", "Helper", "Maths", true, "ignored", null, false);
        }

        var extracted = await project.ReadSourceAsync("Maths.cs");

        // BigService.PublicEntry still calls Helper, so once Helper moves it
        // must be reachable from the origin — i.e. not private.
        extracted.ShouldNotContain("private static int Helper", Case.Sensitive);
        extracted.ShouldContain("internal static int Helper", Case.Sensitive);
    }

    [Fact]
    public async Task moved_member_referenced_only_internally_keeps_its_modifier()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               public static int PublicEntry(int x) => Helper(x);

                                               private static int Helper(int x) => Twice(x);

                                               private static int Twice(int x) => x * 2;
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExecuteExtractClassAsync(
                "BigService", "Helper,Twice", "Maths", true, "ignored", null, false);
        }

        var extracted = await project.ReadSourceAsync("Maths.cs");

        // Twice is only ever called by Helper, which moved with it, so there
        // is no cross-boundary call — its private modifier must be untouched.
        extracted.ShouldContain("private static int Twice", Case.Sensitive);
    }

    [Fact]
    public async Task nested_type_named_in_members_moves_to_the_new_class()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("BigService.cs", """
                                           namespace MyApp;

                                           public class BigService
                                           {
                                               public static Point Origin() => new Point();

                                               public sealed class Point
                                               {
                                                   public int X { get; set; }
                                                   public int Y { get; set; }
                                               }
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExecuteExtractClassAsync(
                "BigService", "Origin,Point", "Geometry", true, "ignored", null, false);
        }

        var extracted = await project.ReadSourceAsync("Geometry.cs");
        var source = await project.ReadSourceAsync("BigService.cs");

        extracted.ShouldContain("class Point", Case.Sensitive);
        source.ShouldNotContain("class Point", Case.Sensitive);
    }

    private async Task ExecuteExtractClassAsync(
        string typeName,
        string members,
        string newTypeName,
        bool isStatic,
        string? solutionPath,
        string? outputDir,
        bool dryRun)
    {
        (await _services.Resolve<ExtractClassOperation>().ExecuteAsync(new ExtractClassRequest(
            typeName,
            members,
            newTypeName,
            isStatic,
            outputDir,
            solutionPath,
            dryRun))).Unwrap();
    }
}
