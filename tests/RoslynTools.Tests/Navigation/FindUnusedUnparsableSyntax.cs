using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Commands.Results;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

/// <summary>
/// Issue #128. A parse failure must never surface as analysis. When the parser cannot understand a
/// declaration it guesses at a member, and the guess is indistinguishable — to everything downstream
/// — from a real symbol nobody references. find-unused then offers to delete it. The fixture is the
/// declaration that exposed this: a C# <c>union</c>, preview syntax that a parser predating it
/// reads as a bodiless method whose return type is the word <c>union</c>.
/// </summary>
public class FindUnusedUnparsableSyntax
{
    private const string SolutionPath = "/path/to/App.slnx";

    /// <summary>
    /// A closed union, shaped like the declaration in the report: a preview type declaration whose
    /// name is followed by a parenthesised case list. A parser that predates the syntax reads
    /// <c>union</c> as the return type and <c>ValueExpression</c> as a method name. Nothing else in
    /// the fixture names the union, so a parser that does understand it has to report it as an
    /// unreferenced <em>type</em>.
    /// </summary>
    private const string UnionSource = """
                                       namespace Mechanics;

                                       public sealed record Literal(int Value);

                                       public sealed record Dice(int Sides);

                                       public readonly union ValueExpression(Literal, Dice);
                                       """;

    [Fact]
    public async Task syntax_the_parser_cannot_understand_is_never_offered_for_deletion()
    {
        using var project = new AdhocProjectBuilder(languageVersion: LanguageVersion.Latest);
        project.AddSource("ValueExpression.cs", UnionSource);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "methods");

            results.ShouldNotContain(
                r => r.Name == "ValueExpression",
                "a document the parser could not understand cannot support the claim that nothing "
                + "references what it appears to declare");
            FindUnusedResult.FromSymbols(results, SolutionPath).Symbols
                .SelectMany(symbol => symbol.SuggestedFixes)
                .ShouldBeEmpty("a mis-parsed fragment must never reach the caller as a delete command");
        }
    }

    [Fact]
    public async Task a_well_formed_declaration_beside_unreadable_syntax_makes_no_absence_claim()
    {
        using var project = new AdhocProjectBuilder(languageVersion: LanguageVersion.Latest);
        project.AddSource("Mechanics.cs", """
                                          namespace Mechanics;

                                          public sealed class Registry { }

                                          public readonly union ValueExpression(int, string);
                                          """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "types");

            results.ShouldNotContain(
                r => r.Name == "Registry",
                "the parser recovers from what it could not read by discarding source, and the "
                + "discarded source is exactly where a reference would have been — so no "
                + "declaration in the document is safe to call unreferenced, however well formed "
                + "it looks on its own");
        }
    }

    [Fact]
    public async Task a_declaration_that_is_not_a_method_declaration_is_not_an_unused_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Broken.cs", """
                                       public class Fragment
                                       {
                                           public void NotAMethodDeclaration();
                                       }
                                       """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "methods");

            results.ShouldNotContain(
                r => r.Name == "NotAMethodDeclaration",
                "a bodiless declaration that is not abstract, extern or partial does not declare a "
                + "method at all, and reporting one as an unused method asserts the parse was right");
        }
    }

    [Theory]
    [InlineData("public abstract class Host { public abstract void Orphan(); }")]
    [InlineData("public class Host { public static extern void Orphan(); }")]
    [InlineData("public partial class Host { public partial void Orphan(); }")]
    public async Task a_method_that_says_why_it_has_no_body_is_still_analysed(string source)
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Host.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution, "methods");

            results.ShouldContain(
                r => r.Name == "Orphan",
                "abstract, extern and partial are the three ways a real method declaration accounts "
                + "for a missing body, and refusing to analyse them would trade the false positive "
                + "for a blind spot covering every abstraction in the solution");
        }
    }

    [Fact]
    public async Task a_union_declaration_is_classified_as_a_type_when_the_parser_understands_it()
    {
        using var project = new AdhocProjectBuilder(languageVersion: LanguageVersion.Preview);
        project.AddSource("ValueExpression.cs", UnionSource);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(solution);

            results.ShouldContain(
                r => r.Name == "ValueExpression" && r.Kind == "type",
                "a union is a type declaration, and the kind rt reports is what tells the caller "
                + "whether the parser understood the source at all");
        }
    }
}
