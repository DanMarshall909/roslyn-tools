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

        if (request.Options.DetectRenames)
            deltas = DetectRenames(deltas, before, after, request.Options);

        return new SemanticDiffResult(deltas);
    }

    /// <summary>
    ///     Post-pass that collapses pairs of Added/Removed declarations that are a pure
    ///     rename (same <see cref="DeclarationKind" />, bodies equivalent ignoring their
    ///     own name) into a single <see cref="DeclarationStatus.Renamed" /> delta.
    ///     <para>
    ///         <b>Pairing is greedy and deterministic.</b> Removed declarations are
    ///         considered in Ordinal FQN order; for each, the first not-yet-paired Added
    ///         declaration — also iterated in Ordinal FQN order — of the same kind whose
    ///         body is equivalent-modulo-name is taken. Each Added pairs at most one
    ///         Removed; leftovers keep their original Added / Removed status. Two trivially
    ///         identical bodies (e.g. two <c>=&gt; 0</c> methods) can mis-pair, which is an
    ///         accepted, fully deterministic v1 behaviour.
    ///     </para>
    ///     <para>
    ///         <b>v1 limitation.</b> Only pure renames pair: a name change combined with a
    ///         body change is not equivalent-modulo-name, so it stays Added + Removed.
    ///     </para>
    ///     <para>
    ///         <b>Member cascade on a type rename.</b> Pairing is independent per kind, so
    ///         when a type is renamed its members — which key under the new type FQN — are
    ///         not Added/Removed at all (their FQN is present on both sides via the new
    ///         name only if the type kept its name; on a true type rename the members'
    ///         FQNs change too and they appear as their own Added/Removed pairs). Each such
    ///         member pair is evaluated on its own merits: an unchanged-body member cascades
    ///         to its own Renamed delta, a changed-body member stays Added + Removed. The
    ///         containing type's Renamed delta does not suppress or force its members'
    ///         classification.
    ///     </para>
    /// </summary>
    private static List<DeclarationDelta> DetectRenames(
        List<DeclarationDelta> deltas,
        Dictionary<string, DeclEntry> before,
        Dictionary<string, DeclEntry> after,
        SemanticDiffOptions opts)
    {
        var removed = deltas
            .Where(d => d.Status == DeclarationStatus.Removed && IsRenameable(d.Kind))
            .OrderBy(d => d.Fqn, StringComparer.Ordinal)
            .ToList();
        var added = deltas
            .Where(d => d.Status == DeclarationStatus.Added && IsRenameable(d.Kind))
            .OrderBy(d => d.Fqn, StringComparer.Ordinal)
            .ToList();

        // Fqn -> the paired counterpart, recorded so the rebuild can drop both originals
        // and emit one Renamed delta in their place.
        var pairedRemovedToAdded = new Dictionary<string, DeclarationDelta>(StringComparer.Ordinal);
        var pairedAddedFqns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rem in removed)
        {
            foreach (var add in added)
            {
                if (pairedAddedFqns.Contains(add.Fqn) || add.Kind != rem.Kind)
                    continue;

                if (!AreEquivalent(before[rem.Fqn].Node, after[add.Fqn].Node, opts, normalizeIdentifier: true))
                    continue;

                pairedRemovedToAdded[rem.Fqn] = add;
                pairedAddedFqns.Add(add.Fqn);
                break;
            }
        }

        if (pairedRemovedToAdded.Count == 0)
            return deltas;

        var result = new List<DeclarationDelta>(deltas.Count);
        foreach (var delta in deltas)
        {
            // Drop the Added half of every pair; it is re-emitted via its Removed half.
            if (delta.Status == DeclarationStatus.Added && pairedAddedFqns.Contains(delta.Fqn))
                continue;

            // Replace the Removed half with a single Renamed delta keyed on the new FQN.
            if (delta.Status == DeclarationStatus.Removed
                && pairedRemovedToAdded.TryGetValue(delta.Fqn, out var add))
            {
                result.Add(new DeclarationDelta(
                    Fqn: add.Fqn,
                    Kind: add.Kind,
                    Status: DeclarationStatus.Renamed,
                    OldPath: delta.OldPath,
                    NewPath: add.NewPath,
                    OldFqn: delta.Fqn,
                    NewFqn: add.Fqn));
                continue;
            }

            result.Add(delta);
        }

        result.Sort(static (x, y) => string.CompareOrdinal(x.Fqn, y.Fqn));
        return result;
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
    ///     When <see cref="SemanticDiffOptions.IgnoreAccessibility" /> is set, every
    ///     comparison operand is first passed through <see cref="StripAccessibility" />,
    ///     which removes accessibility modifiers (<c>public</c>, <c>private</c>, …) from
    ///     the node's own top-level modifier list before comparison. All other modifiers
    ///     (<c>static</c>, <c>sealed</c>, <c>abstract</c>, …) survive, so a non-accessibility
    ///     modifier change still reports Changed, and a genuine body difference is never
    ///     masked. With the flag off (default) the operands are untouched.
    /// </summary>
    private static bool BodiesEquivalent(SyntaxNode a, SyntaxNode b, SemanticDiffOptions opts) =>
        AreEquivalent(a, b, opts, normalizeIdentifier: false);

    /// <summary>
    ///     Equality for a pair of declarations after <see cref="Canonicalize" /> has put
    ///     both through the same trivia / type-header-shell / accessibility rules. When
    ///     <paramref name="normalizeIdentifier" /> is <c>true</c> the declaration's own
    ///     name is also blanked, so the comparison answers "equivalent modulo own name" —
    ///     the predicate the rename post-pass pairs on. The Changed path passes
    ///     <c>false</c>; the rename path passes <c>true</c>. Both share one comparison so
    ///     the shell-strip and accessibility-strip rules can never drift between them.
    /// </summary>
    private static bool AreEquivalent(SyntaxNode a, SyntaxNode b, SemanticDiffOptions opts, bool normalizeIdentifier) =>
        Canonicalize(a, opts, normalizeIdentifier)
            .IsEquivalentTo(Canonicalize(b, opts, normalizeIdentifier), topLevel: false);

    /// <summary>
    ///     Produce the comparison-ready form of <paramref name="node" />:
    ///     <list type="number">
    ///         <item>
    ///             For a type (class / struct / record / interface) or enum, strip the
    ///             member list (<c>WithMembers(default)</c>) so only the header shell is
    ///             compared — a member edit does not bubble up into the containing type.
    ///             A delegate or member keeps its whole node (a delegate signature IS its
    ///             header). Each branch gates on the node's own category, so a category
    ///             flip (<c>class Foo</c> → <c>enum Foo</c>) falls through unchanged and is
    ///             later reported not-equivalent rather than throwing.
    ///         </item>
    ///         <item>
    ///             When <paramref name="normalizeIdentifier" /> is set, blank the
    ///             declaration's own name via <see cref="NormalizeIdentifier" /> so two
    ///             declarations that differ only in their name canonicalize identically.
    ///         </item>
    ///         <item>
    ///             When <see cref="SemanticDiffOptions.IgnoreAccessibility" /> is set,
    ///             strip accessibility modifiers from the node's own header. Applied after
    ///             the shell strip so it targets the type's own modifiers, not the members
    ///             it would otherwise reach.
    ///         </item>
    ///     </list>
    /// </summary>
    private static SyntaxNode Canonicalize(SyntaxNode node, SemanticDiffOptions opts, bool normalizeIdentifier)
    {
        // Step 1: strip the member list for declarations that carry one, so the shell
        // alone is compared. TypeDeclarationSyntax and EnumDeclarationSyntax both expose
        // WithMembers; delegates and members fall through with the whole node intact.
        var shell = node switch
        {
            TypeDeclarationSyntax type => type.WithMembers(default),
            EnumDeclarationSyntax @enum => @enum.WithMembers(default),
            _ => node,
        };

        // Step 2 (rename path only): blank the declaration's own name.
        if (normalizeIdentifier)
            shell = NormalizeIdentifier(shell);

        // Step 3: strip accessibility modifiers when the option is set.
        return StripAccessibility(shell, opts);
    }

    /// <summary>
    ///     Blank the declaration's own identifier to a fixed placeholder so two
    ///     declarations that differ <i>only</i> in their name canonicalize to the same
    ///     node — the comparison the rename post-pass keys on. The name token lives in a
    ///     different place per syntax category:
    ///     <list type="bullet">
    ///         <item>Types, methods, delegates: the <c>Identifier</c> token.</item>
    ///         <item>Constructors: the <c>Identifier</c> token (it spells the type name).</item>
    ///         <item>Properties, declaration-form events: the <c>Identifier</c> token.</item>
    ///         <item>Fields and field-form events: each variable declarator's identifier.</item>
    ///     </list>
    ///     Indexers (named <c>this</c>) and operators (keyed on an operator token, not a
    ///     name) have no renameable own-name and are returned unchanged — the rename pass
    ///     never reaches them because it excludes those kinds before comparing.
    /// </summary>
    private static SyntaxNode NormalizeIdentifier(SyntaxNode node)
    {
        var blank = SyntaxFactory.Identifier(string.Empty);
        return node switch
        {
            TypeDeclarationSyntax type => type.WithIdentifier(blank),
            EnumDeclarationSyntax @enum => @enum.WithIdentifier(blank),
            DelegateDeclarationSyntax @delegate => @delegate.WithIdentifier(blank),
            MethodDeclarationSyntax method => method.WithIdentifier(blank),
            ConstructorDeclarationSyntax ctor => ctor.WithIdentifier(blank),
            PropertyDeclarationSyntax prop => prop.WithIdentifier(blank),
            EventDeclarationSyntax @event => @event.WithIdentifier(blank),
            FieldDeclarationSyntax field => field.WithDeclaration(BlankDeclarators(field.Declaration)),
            EventFieldDeclarationSyntax eventField =>
                eventField.WithDeclaration(BlankDeclarators(eventField.Declaration)),

            // coverage: unreachable through the public API. The rename pass only
            // normalizes declarations that survived the IsRenameable filter, and every
            // such kind has an explicit arm above; indexers/operators are excluded before
            // they reach here. The arm is a defensive identity for any future caller.
            _ => node,
        };
    }

    /// <summary>
    ///     Blank every variable declarator's name in a field/event-field declaration so
    ///     the declaration canonicalizes by type and initializer alone. Single-declarator
    ///     declarations (the rename case) are fully covered; multi-declarator forms are
    ///     blanked uniformly but are not a targeted rename scenario.
    /// </summary>
    private static VariableDeclarationSyntax BlankDeclarators(VariableDeclarationSyntax declaration)
    {
        var blanked = declaration.Variables.Select(static v => v.WithIdentifier(SyntaxFactory.Identifier(string.Empty)));
        return declaration.WithVariables(SyntaxFactory.SeparatedList(blanked));
    }

    /// <summary>
    ///     True for declaration kinds that have a renameable own-name and so can take
    ///     part in rename pairing. Indexers (<c>this</c>) and operators (keyed on an
    ///     operator/conversion token rather than a name) are excluded — there is no name
    ///     to rename, so blanking the identifier would mis-pair unrelated members.
    /// </summary>
    private static bool IsRenameable(DeclarationKind kind) => kind switch
    {
        DeclarationKind.Indexer => false,
        DeclarationKind.Operator => false,
        _ => true,
    };

    /// <summary>
    ///     When <see cref="SemanticDiffOptions.IgnoreAccessibility" /> is set, return a
    ///     copy of <paramref name="node" /> with accessibility modifiers removed from its
    ///     own top-level modifier list, leaving every other modifier intact; otherwise
    ///     return the node unchanged. Modifiers live on <see cref="MemberDeclarationSyntax.Modifiers" />,
    ///     which is the common base of every node compared here (types, enums, delegates,
    ///     and members), so a single helper covers all three comparison paths.
    /// </summary>
    private static SyntaxNode StripAccessibility(SyntaxNode node, SemanticDiffOptions opts)
    {
        // Flag off: never rebuild the node — the default path stays byte-for-byte the
        // pre-Cycle-4 behaviour, so accessibility differences still register as Changed.
        if (!opts.IgnoreAccessibility)
            return node;

        // coverage: every node reaching here originates from Walk, which only stores
        // MemberDeclarationSyntax subtypes; the guard is a defensive no-op for any
        // future non-member caller and is not exercised through the public API.
        if (node is not MemberDeclarationSyntax member)
            return node;

        var kept = member.Modifiers.Where(static t => !IsAccessibilityModifier(t));
        return member.WithModifiers(SyntaxFactory.TokenList(kept));
    }

    /// <summary>
    ///     True for the accessibility-modifier keywords <c>public</c>, <c>private</c>,
    ///     <c>protected</c>, <c>internal</c>, and <c>file</c>. Combined forms
    ///     (<c>protected internal</c>, <c>private protected</c>) are two separate tokens,
    ///     each matched independently, so both are stripped.
    /// </summary>
    private static bool IsAccessibilityModifier(SyntaxToken token) => token.Kind() switch
    {
        SyntaxKind.PublicKeyword => true,
        SyntaxKind.PrivateKeyword => true,
        SyntaxKind.ProtectedKeyword => true,
        SyntaxKind.InternalKeyword => true,
        SyntaxKind.FileKeyword => true,
        _ => false,
    };

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
