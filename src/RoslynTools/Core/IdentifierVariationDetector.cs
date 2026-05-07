using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
///     Detects which identifier positions (by traversal order within the extracted block)
///     vary across structural duplicates. Used by MethodExtractorDuplicates (Phase 2)
///     to determine which identifiers should be parameterized.
/// </summary>
internal static class IdentifierVariationDetector
{
    /// <summary>
    ///     Given the ordered list of identifiers in the pattern block (in traversal order)
    ///     and the Substitutions map for each match, returns the set of positional indices
    ///     where the identifier value differs from the pattern across any match.
    /// </summary>
    /// <param name="patternIdentifiers">
    ///     Ordered identifiers from the source/pattern block (extracted via
    ///     <see cref="ExtractOrderedIdentifiers" />). These are the canonical names.
    /// </param>
    /// <param name="matchSubstitutions">
    ///     Per-match Substitutions dictionaries from <see cref="PatternMatch" />.
    ///     Each maps pattern identifier name → the actual name used at that match site.
    /// </param>
    /// <returns>
    ///     Set of positions (0-based, in <paramref name="patternIdentifiers" /> order)
    ///     where at least one match uses a different identifier than the pattern.
    /// </returns>
    internal static HashSet<int> FindVaryingPositions(
        IReadOnlyList<string> patternIdentifiers,
        IReadOnlyList<Dictionary<string, string>> matchSubstitutions)
    {
        var varyingPositions = new HashSet<int>();

        if (patternIdentifiers.Count == 0 || matchSubstitutions.Count == 0)
            return varyingPositions;

        for (var pos = 0; pos < patternIdentifiers.Count; pos++)
        {
            var patternName = patternIdentifiers[pos];

            foreach (var substitutions in matchSubstitutions)
            {
                if (!substitutions.TryGetValue(patternName, out var matchedName))
                    continue;

                if (matchedName != patternName)
                {
                    varyingPositions.Add(pos);
                    break;
                }
            }
        }

        return varyingPositions;
    }

    /// <summary>
    ///     Extracts all simple identifier references (IdentifierNameSyntax) from a block
    ///     in traversal order. Filters out identifiers that are declared within the block
    ///     itself (those are internal locals and don't need parameterization).
    ///     Only captures identifiers that resolve to symbols declared outside the block.
    /// </summary>
    internal static List<string> ExtractOrderedIdentifiers(
        IReadOnlyList<StatementSyntax> statements)
    {
        var identifiers = new List<string>();
        var visitor = new OrderedIdentifierVisitor();
        foreach (var stmt in statements)
            visitor.Visit(stmt);
        return visitor.Identifiers;
    }

    /// <summary>
    ///     Walks statements collecting all simple IdentifierNameSyntax references in
    ///     traversal order. Excludes identifiers on the left-hand side of declarations
    ///     (those are being introduced by the block, not referenced from outside).
    /// </summary>
    private sealed class OrderedIdentifierVisitor : CSharpSyntaxWalker
    {
        // Track names declared inside the block so we can exclude them
        private readonly HashSet<string> _declaredInBlock = new(StringComparer.Ordinal);

        public List<string> Identifiers { get; } = new();

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            // Record the declared name so uses of it are not counted as "external"
            _declaredInBlock.Add(node.Identifier.Text);
            // Still walk the initializer — it may reference external identifiers
            if (node.Initializer != null)
                Visit(node.Initializer);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.Text;

            // Skip if declared inside the block
            if (_declaredInBlock.Contains(name))
                return;

            // Skip contextual keywords used as names (var, dynamic, etc.)
            if (name is "var" or "dynamic" or "string" or "int" or "bool" or "object"
                or "float" or "double" or "decimal" or "long" or "short" or "byte"
                or "char" or "uint" or "ulong" or "ushort" or "sbyte" or "nint" or "nuint")
                return;

            // v1 scope: skip identifiers that are the expression side of member access
            // (e.g. _service in _service.Method() or _field.Prop). These require type
            // inference to parameterize correctly; complex expressions are Phase 3+.
            if (node.Parent is MemberAccessExpressionSyntax ma && ma.Expression == node)
                return;

            // Skip identifiers that are direct invocation targets (e.g. Method())
            if (node.Parent is InvocationExpressionSyntax inv && inv.Expression == node)
                return;

            Identifiers.Add(name);
        }
    }
}
