using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services.SemanticReview;

internal static class CandidateSourceSanitizer
{
    internal static string Sanitize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var sanitized = new UntrustedDataRewriter().Visit(root);
        return sanitized?.NormalizeWhitespace(eol: "\n").ToFullString() ?? string.Empty;
    }

    private sealed class UntrustedDataRewriter : CSharpSyntaxRewriter
    {
        internal UntrustedDataRewriter() : base(visitIntoStructuredTrivia: true)
        {
        }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia))
            {
                return SyntaxFactory.ElasticSpace;
            }

            return base.VisitTrivia(trivia);
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression)
                || node.IsKind(SyntaxKind.Utf8StringLiteralExpression))
            {
                return SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("<redacted-string>"))
                    .WithTriviaFrom(node);
            }

            if (node.IsKind(SyntaxKind.CharacterLiteralExpression))
            {
                return SyntaxFactory.LiteralExpression(
                        SyntaxKind.CharacterLiteralExpression,
                        SyntaxFactory.Literal('?'))
                    .WithTriviaFrom(node);
            }

            return base.VisitLiteralExpression(node);
        }

        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node) =>
            SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal("<redacted-string>"))
                .WithTriviaFrom(node);
    }
}
