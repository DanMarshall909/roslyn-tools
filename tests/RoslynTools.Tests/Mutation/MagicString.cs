using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public sealed class MagicString
{
    [Fact]
    public async Task groups_repeated_string_literals_by_value()
    {
        const string source = """
                              namespace Sample;

                              public class Orders
                              {
                                  public string First() => "checkout-flow";
                                  public string Second() => "checkout-flow";
                                  public string Different() => "payment-flow";
                              }
                              """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Orders.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var groups = await new MagicStringService().AnalyzeAsync(solution, minLength: 5, minOccurrences: 2);

            var group = Assert.Single(groups);
            Assert.Equal("checkout-flow", group.Value);
            Assert.Equal(2, group.Count);
            Assert.Equal(["First", "Second"], group.Occurrences.Select(o => o.MemberName!).ToArray());
            Assert.All(group.Occurrences, occurrence => Assert.Equal("Orders", occurrence.TypeName));
        }
    }

    [Fact]
    public async Task ignores_constants_short_values_whitespace_and_generated_files()
    {
        const string source = """
                              namespace Sample;

                              public class Messages
                              {
                                  private const string ConstantValue = "constant-value";

                                  public string Value() => "runtime-value";
                                  public string Short() => "abc";
                                  public string Blank() => "   ";
                              }
                              """;
        const string generatedSource = """
                                       namespace Sample;

                                       public class GeneratedMessages
                                       {
                                           public string Value() => "generated-value";
                                       }
                                       """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Messages.cs", source)
            .AddSource("Messages.g.cs", generatedSource);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var groups = await new MagicStringService().AnalyzeAsync(solution, minLength: 4, minOccurrences: 1);

            var group = Assert.Single(groups);
            Assert.Equal("runtime-value", group.Value);
            Assert.Equal("Value", Assert.Single(group.Occurrences).MemberName);
        }
    }

    [Fact]
    public async Task extracts_a_constant_for_matching_literals_in_the_containing_type()
    {
        const string source = """
                              namespace Sample;

                              public class Messages
                              {
                                  private const string Existing = "repeat-me";

                                  public string First() => "repeat-me";
                                  public string Second() => "repeat-me";
                              }

                              public class OtherMessages
                              {
                                  public string Value() => "repeat-me";
                              }
                              """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Messages.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var writer = new CapturingSolutionChangeWriter();
            var service = new MagicStringService(writer);
            var filePath = Path.Combine(project.ProjectDir, "Messages.cs");
            var line = GetLineNumber(source, "public string First() => \"repeat-me\";");

            await service.ExtractConstantAsync(solution, filePath, line, "RepeatMe", dryRun: false);

            var updatedText = await TestSolutionDocuments.GetTextAsync(writer.NewSolution!, "Messages.cs");
            var root = CSharpSyntaxTree.ParseText(updatedText).GetCompilationUnitRoot();
            var messages = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(type => type.Identifier.ValueText == "Messages");
            var otherMessages = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(type => type.Identifier.ValueText == "OtherMessages");

            Assert.Contains(messages.Members.OfType<FieldDeclarationSyntax>(), field =>
                field.Modifiers.Any(SyntaxKind.ConstKeyword)
                && field.Declaration.Variables.Any(variable =>
                    variable.Identifier.ValueText == "RepeatMe"
                    && variable.Initializer?.Value is LiteralExpressionSyntax literal
                    && literal.Token.ValueText == "repeat-me"));
            Assert.Contains(messages.Members.OfType<FieldDeclarationSyntax>(), field =>
                field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "Existing"));
            Assert.Equal("RepeatMe", GetExpressionBodyText(messages, "First"));
            Assert.Equal("RepeatMe", GetExpressionBodyText(messages, "Second"));
            Assert.Equal("\"repeat-me\"", GetExpressionBodyText(otherMessages, "Value"));
            Assert.False(writer.DryRun);
        }
    }

    [Fact]
    public async Task rejects_extraction_when_the_target_name_already_exists()
    {
        const string source = """
                              namespace Sample;

                              public class Messages
                              {
                                  private const string RepeatMe = "other";

                                  public string First() => "repeat-me";
                              }
                              """;

        using var project = new AdhocProjectBuilder()
            .AddSource("Messages.cs", source);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var service = new MagicStringService(new CapturingSolutionChangeWriter());
            var filePath = Path.Combine(project.ProjectDir, "Messages.cs");
            var line = GetLineNumber(source, "public string First() => \"repeat-me\";");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExtractConstantAsync(solution, filePath, line, "RepeatMe", dryRun: false));

            Assert.Contains("already contains a member named 'RepeatMe'", ex.Message);
        }
    }

    private static int GetLineNumber(string source, string text)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Trim() == text)
                return i + 1;

        throw new InvalidOperationException($"Could not find line containing '{text}'.");
    }

    private static string GetExpressionBodyText(ClassDeclarationSyntax type, string methodName)
    {
        return type.Members
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == methodName)
            .ExpressionBody!
            .Expression
            .ToString();
    }

}
