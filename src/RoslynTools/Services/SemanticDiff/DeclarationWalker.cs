using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Shared declaration index: parses in-memory C# snapshots and keys every type and
///     member declaration by a purely syntactic fully-qualified name. This is the single
///     owner of the FQN scheme — both <see cref="SemanticDiffService" /> (the comparison
///     engine) and the git-diff renderer resolve declarations through this one walker, so
///     the two hosts can never drift apart on how a declaration is named or located.
///     <para>
///         The FQN forms are: <b>types</b> = namespace + nested type path + name + generic
///         arity (<c>`N</c>); <b>members</b> = containing-type FQN + name + arity +
///         parenthesised parameter types as written (internal whitespace stripped);
///         <b>properties / fields / events</b> by name. One entry is produced per variable
///         declarator. Enum members are intentionally not reported.
///     </para>
/// </summary>
internal static class DeclarationWalker
{
    /// <summary>
    ///     Parse every source document and index its declarations by syntactic FQN.
    ///     Later sources / later declarations win on key collision (partial classes,
    ///     duplicate definitions) rather than throwing — the engine never assumes a
    ///     unique key per side.
    /// </summary>
    public static IReadOnlyDictionary<string, DeclEntry> Collect(IReadOnlyList<SemanticDiffSource> sources)
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
        // Every declaration yielded here is contained by `containingTypeFqn`: for a member
        // that is its declaring type's FQN; for a type/enum/delegate it is the enclosing
        // namespace (or enclosing type when nested). Recording it lets the rename pass
        // scope pairing to a shared container so two identical-bodied declarations in
        // unrelated containers never mis-pair as a rename (US-126).
        DeclEntry Entry(string fqn, DeclarationKind kind, SyntaxNode node) =>
            new(fqn, kind, path, node, containingTypeFqn);

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
                    yield return Entry(typeFqn, TypeKind(type), type);
                    foreach (var entry in Walk(type.Members, typeFqn, path))
                        yield return entry;
                    break;

                case EnumDeclarationSyntax @enum:
                    // Enum members are not reported as declarations — only the enum type.
                    var enumFqn = Join(containingTypeFqn, @enum.Identifier.Text);
                    yield return Entry(enumFqn, DeclarationKind.Enum, @enum);
                    break;

                case DelegateDeclarationSyntax @delegate:
                    var delegateFqn = Join(
                        containingTypeFqn,
                        NameWithArity(@delegate.Identifier, @delegate.TypeParameterList));
                    yield return Entry(delegateFqn, DeclarationKind.Delegate, @delegate);
                    break;

                case MethodDeclarationSyntax method:
                    yield return Entry(
                        MemberFqn(
                            containingTypeFqn,
                            NameWithArity(method.Identifier, method.TypeParameterList),
                            method.ParameterList),
                        DeclarationKind.Method,
                        method);
                    break;

                case ConstructorDeclarationSyntax ctor:
                    yield return Entry(
                        MemberFqn(containingTypeFqn, ctor.Identifier.Text, ctor.ParameterList),
                        DeclarationKind.Constructor,
                        ctor);
                    break;

                case OperatorDeclarationSyntax op:
                    yield return Entry(
                        MemberFqn(
                            containingTypeFqn,
                            "operator " + op.OperatorToken.Text,
                            op.ParameterList),
                        DeclarationKind.Operator,
                        op);
                    break;

                case ConversionOperatorDeclarationSyntax conv:
                    yield return Entry(
                        MemberFqn(
                            containingTypeFqn,
                            "operator " + conv.Type.ToString(),
                            conv.ParameterList),
                        DeclarationKind.Operator,
                        conv);
                    break;

                case IndexerDeclarationSyntax indexer:
                    yield return Entry(
                        MemberFqn(containingTypeFqn, "this", indexer.ParameterList),
                        DeclarationKind.Indexer,
                        indexer);
                    break;

                case PropertyDeclarationSyntax prop:
                    yield return Entry(
                        Join(containingTypeFqn, prop.Identifier.Text),
                        DeclarationKind.Property,
                        prop);
                    break;

                case EventDeclarationSyntax @event:
                    yield return Entry(
                        Join(containingTypeFqn, @event.Identifier.Text),
                        DeclarationKind.Event,
                        @event);
                    break;

                case EventFieldDeclarationSyntax eventField:
                    // One delta per declarator, each carrying a single-variable copy
                    // of the event field (modifiers + type + this one declarator) so
                    // every entry compares only its own declarator — `event H X, Y;`
                    // must not let an edit to Y bleed into X's classification.
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        yield return Entry(
                            Join(containingTypeFqn, variable.Identifier.Text),
                            DeclarationKind.Event,
                            eventField.WithDeclaration(
                                eventField.Declaration.WithVariables(
                                    SyntaxFactory.SingletonSeparatedList(variable))));
                    }

                    break;

                case FieldDeclarationSyntax field:
                    // One delta per declarator: `int a, b;` is two fields. Each entry
                    // carries a single-variable copy of the field (modifiers + type +
                    // this one declarator) so it compares only its own declarator; the
                    // whole multi-variable node would falsely mark a byte-identical
                    // declarator Changed when a sibling changes.
                    foreach (var variable in field.Declaration.Variables)
                    {
                        yield return Entry(
                            Join(containingTypeFqn, variable.Identifier.Text),
                            DeclarationKind.Field,
                            field.WithDeclaration(
                                field.Declaration.WithVariables(
                                    SyntaxFactory.SingletonSeparatedList(variable))));
                    }

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

    /// <summary>
    ///     Render a type/method name with generic arity as a <c>`N</c> suffix.
    ///     <para>
    ///         <b>Arity is part of the key, so a generic-arity change is reported as
    ///         remove + add, never Changed or Renamed.</b> <c>Box&lt;T&gt;</c> keys as
    ///         <c>Box`1</c> and <c>Box&lt;T, U&gt;</c> as <c>Box`2</c>: the two keys
    ///         never coincide, so the type appears Removed on the before side and Added
    ///         on the after side. Rename pairing cannot rescue it either —
    ///         <c>NormalizeIdentifier</c> blanks only the identifier token, not
    ///         the type-parameter list, so the canonical forms still differ by arity and
    ///         are not equivalent-modulo-name. This is a deliberate consequence of arity
    ///         living in the key, not a defect.
    ///     </para>
    /// </summary>
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
            // ParameterSyntax.Type is non-null for every parameter in a well-formed
            // method/ctor/operator/indexer list. A null Type can only arise from a
            // parse-error-recovery tree (deferred to US-126), so no defensive `"?"`
            // branch is kept here — it would be unreachable (NoCoverage) cruft. The
            // null-forgiving operator preserves the NRT contract without introducing a
            // dead conditional branch.
            var type = parameters.Parameters[i].Type;
            sb.Append(StripWhitespace(type!.ToString()));
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
}

/// <summary>
///     Shared index entry: a declaration's syntactic key, kind, source path, and node, plus the
///     FQN of its container (declaring type for a member; enclosing namespace or type for a
///     type/enum/delegate; <c>null</c> at the compilation-unit root). The rename pass scopes
///     pairing to a shared <see cref="Container" /> so identical-bodied declarations in unrelated
///     containers never mis-pair as a rename.
/// </summary>
internal sealed record DeclEntry(string Fqn, DeclarationKind Kind, string Path, SyntaxNode Node, string? Container);
