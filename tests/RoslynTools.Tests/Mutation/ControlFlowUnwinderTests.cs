using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

/// <summary>
///     Tests for ControlFlowUnwinder — classification and strategy proposals for
///     duplicate groups blocked by control flow.
/// </summary>
public class ControlFlowUnwinderTests
{
    // ──────────────────────────────────────────────────────────────────────────
    //  AC #2 / AC #3: Classify control-flow patterns
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classifies_simple_guard_pattern_as_GuardReturn()
    {
        // Arrange: block is a single if (cond) return false; guard clause
        var stmts = ParseStatements(
            "if (string.IsNullOrWhiteSpace(filePath))\n" +
            "    return false;");

        // Act
        var result = ControlFlowUnwinder.Classify(stmts);

        // Assert
        Assert.Equal(ControlFlowPattern.GuardReturn, result.Pattern);
        Assert.Equal(ControlFlowStrategy.ExtractAsBoolGuard, result.Strategy);
    }

    [Fact]
    public void Classifies_method_ending_return_as_ValueReturn()
    {
        // Arrange: block contains statements followed by a return with a value
        var stmts = ParseStatements(
            "var result = Compute(x);\n" +
            "var cleaned = Trim(result);\n" +
            "return cleaned;");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.ValueReturn, result.Pattern);
        Assert.Equal(ControlFlowStrategy.ExtractWithReturnType, result.Strategy);
    }

    [Fact]
    public void Classifies_break_in_loop_as_BreakEscape()
    {
        // Arrange: block contains a break statement
        var stmts = ParseStatements(
            "if (items.Count > 10)\n" +
            "    break;\n" +
            "Process(items);");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.BreakEscape, result.Pattern);
        Assert.Equal(ControlFlowStrategy.LogAlternatives, result.Strategy);
    }

    [Fact]
    public void Classifies_continue_as_ContinueEscape()
    {
        // Arrange: block contains a continue statement
        var stmts = ParseStatements(
            "if (item == null)\n" +
            "    continue;\n" +
            "Process(item);");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.ContinueEscape, result.Pattern);
        Assert.Equal(ControlFlowStrategy.LogAlternatives, result.Strategy);
    }

    [Fact]
    public void Classifies_void_early_return_as_EarlyReturnVoid()
    {
        // Arrange: block has an early return with no value
        var stmts = ParseStatements(
            "if (value == null)\n" +
            "    return;\n" +
            "DoWork(value);");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.EarlyReturnVoid, result.Pattern);
        Assert.Equal(ControlFlowStrategy.ExtractAsBoolGuard, result.Strategy);
    }

    [Fact]
    public void Classifies_no_escape_as_None()
    {
        // Arrange: block has no control flow escape
        var stmts = ParseStatements(
            "var x = 1;\n" +
            "var y = 2;\n" +
            "System.Console.WriteLine(x + y);");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.None, result.Pattern);
        Assert.Equal(ControlFlowStrategy.NotNeeded, result.Strategy);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC #4: Auto-unwrap simple guard pattern (if-return) for extraction
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_pattern_becomes_extractable_via_MethodExtractor()
    {
        // Arrange: a method with a guard pattern (if-null return false) — previously
        // MethodExtractor threw "Cannot extract statements that contain return statements".
        // After guard-unwinding, extraction succeeds.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  namespace TestApp;

                                  public class ServiceA
                                  {
                                      private bool IsGeneratedOrIntermediate(string? filePath)
                                      {
                                          if (string.IsNullOrWhiteSpace(filePath))
                                              return false;
                                          if (filePath.Contains(".g.cs", System.StringComparison.Ordinal))
                                              return true;
                                          return false;
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var filePath = Path.Combine(project.ProjectDir, "A.cs");
            // This used to throw — verify it now succeeds
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                filePath,
                "CheckFilePath",
                7,
                8);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");

        // The extracted method returns bool (guard unwound, not void)
        Assert.Contains("private bool CheckFilePath(", contentA);

        // The call site uses the guard pattern
        Assert.Contains("if (CheckFilePath(", contentA);
        // The original guard if-statement is gone
        Assert.DoesNotContain("if (string.IsNullOrWhiteSpace(filePath))",
            GetMethodBody(contentA, "IsGeneratedOrIntermediate"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC #5: Call site for bool guard extraction becomes if-return pattern
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guard_extraction_rewrites_call_site_as_if_return()
    {
        // Arrange: extract a guard block that returns false when null
        using var project = new AdhocProjectBuilder();
        project.AddSource("Service.cs", """
                                        namespace TestApp;

                                        public class Service
                                        {
                                            public bool Validate(string? input)
                                            {
                                                if (string.IsNullOrWhiteSpace(input))
                                                    return false;
                                                return true;
                                            }
                                        }
                                        """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var filePath = Path.Combine(project.ProjectDir, "Service.cs");
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                filePath,
                "IsInputBlank",
                7,
                8);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var content = await project.ReadSourceAsync("Service.cs");

        // The extracted method exists and returns bool
        Assert.Contains("private bool IsInputBlank(string? input)", content);

        // The call site uses the guard pattern: if (IsInputBlank(input)) return false;
        Assert.Contains("if (IsInputBlank(input))", content);
        Assert.Contains("return false;", GetMethodBody(content, "Validate"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC #6: Break in loop — classify and log alternatives (no transform)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Break_in_loop_pattern_logs_alternatives_not_transforms()
    {
        var stmts = ParseStatements(
            "if (count > maxCount)\n" +
            "    break;\n" +
            "accumulator += count;");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.BreakEscape, result.Pattern);
        Assert.Equal(ControlFlowStrategy.LogAlternatives, result.Strategy);
        Assert.NotEmpty(result.AlternativeSuggestions);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC #7: Roslyn-tools groups — verify classification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Guard_pattern_with_return_false_classifies_correctly()
    {
        // Mirrors GRP-3-1cd9f103: ImportDirectiveService vs MagicStringService guard
        var stmts = ParseStatements(
            "if (string.IsNullOrWhiteSpace(filePath))\n" +
            "    return false;");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.GuardReturn, result.Pattern);
        Assert.Equal(ControlFlowStrategy.ExtractAsBoolGuard, result.Strategy);
        Assert.NotEmpty(result.Description);
    }

    [Fact]
    public void Multi_statement_block_with_trailing_return_classifies_as_ValueReturn()
    {
        // Mirrors GRP-7-3b447cb2 / GRP-8-b83743c4: Build() method bodies ending with return
        var stmts = ParseStatements(
            "var solutionOpt = CreateSolutionOption();\n" +
            "var typeOpt = new System.Collections.Generic.List<string>();\n" +
            "return typeOpt;");

        var result = ControlFlowUnwinder.Classify(stmts);

        Assert.Equal(ControlFlowPattern.ValueReturn, result.Pattern);
        Assert.Equal(ControlFlowStrategy.ExtractWithReturnType, result.Strategy);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static List<StatementSyntax> ParseStatements(string code)
    {
        var source = "class C { void M() {\n" + code + "\n} }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return method.Body!.Statements.ToList();
    }

    private static string GetMethodBody(string source, string methodName)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);
        return method?.Body?.ToFullString() ?? string.Empty;
    }
}
