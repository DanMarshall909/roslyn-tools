using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Host-agnostic semantic comparison engine. Parses two in-memory snapshots of
///     C# source, keys every declaration by a syntactic fully-qualified name, and
///     reports per-declaration deltas (added / removed / changed / unchanged).
///     Member change detection is trivia-insensitive; type-header comparison and
///     rename pairing are deferred to later cycles.
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
                status = BodiesEquivalent(oldDecl!.Node, newDecl!.Node, request.Options)
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
    ///     Equality for a declaration present on both sides, trivia-insensitive
    ///     throughout (<c>topLevel: false</c> ignores comments and whitespace but
    ///     preserves identifiers and literals).
    ///     <para>
    ///         For <b>type declarations</b> (class / struct / record / interface) the
    ///         member list is stripped (<c>WithMembers(default)</c>) before comparison,
    ///         so only the type's <i>own header</i> is compared — modifiers, identifier,
    ///         type-parameter list, base list, constraint clauses, attributes, and (for
    ///         records) the positional parameter list, which lives on the declaration
    ///         node rather than in <c>Members</c>. A member edit therefore does not
    ///         bubble up into the containing type; it is reported at member granularity.
    ///     </para>
    ///     <para>
    ///         <b>Enums</b> are handled the same way: their members are stripped, so an
    ///         enum-member change is not reported (a Cycle 1 decision) while a change to
    ///         the enum's own header (underlying type, attributes) is. A
    ///         <b>delegate</b> has no member list — it <i>is</i> a signature — so it
    ///         falls through to the whole-node comparison alongside members.
    ///     </para>
    ///     <para>
    ///         Each branch gates on <i>both</i> operands: FQN keying does not guarantee
    ///         the same syntactic category across sides (e.g. <c>class Foo</c> becoming
    ///         <c>enum Foo</c> keeps the key but flips the node type). A category flip
    ///         falls through to the final whole-node comparison, which reports it as not
    ///         equivalent (Changed) without an <see cref="InvalidCastException" />.
    ///     </para>
    ///     The <paramref name="opts" /> parameter is unused this cycle; a later cycle
    ///     consults it (e.g. <see cref="SemanticDiffOptions.IgnoreAccessibility" />)
    ///     without changing this signature.
    /// </summary>
    private static bool BodiesEquivalent(SyntaxNode a, SyntaxNode b, SemanticDiffOptions opts)
    {
        // Class / struct / record / interface: compare the header shell only, with the
        // member list removed so member edits/adds/removals do not mark the type Changed.
        if (a is TypeDeclarationSyntax typeA && b is TypeDeclarationSyntax typeB)
            return typeA.WithMembers(default).IsEquivalentTo(typeB.WithMembers(default), topLevel: false);

        // Enum: same shell-only comparison — enum-member changes are out of scope, but
        // the enum's own header (e.g. underlying type, attributes) is in scope.
        if (a is EnumDeclarationSyntax enumA && b is EnumDeclarationSyntax enumB)
            return enumA.WithMembers(default).IsEquivalentTo(enumB.WithMembers(default), topLevel: false);

        // Members and delegates: trivia-insensitive whole-node comparison. A delegate
        // is a signature with no member list, so the whole node IS its header.
        return a.IsEquivalentTo(b, topLevel: false);
    }

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
            // coverage: ParameterSyntax.Type is non-null for every parameter in a
            // well-formed method/ctor/operator/indexer list; the "?" placeholder is
            // a defensive guard for parse-error-recovery trees only.
            sb.Append(type is null ? "?" : StripWhitespace(type.ToString()));
        }

        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    ///     Remove all whitespace from a written type so parameter lists key
    ///     identically regardless of spacing — e.g. <c>Dictionary&lt;int, string&gt;</c>
    ///     and <c>Dictionary&lt;int,string&gt;</c> both become <c>Dictionary&lt;int,string&gt;</c>.
    /// </summary>
    private static string StripWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>Internal index entry: a declaration's syntactic key, kind, source path, and node.</summary>
    private sealed record DeclEntry(string Fqn, DeclarationKind Kind, string Path, SyntaxNode Node);
}
