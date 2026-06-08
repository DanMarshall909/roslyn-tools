using System.Diagnostics.CodeAnalysis;
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

        var before = DeclarationWalker.Collect(request.Before);
        var after = DeclarationWalker.Collect(request.After);

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
        IReadOnlyDictionary<string, DeclEntry> before,
        IReadOnlyDictionary<string, DeclEntry> after,
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
        // mutation: equivalent — BodiesEquivalent is only called for a both-present
        // declaration matched by FQN, so a and b already share the same name. Flipping
        // normalizeIdentifier to true blanks both identical names to the same blank
        // token, a no-op that cannot change the equivalence result.
        // Stryker disable once Boolean: equivalent — see comment above
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
        // mutation: equivalent — the blank token is applied to BOTH sides of the rename
        // comparison, so any constant identifier string yields the same equivalence
        // outcome; the specific value is irrelevant to the result.
        // Stryker disable once String: equivalent — see comment above
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
        // mutation: equivalent — the blank token is applied to BOTH sides of the rename
        // comparison, so any constant identifier string yields the same equivalence
        // outcome; the specific value is irrelevant to the result.
        // Stryker disable once String: equivalent — see comment above
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
        // mutation: equivalent (both arms) — flipping either to true cannot make an
        // indexer/operator actually pair as a rename: (a) NormalizeIdentifier has no arm
        // for them (falls to `_ => node`), so their name is never blanked; and (b) their
        // FQN encodes the discriminating part (`this(params)`, `operator <token>(params)`)
        // rather than a name, so a different FQN means different nodes that are never
        // equivalent-modulo-name. The guard's effect is therefore unobservable. Note the
        // default arm is `_ => true`, so DELETING these arms would APPLY the mutant, not
        // remove dead code — annotate-as-equivalent is the only correct move here.
        // Stryker disable once Boolean: equivalent — see comment above
        DeclarationKind.Indexer => false,
        // Stryker disable once Boolean: equivalent — see comment above
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
}
