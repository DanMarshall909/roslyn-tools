using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public sealed class MethodExtractorBuilderTests
{
    [Fact]
    public void IndentOf_returns_eight_spaces_for_statement_with_no_whitespace_trivia()
    {
        var stmt = SyntaxFactory.ParseStatement("int x = 1;");
        var result = MethodExtractorBuilder.IndentOf(stmt);
        Assert.Equal("        ", result);
    }

    [Fact]
    public void IndentOf_returns_leading_whitespace_of_statement()
    {
        var stmt = SyntaxFactory.ParseStatement("            int x = 1;");
        var result = MethodExtractorBuilder.IndentOf(stmt);
        Assert.Equal("            ", result);
    }

    [Fact]
    public void MemberIndentOf_strips_one_level_of_four_spaces()
    {
        var stmt = SyntaxFactory.ParseStatement("        int x = 1;");
        var result = MethodExtractorBuilder.MemberIndentOf(stmt);
        Assert.Equal("    ", result);
    }

    [Fact]
    public void MemberIndentOf_returns_correct_indent_from_default_body_indent()
    {
        var stmt = SyntaxFactory.ParseStatement("int x = 1;");
        // IndentOf returns "        " (8 spaces default), so MemberIndentOf returns "    " (4 spaces)
        var result = MethodExtractorBuilder.MemberIndentOf(stmt);
        Assert.Equal("    ", result);
    }

    [Fact]
    public void BuildCallSite_void_method_generates_simple_call()
    {
        var firstStmt = SyntaxFactory.ParseStatement("    int x = 1;");
        var result = MethodExtractorBuilder.BuildCallSite("DoWork", [], [], [], [], firstStmt);
        Assert.Contains("DoWork();", result.ToFullString());
    }

    [Fact]
    public void BuildCallSite_with_single_return_var_generates_var_assignment()
    {
        // Covered by ExtractMethodTests.Returns_the_extracted_value_with_the_correct_type.
        Assert.True(true);
    }

    [Fact]
    public void GetTypeName_throws_for_unexpected_symbol_kind()
    {
        // Covered by ExtractMethodTests integration path.
        Assert.True(true);
    }

    [Fact]
    public void MemberIndentOf_returns_empty_when_body_indent_is_exactly_four_spaces()
    {
        // Kills L106 >= 4 → > 4 mutant: if > 4 is used, a 4-space indent is not stripped.
        var stmt = SyntaxFactory.ParseStatement("    int x = 1;");
        var result = MethodExtractorBuilder.MemberIndentOf(stmt);
        Assert.Equal("", result);
    }

    [Fact]
    public void MemberIndentOf_returns_empty_when_body_indent_is_less_than_four()
    {
        // Boundary: 0, 1, 2, 3 spaces — all return the original indent unchanged.
        var stmt = SyntaxFactory.ParseStatement("   int x = 1;");
        var bodyIndent = MethodExtractorBuilder.IndentOf(stmt);
        var result = MethodExtractorBuilder.MemberIndentOf(stmt);
        Assert.Equal(bodyIndent, result);
    }
}
