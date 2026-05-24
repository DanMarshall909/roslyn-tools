using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spectre.Console;

namespace RoslynTools.Rendering;

/// <summary>
/// Renders a single line of C# source code as Spectre.Console markup with
/// token-level colouring (keywords, strings, numbers, comments).
/// </summary>
internal static class CSharpHighlighter
{
    internal static string ToMarkup(string codeLine)
    {
        if (string.IsNullOrEmpty(codeLine))
            return string.Empty;

        var sb = new StringBuilder(codeLine.Length + 16);
        var tokens = SyntaxFactory.ParseTokens(codeLine).ToList();

        foreach (var token in tokens)
        {
            EmitTriviaList(sb, token.LeadingTrivia);
            EmitToken(sb, token);
            EmitTriviaList(sb, token.TrailingTrivia);
        }

        return sb.ToString();
    }

    private static void EmitToken(StringBuilder sb, SyntaxToken token)
    {
        var text = token.ToString();
        if (text.Length == 0)
            return;

        var safe = Markup.Escape(text);
        var color = ColourFor(token.Kind());
        AppendStyled(sb, safe, color);
    }

    private static void EmitTriviaList(StringBuilder sb, SyntaxTriviaList trivia)
    {
        foreach (var t in trivia)
        {
            var text = t.ToString();
            if (text.Length == 0)
                continue;

            var safe = Markup.Escape(text);
            var color = TriviaColour(t.Kind());
            AppendStyled(sb, safe, color);
        }
    }

    private static void AppendStyled(StringBuilder sb, string escapedText, string? color)
    {
        if (color is null)
        {
            sb.Append(escapedText);
        }
        else
        {
            sb.Append('[').Append(color).Append(']').Append(escapedText).Append("[/]");
        }
    }

    private static string? ColourFor(SyntaxKind kind)
    {
        if (SyntaxFacts.IsKeywordKind(kind) || SyntaxFacts.IsContextualKeyword(kind))
            return "blue";

        return kind switch
        {
            SyntaxKind.StringLiteralToken
                or SyntaxKind.SingleLineRawStringLiteralToken
                or SyntaxKind.MultiLineRawStringLiteralToken
                or SyntaxKind.Utf8StringLiteralToken
                or SyntaxKind.Utf8SingleLineRawStringLiteralToken
                or SyntaxKind.Utf8MultiLineRawStringLiteralToken
                or SyntaxKind.InterpolatedStringStartToken
                or SyntaxKind.InterpolatedStringEndToken
                or SyntaxKind.InterpolatedVerbatimStringStartToken
                or SyntaxKind.InterpolatedStringTextToken
                or SyntaxKind.CharacterLiteralToken => "darkorange3",
            SyntaxKind.NumericLiteralToken => "cyan",
            _ => null,
        };
    }

    private static string? TriviaColour(SyntaxKind kind) => kind switch
    {
        SyntaxKind.SingleLineCommentTrivia
            or SyntaxKind.MultiLineCommentTrivia
            or SyntaxKind.SingleLineDocumentationCommentTrivia
            or SyntaxKind.MultiLineDocumentationCommentTrivia
            or SyntaxKind.DocumentationCommentExteriorTrivia => "grey",
        _ => null,
    };
}
