using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
///     Detects which varying identifier positions (from Phase 2 analysis) are in
///     type-argument positions and should become generic type parameters rather than
///     value parameters. Used by MethodExtractorDuplicates (Phase 6 / US-049).
///     v1 scope: IdentifierNameSyntax nodes whose parent is TypeArgumentListSyntax
///     (e.g. List&lt;MyType&gt;) or who appear as the type in ObjectCreationExpressionSyntax
///     (e.g. new MyType()). Predefined keyword types (int, string) are out of scope
///     because PatternMatcher's structural matching already excludes them.
///     Constraint inference (where T : ...) is deferred.
/// </summary>
internal static class TypeParameterVariationDetector
{
    /// <summary>
    ///     Classifies the varying identifier positions into two disjoint sets:
    ///     those that are in type-argument position (should become type parameters)
    ///     and those in value position (should become ordinary value parameters).
    ///     Deduplication: multiple positions that share the same pattern identifier name
    ///     (e.g. MyType appears twice) map to a single type parameter.
    /// </summary>
    /// <param name="patternIdentifiers">
    ///     Ordered list of external identifiers from the source block (from
    ///     <see cref="IdentifierVariationDetector.ExtractOrderedIdentifiers" />).
    /// </param>
    /// <param name="varyingPositions">
    ///     Indices into <paramref name="patternIdentifiers" /> that vary across matches.
    /// </param>
    /// <param name="sourceStatements">
    ///     The extracted source statements, used to inspect AST structure at each position.
    /// </param>
    /// <returns>
    ///     <c>typeParamNames</c>: ordered list of unique pattern names that should become
    ///     type parameters, in the order they are first encountered.
    ///     <c>valuePositions</c>: positions that are NOT in type-argument position and
    ///     should remain as value parameters.
    /// </returns>
    internal static (IReadOnlyList<string> typeParamNames, HashSet<int> valuePositions) ClassifyVaryingPositions(
        IReadOnlyList<string> patternIdentifiers,
        HashSet<int> varyingPositions,
        IReadOnlyList<StatementSyntax> sourceStatements)
    {
        if (varyingPositions.Count == 0)
            return (Array.Empty<string>(), new HashSet<int>());

        // Build position -> is-type-position map by walking the statements
        var typePositionsByName = new HashSet<string>(StringComparer.Ordinal);
        var visitor = new TypeArgumentIdentifierVisitor();
        foreach (var stmt in sourceStatements)
            visitor.Visit(stmt);
        var typeArgIdentifiers = visitor.TypeArgumentIdentifiers;

        // For each varying position, classify based on whether the pattern identifier
        // appears in a type-argument position in the source statements
        var typeParamNames = new List<string>(); // ordered, unique
        var seenTypeParamNames = new HashSet<string>(StringComparer.Ordinal);
        var valuePositions = new HashSet<int>();

        foreach (var pos in varyingPositions.OrderBy(p => p))
        {
            if (pos >= patternIdentifiers.Count)
                continue;

            var patternName = patternIdentifiers[pos];

            if (typeArgIdentifiers.Contains(patternName))
            {
                // Type-argument position: becomes a type parameter (deduplicated by name)
                if (seenTypeParamNames.Add(patternName))
                    typeParamNames.Add(patternName);
            }
            else
            {
                // Value position: becomes an ordinary value parameter
                valuePositions.Add(pos);
            }
        }

        return (typeParamNames, valuePositions);
    }

    /// <summary>
    ///     Assigns generic type parameter names (T, T1, T2, ...) to the given ordered
    ///     list of unique pattern type names. Returns a mapping from pattern name to type
    ///     parameter name.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> AssignTypeParameterNames(
        IReadOnlyList<string> typeParamNames)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < typeParamNames.Count; i++)
        {
            var tpName = i == 0 ? "T" : "T" + i.ToString(CultureInfo.InvariantCulture);
            result[typeParamNames[i]] = tpName;
        }

        return result;
    }

    /// <summary>
    ///     Walks statements collecting all IdentifierNameSyntax nodes that appear
    ///     in type-argument position. An identifier is in "type position" when:
    ///     - Its parent is TypeArgumentListSyntax (e.g. List&lt;MyType&gt;)
    ///     - It is the Type of an ObjectCreationExpressionSyntax (e.g. new MyType())
    /// </summary>
    private sealed class TypeArgumentIdentifierVisitor : CSharpSyntaxWalker
    {
        public HashSet<string> TypeArgumentIdentifiers { get; } = new(StringComparer.Ordinal);

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.Text;

            // Case 1: direct type argument (List<MyType> -> MyType's parent is TypeArgumentListSyntax)
            if (node.Parent is TypeArgumentListSyntax)
                TypeArgumentIdentifiers.Add(name);
            // Case 2: object creation type (new MyType() -> parent is ObjectCreationExpressionSyntax)
            else if (node.Parent is ObjectCreationExpressionSyntax objCreation
                     && objCreation.Type == node)
                TypeArgumentIdentifiers.Add(name);

            base.VisitIdentifierName(node);
        }
    }
}
