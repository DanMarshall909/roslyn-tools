using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
///     Rewrites a statement window into a canonical form so that structurally
///     identical code with renamed locals or changed literal values hashes to
///     the same string.
///     Canonical substitutions applied:
///     - Local variable identifiers replaced with $L0, $L1, … (first-use order)
///     - Parameter references replaced with $P0, $P1, … (first-use order)
///     - Integer literals replaced with $I0, $I1, …
///     - String literals replaced with $S0, $S1, …
///     - Boolean literals replaced with $B0, $B1, …
///     - Character literals replaced with $C0, $C1, …
///     - Other numeric literals (float/double/decimal/long) replaced with $N0, $N1, …
///     - All trivia (whitespace, comments) stripped
///     Type names and member-access targets are NOT canonicalised — invocation
///     target identity matters for extractability (a.Foo() and a.Bar() must not group).
/// </summary>
internal sealed class BlockNormalizer : CSharpSyntaxRewriter
{
    private readonly SemanticModel _model;
    private readonly Dictionary<string, string> _substitutions = new(StringComparer.Ordinal);
    private int _boolCounter;
    private int _charCounter;
    private int _intCounter;

    // Counters for placeholder sequences — one per category, order by first-use.
    private int _localCounter;
    private int _numericCounter;
    private int _paramCounter;
    private int _stringCounter;

    private BlockNormalizer(SemanticModel model)
        : base(false)
    {
        _model = model;
    }

    /// <summary>
    ///     Normalize <paramref name="statements" /> to canonical text and return the
    ///     substitution map used (placeholder → original token text).
    /// </summary>
    public static (string CanonicalText, IReadOnlyDictionary<string, string> Substitutions) Normalize(
        IReadOnlyList<StatementSyntax> statements,
        SemanticModel model)
    {
        var normalizer = new BlockNormalizer(model);
        var sb = new StringBuilder();
        foreach (var stmt in statements)
        {
            var rewritten = (StatementSyntax)normalizer.Visit(stmt);
            sb.Append(rewritten.NormalizeWhitespace().ToFullString());
        }

        return (sb.ToString(), normalizer._substitutions);
    }

    // ── Variable declarations ────────────────────────────────────────────────────

    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        // Canonicalise the declared variable name if it is a local symbol, so that
        // `int x = 1;` and `int alpha = 1;` normalise to the same declaration.
        var symbol = _model.GetDeclaredSymbol(node);
        if (symbol is ILocalSymbol)
        {
            var name = node.Identifier.Text;
            var placeholder = GetOrAdd(name, () => $"$L{_localCounter++}");
            var newIdentifier = SyntaxFactory.Identifier(placeholder).WithoutTrivia();
            var visited = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node)!;
            return visited.WithIdentifier(newIdentifier);
        }

        return base.VisitVariableDeclarator(node);
    }

    // ── Identifier nodes ─────────────────────────────────────────────────────────

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var name = node.Identifier.Text;
        var symbolInfo = _model.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        if (symbol is ILocalSymbol)
        {
            var placeholder = GetOrAdd(name, () => $"$L{_localCounter++}");
            return SyntaxFactory.IdentifierName(placeholder).WithoutTrivia();
        }

        if (symbol is IParameterSymbol)
        {
            var placeholder = GetOrAdd(name, () => $"$P{_paramCounter++}");
            return SyntaxFactory.IdentifierName(placeholder).WithoutTrivia();
        }

        // Type names, method names, field/property references — preserve verbatim.
        // Invocation target identity matters: a.Foo() and a.Bar() must not group.
        return base.VisitIdentifierName(node);
    }

    // ── Literal expressions ───────────────────────────────────────────────────────

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        string placeholder;
        if (node.IsKind(SyntaxKind.TrueLiteralExpression) || node.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            var key = $"__bool__{node.Token.Text}";
            placeholder = GetOrAdd(key, () => $"$B{_boolCounter++}");
            return SyntaxFactory.IdentifierName(placeholder).WithoutTrivia();
        }

        if (node.IsKind(SyntaxKind.StringLiteralExpression) || node.IsKind(SyntaxKind.Utf8StringLiteralExpression))
        {
            var key = $"__str__{node.Token.Text}";
            placeholder = GetOrAdd(key, () => $"$S{_stringCounter++}");
            return SyntaxFactory.IdentifierName(placeholder).WithoutTrivia();
        }

        if (node.IsKind(SyntaxKind.CharacterLiteralExpression))
        {
            var key = $"__char__{node.Token.Text}";
            placeholder = GetOrAdd(key, () => $"$C{_charCounter++}");
            return SyntaxFactory.IdentifierName(placeholder).WithoutTrivia();
        }

        if (node.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            var tokenText = node.Token.Text;
            // Distinguish integer from floating-point by presence of . / e / f / d / m / L / U suffixes.
            var isFloat = tokenText.IndexOfAny(['.', 'e', 'E', 'f', 'F', 'd', 'D', 'm', 'M']) >= 0;
            if (isFloat)
            {
                var key = $"__num__{tokenText}";
                placeholder = GetOrAdd(key, () => $"$N{_numericCounter++}");
            }
            else
            {
                var key = $"__int__{tokenText}";
                placeholder = GetOrAdd(key, () => $"$I{_intCounter++}");
            }

            return SyntaxFactory.IdentifierName(placeholder).WithoutTrivia();
        }

        if (node.IsKind(SyntaxKind.NullLiteralExpression))
        {
            // null literal treated as a special integer-like placeholder so different
            // `null` usages in structurally similar positions still group.
            var key = "__null__";
            placeholder = GetOrAdd(key, () => $"$I{_intCounter++}");
            return SyntaxFactory.IdentifierName(placeholder).WithoutTrivia();
        }

        return base.VisitLiteralExpression(node);
    }

    // ── Trivia stripping ─────────────────────────────────────────────────────────

    // Remove all leading/trailing trivia from every token so whitespace and
    // comment differences do not affect the canonical form.
    public override SyntaxToken VisitToken(SyntaxToken token)
    {
        return token.WithoutTrivia();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string GetOrAdd(string originalKey, Func<string> createPlaceholder)
    {
        if (_substitutions.TryGetValue(originalKey, out var existing))
            return existing;
        var ph = createPlaceholder();
        _substitutions[originalKey] = ph;
        return ph;
    }
}
