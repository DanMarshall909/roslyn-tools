using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

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
            var groups = await new MagicStringService().AnalyzeAsync(solution, 5, 2);

            var group = Assert.Single(groups);
            group.Value.ShouldBe("checkout-flow");
            group.Count.ShouldBe(2);
            group.Occurrences.Select(o => o.MemberName!).ToArray().ShouldBe(["First", "Second"]);
            Assert.All(group.Occurrences, occurrence => occurrence.TypeName.ShouldBe("Orders"));
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
            var groups = await new MagicStringService().AnalyzeAsync(solution, 4, 1);

            var group = Assert.Single(groups);
            group.Value.ShouldBe("runtime-value");
            Assert.Single(group.Occurrences).MemberName.ShouldBe("Value");
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
            var filePath = Path.Combine(project.ProjectDir, "Messages.cs");
            var line = GetLineNumber(source, "public string First() => \"repeat-me\";");

            var operation = new ExtractStringConstantOperation(
                new FixedWorkspaceSessionFactory(workspace, solution),
                new MagicStringService(writer),
                writer);
            var execution = (await operation.ExecuteAsync(new ExtractStringConstantRequest(
                filePath,
                line,
                "RepeatMe"))).Unwrap();

            execution.Status.ShouldBe(MutationExecutionStatus.Applied);
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
            messages.Members.OfType<FieldDeclarationSyntax>().ShouldContain(field =>
                field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "Existing"));
            GetExpressionBodyText(messages, "First").ShouldBe("RepeatMe");
            GetExpressionBodyText(messages, "Second").ShouldBe("RepeatMe");
            GetExpressionBodyText(otherMessages, "Value").ShouldBe("\"repeat-me\"");
            writer.DryRun.ShouldBe(false);
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
            var writer = new CapturingSolutionChangeWriter();
            var operation = new ExtractStringConstantOperation(
                new FixedWorkspaceSessionFactory(workspace, solution),
                new MagicStringService(writer),
                writer);
            var filePath = Path.Combine(project.ProjectDir, "Messages.cs");
            var line = GetLineNumber(source, "public string First() => \"repeat-me\";");

            var result = await operation.ExecuteAsync(new ExtractStringConstantRequest(
                filePath,
                line,
                "RepeatMe"));

            result.IsErr.ShouldBeTrue();
            result.Match(_ => string.Empty, error => error.Message)
                .ShouldContain("already contains a member named 'RepeatMe'", Case.Sensitive);
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

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
