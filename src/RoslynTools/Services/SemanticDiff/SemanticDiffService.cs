using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Host-agnostic semantic comparison engine. Parses two in-memory snapshots of
///     C# source, keys every declaration by a syntactic fully-qualified name, and
///     reports per-declaration deltas (added / removed / unchanged for this cycle).
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class registered in DI per the static-to-instance migration (EP-006). CA1822 enforces the old static-when-stateless rule that conflicts with this direction; suppress per class until the migration completes and the rule is reviewed project-wide.")]
internal sealed class SemanticDiffService : IRtService
{
    /// <summary>
    ///     Compare the "before" and "after" snapshots of <paramref name="request" />
    ///     and return the per-declaration deltas, ordered by FQN.
    /// </summary>
    public SemanticDiffResult Compare(SemanticDiffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var before = Collect(request.Before);
        var after = Collect(request.After);

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        keys.UnionWith(before.Keys);
        keys.UnionWith(after.Keys);

        var deltas = new List<DeclarationDelta>(keys.Count);
        foreach (var key in keys)
        {
            var inBefore = before.TryGetValue(key, out var oldDecl);
            var inAfter = after.TryGetValue(key, out var newDecl);

            DeclarationStatus status;
            if (inBefore && inAfter)
                status = BodiesEquivalent(oldDecl!.Node, newDecl!.Node)
                    ? DeclarationStatus.Unchanged
                    : DeclarationStatus.Changed;
            else if (inAfter)
                status = DeclarationStatus.Added;
            else
                status = DeclarationStatus.Removed;

            // Prefer the after-side record for kind/fqn so renames of cosmetic
            // metadata follow the surviving declaration; both sides agree on key.
            var carrier = newDecl ?? oldDecl!;

            deltas.Add(new DeclarationDelta(
                Fqn: carrier.Fqn,
                Kind: carrier.Kind,
                Status: status,
                OldPath: inBefore ? oldDecl!.Path : null,
                NewPath: inAfter ? newDecl!.Path : null));
        }

        return new SemanticDiffResult(deltas);
    }

    /// <summary>
    ///     Seam for body-equality. The foundational cycle treats any declaration
    ///     present on both sides as unchanged; a later story replaces this with a
    ///     real comparison (e.g. <c>a.IsEquivalentTo(b, topLevel: false)</c>).
    /// </summary>
    private bool BodiesEquivalent(SyntaxNode a, SyntaxNode b) => true;

    /// <summary>
    ///     Parse every source document and index its declarations by syntactic FQN.
    ///     Later sources / later declarations win on key collision (partial classes,
    ///     duplicate definitions) rather than throwing — the engine never assumes a
    ///     unique key per side.
    /// </summary>
    private static Dictionary<string, DeclEntry> Collect(IReadOnlyList<SemanticDiffSource> sources)
    {
        var map = new Dictionary<string, DeclEntry>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(source.Text);
            var root = tree.GetCompilationUnitRoot();
            foreach (var entry in Walk(root.Members, containingTypeFqn: null, source.Path))
                map[entry.Fqn] = entry;
        }

