using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Services;

/// <summary>
/// The documents that could possibly contain a source reference to a candidate symbol. find-unused
/// pays for one reference search per candidate, so the size of this set is the shape of its cost —
/// but the set is only worth narrowing while it still contains every document that could hold a
/// reference, because a document dropped from the search is a reference rt cannot see, and an unseen
/// reference becomes a high-confidence deletion candidate.
/// </summary>
/// <remarks>
/// The obvious rule — the declaring project plus the projects that transitively depend on it — is
/// correct only for a symbol that call sites must name directly. It is <em>wrong</em> for a member
/// that implements an interface or overrides a base member: those call sites name the interface or
/// base member, which is declared <em>upstream</em> of the implementation, so its callers live in a
/// part of the graph the implementing project knows nothing about. Scoping such a member to its own
/// dependents hides every dispatching call site and reports live code as dead.
/// <para>
/// So the scope is the union, over the candidate and every member it implements or overrides, of
/// each one's declaring project and that project's transitive dependents. Where any of those roots
/// comes from metadata rather than source — an override of a BCL method, an implementation of a
/// framework interface — the dispatching call site can be anywhere in the solution and nothing may
/// be excluded.
/// </para>
/// Two membership rules matter as much as the narrowing:
/// <list type="bullet">
///   <item>
///     Source-generated documents are included. A generator can emit the only call site a symbol
///     has, and dropping generated documents would turn live code into a deletion candidate.
///   </item>
///   <item>
///     Documents excluded by <c>.rtignore</c> are included. Ignoring a file means rt reports no
///     findings <em>about</em> it; it does not mean the file stopped calling things.
///   </item>
/// </list>
/// </remarks>
internal sealed class ReferenceSearchScope(Solution solution)
{
    private readonly Solution _solution = solution;

    private readonly Dictionary<ProjectId, IImmutableSet<Document>> _byDeclaringProject = new();

    private IImmutableSet<Document>? _everything;

    /// <summary>
    /// The documents worth searching for references to <paramref name="symbol" />.
    /// </summary>
    internal async ValueTask<IImmutableSet<Document>> ForAsync(
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var roots = DispatchRoots(symbol).ToArray();

        // A root the solution did not declare is reached through code rt cannot see, so the source
        // call sites that dispatch to it are not bounded by any project relationship here.
        if (roots.Any(root => OriginatingProject(root) is null))
            return await EverythingAsync(cancellationToken);

        var scope = ImmutableHashSet.CreateBuilder<Document>();
        foreach (var root in roots)
        {
            var project = OriginatingProject(root);
            if (project is null)
                continue;

            scope.UnionWith(await ForDeclaringProjectAsync(project, cancellationToken));
        }

        return scope.ToImmutable();
    }

    /// <summary>
    /// The candidate together with every member whose name a call site could write instead of the
    /// candidate's own: the base members it overrides, and the interface members it implements —
    /// explicitly or implicitly.
    /// </summary>
    private static IEnumerable<ISymbol> DispatchRoots(ISymbol symbol)
    {
        yield return symbol;

        if (symbol is not IMethodSymbol method)
            yield break;

        for (var overridden = method.OverriddenMethod; overridden is not null;
             overridden = overridden.OverriddenMethod)
            yield return overridden.OriginalDefinition;

        foreach (var explicitly in method.ExplicitInterfaceImplementations)
            yield return explicitly.OriginalDefinition;

        if (method.ContainingType is not { } containingType)
            yield break;

        foreach (var contract in containingType.AllInterfaces)
            foreach (var member in contract.GetMembers())
                if (SymbolEqualityComparer.Default.Equals(
                        containingType.FindImplementationForInterfaceMember(member),
                        method))
                    yield return member.OriginalDefinition;
    }

    /// <summary>
    /// The project that declares the symbol in source, or null where nothing in the solution does —
    /// a symbol read from metadata, which no project relationship can bound.
    /// </summary>
    private ProjectId? OriginatingProject(ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
            if (_solution.GetDocumentId(reference.SyntaxTree) is { } documentId)
                return documentId.ProjectId;

        return null;
    }

    private async ValueTask<IImmutableSet<Document>> ForDeclaringProjectAsync(
        ProjectId declaringProject,
        CancellationToken cancellationToken)
    {
        if (_byDeclaringProject.TryGetValue(declaringProject, out var cached))
            return cached;

        var graph = _solution.GetProjectDependencyGraph();
        var reachable = graph
            .GetProjectsThatTransitivelyDependOnThisProject(declaringProject)
            .Append(declaringProject);

        var documents = ImmutableHashSet.CreateBuilder<Document>();
        foreach (var projectId in reachable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_solution.GetProject(projectId) is not { } project)
                continue;

            documents.UnionWith(project.Documents);
            foreach (var generated in await project.GetSourceGeneratedDocumentsAsync(cancellationToken))
                documents.Add(generated);
        }

        var scope = documents.ToImmutable();
        _byDeclaringProject[declaringProject] = scope;
        return scope;
    }

    private async ValueTask<IImmutableSet<Document>> EverythingAsync(CancellationToken cancellationToken)
    {
        if (_everything is not null)
            return _everything;

        var documents = ImmutableHashSet.CreateBuilder<Document>();
        foreach (var project in _solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.UnionWith(project.Documents);
            foreach (var generated in await project.GetSourceGeneratedDocumentsAsync(cancellationToken))
                documents.Add(generated);
        }

        return _everything = documents.ToImmutable();
    }
}
