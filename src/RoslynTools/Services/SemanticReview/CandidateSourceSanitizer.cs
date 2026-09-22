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
            return trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                   || trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                ? base.VisitTrivia(trivia)
                : SyntaxFactory.ElasticSpace;
        }

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression)
                || node.IsKind(SyntaxKind.Utf8StringLiteralExpression))
            {
                return SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal("<redacted-string>"))
                    .WithLeadingTrivia(node.GetLeadingTrivia().Select(VisitTrivia))
                    .WithTrailingTrivia(node.GetTrailingTrivia().Select(VisitTrivia));
            }

            if (node.IsKind(SyntaxKind.CharacterLiteralExpression))
            {
                return SyntaxFactory.LiteralExpression(
                        SyntaxKind.CharacterLiteralExpression,
                        SyntaxFactory.Literal('?'))
                    .WithLeadingTrivia(node.GetLeadingTrivia().Select(VisitTrivia))
                    .WithTrailingTrivia(node.GetTrailingTrivia().Select(VisitTrivia));
            }

            return base.VisitLiteralExpression(node);
        }

        public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node) =>
            SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal("<redacted-string>"))
                .WithLeadingTrivia(node.GetLeadingTrivia().Select(VisitTrivia))
                .WithTrailingTrivia(node.GetTrailingTrivia().Select(VisitTrivia));
    }
}