        return map;
    }

    /// <summary>
    ///     Recursively walk the members of a namespace, compilation unit, or type,
    ///     yielding a <see cref="DeclEntry" /> for every type and member declaration.
    /// </summary>
    private static IEnumerable<DeclEntry> Walk(
        SyntaxList<MemberDeclarationSyntax> members,
        string? containingTypeFqn,
        string path)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    var nsPrefix = Join(containingTypeFqn, ns.Name.ToString());
                    foreach (var entry in Walk(ns.Members, nsPrefix, path))
                        yield return entry;
                    break;

                case TypeDeclarationSyntax type:
                    var typeFqn = Join(containingTypeFqn, NameWithArity(type.Identifier, type.TypeParameterList));
                    yield return new DeclEntry(typeFqn, TypeKind(type), path, type);
                    foreach (var entry in Walk(type.Members, typeFqn, path))
                        yield return entry;
                    break;

                case EnumDeclarationSyntax @enum:
                    // Enum members are not reported as declarations — only the enum type.
                    var enumFqn = Join(containingTypeFqn, @enum.Identifier.Text);
                    yield return new DeclEntry(enumFqn, DeclarationKind.Enum, path, @enum);
                    break;

                case DelegateDeclarationSyntax @delegate:
                    var delegateFqn = Join(
                        containingTypeFqn,
                        NameWithArity(@delegate.Identifier, @delegate.TypeParameterList));
                    yield return new DeclEntry(delegateFqn, DeclarationKind.Delegate, path, @delegate);
                    break;

                case MethodDeclarationSyntax method:
                    yield return new DeclEntry(
                        MemberFqn(
                            containingTypeFqn,
                            NameWithArity(method.Identifier, method.TypeParameterList),
                            method.ParameterList),
                        DeclarationKind.Method,
                        path,
                        method);
                    break;

                case ConstructorDeclarationSyntax ctor:
                    yield return new DeclEntry(
                        MemberFqn(containingTypeFqn, ctor.Identifier.Text, ctor.ParameterList),
                        DeclarationKind.Constructor,
                        path,
                        ctor);
                    break;

                case OperatorDeclarationSyntax op:
                    yield return new DeclEntry(
                        MemberFqn(
                            containingTypeFqn,
                            "operator " + op.OperatorToken.Text,
                            op.ParameterList),
                        DeclarationKind.Operator,
                        path,
                        op);
                    break;

                case ConversionOperatorDeclarationSyntax conv:
                    yield return new DeclEntry(
                        MemberFqn(
                            containingTypeFqn,
                            "operator " + conv.Type.ToString(),
                            conv.ParameterList),
                        DeclarationKind.Operator,
                        path,
                        conv);
                    break;

                case IndexerDeclarationSyntax indexer:
                    yield return new DeclEntry(
                        MemberFqn(containingTypeFqn, "this", indexer.ParameterList),
                        DeclarationKind.Indexer,
                        path,
                        indexer);
                    break;

                case PropertyDeclarationSyntax prop:
                    yield return new DeclEntry(
                        Join(containingTypeFqn, prop.Identifier.Text),
                        DeclarationKind.Property,
                        path,
                        prop);
                    break;

                case EventDeclarationSyntax @event:
                    yield return new DeclEntry(
                        Join(containingTypeFqn, @event.Identifier.Text),
                        DeclarationKind.Event,
                        path,
                        @event);
                    break;

                case EventFieldDeclarationSyntax eventField:
                    foreach (var variable in eventField.Declaration.Variables)
                        yield return new DeclEntry(
                            Join(containingTypeFqn, variable.Identifier.Text),
                            DeclarationKind.Event,
                            path,
                            eventField);
                    break;

                case FieldDeclarationSyntax field:
                    // One delta per declarator: `int a, b;` is two fields.
                    foreach (var variable in field.Declaration.Variables)
                        yield return new DeclEntry(
                            Join(containingTypeFqn, variable.Identifier.Text),
                            DeclarationKind.Field,
                            path,
                            field);
                    break;
            }
        }
    }

    private static DeclarationKind TypeKind(TypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax => DeclarationKind.Record,
        StructDeclarationSyntax => DeclarationKind.Struct,
        InterfaceDeclarationSyntax => DeclarationKind.Interface,
        _ => DeclarationKind.Class,
    };

    /// <summary>Append <paramref name="name" /> to a dotted prefix, or use it bare at the root.</summary>
    private static string Join(string? prefix, string name) =>
        string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;

    /// <summary>Render a type/method name with generic arity as a <c>`N</c> suffix.</summary>
    private static string NameWithArity(SyntaxToken identifier, TypeParameterListSyntax? typeParams)
    {
        var arity = typeParams?.Parameters.Count ?? 0;
        return arity == 0 ? identifier.Text : identifier.Text + "`" + arity;
    }

    /// <summary>
    ///     Build a member FQN: containing-type FQN + name + parenthesised parameter
    ///     types as written, with internal whitespace collapsed so
    ///     <c>(int, string)</c> and <c>(int,string)</c> key identically.
    /// </summary>
    private static string MemberFqn(string? containingTypeFqn, string name, BaseParameterListSyntax parameters)
    {
        var sb = new StringBuilder();
        sb.Append(Join(containingTypeFqn, name));
        sb.Append('(');
        for (var i = 0; i < parameters.Parameters.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            var type = parameters.Parameters[i].Type;
            sb.Append(type is null ? "?" : NormalizeWhitespace(type.ToString()));
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>Collapse all runs of whitespace in a type string to single spaces and trim.</summary>
    private static string NormalizeWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>Internal index entry: a declaration's syntactic key, kind, source path, and node.</summary>
    private sealed record DeclEntry(string Fqn, DeclarationKind Kind, string Path, SyntaxNode Node);
}
