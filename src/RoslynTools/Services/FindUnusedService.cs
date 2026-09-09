using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// How far a finding can be trusted. find-unused emits a safe-delete suggestion only at
/// <see cref="High"/>, so anything the analysis cannot see through must be reported at
/// <see cref="Low"/> rather than excluded outright — the finding stays visible, but it
/// cannot invite a deletion.
/// </summary>
internal static class UnusedConfidence
{
    internal const string High = "high";
    internal const string Low = "low";
}

/// <summary>
/// Standard <see cref="UnusedSymbol.Reason"/> values. A reason states what the analysis
/// could or could not see, so the caller can judge a low-confidence finding for itself.
/// </summary>
internal static class UnusedReason
{
    internal const string NoSourceReferences = "No source references were found.";

    internal const string AssemblyScan =
        "No source references were found, but this project registers types by assembly scan, "
        + "so a live registration would leave no reference.";

    internal const string ReflectiveDiscovery =
        "No source references were found, but this project discovers types reflectively, "
        + "so a live use would leave no reference.";
}

/// <summary>
/// Detects unused symbols in the codebase.
/// </summary>
internal record UnusedSymbol(
    string Name,
    string Kind,
    string? FilePath,
    int Line,
    string Confidence = UnusedConfidence.High,
    string Reason = UnusedReason.NoSourceReferences);

/// <summary>
/// Searches for unused services in a solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class FindUnusedService : IRtService
{
    /// <summary>
    /// Analyzes a solution to find unused symbols.
    /// </summary>
    internal async Task<List<UnusedSymbol>> AnalyzeAsync(
        Solution solution,
        string scope = "all",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<UnusedSymbol>();
        var rtIgnore = RtIgnore.ForSolution(solution);
        var reflectivelyScannedProjects = await FindReflectivelyScannedProjectsAsync(solution, cancellationToken);
        var externalInterfaceImplementations = (await FindExternalInterfaceImplementationsAsync(solution, cancellationToken))
            .ToHashSet(SymbolEqualityComparer.Default);

        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rtIgnore.IsIgnored(document.FilePath))
                    continue;

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null)
                    continue;

                // Roslyn recovery can shape unsupported syntax as a method or another familiar
                // declaration. Never turn that recovery tree into an absence claim: the complete
                // workspace assessment exposes the parse failure, while this document contributes
                // no candidates with a fabricated kind.
                if (root.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null)
                    continue;

                if (scope is "all" or "types")
                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) is not INamedTypeSymbol symbol)
                            continue;

                        if (ShouldExclude(symbol)) continue;

                        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken: cancellationToken);
                        var count = refs.Sum(r => r.Locations.Count());
                        if (count != 0)
                            continue;

                        if (await HasReferencedExtensionMethodAsync(symbol, solution, cancellationToken))
                            continue;

                        // A reflective container can construct this type without any source
                        // reference, so zero references is no longer proof of death. Report the
                        // finding at low confidence with the reason: safe-delete is withheld, and
                        // the caller decides whether to investigate.
                        var line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        results.Add(
                            IsInstantiable(symbol)
                            && reflectivelyScannedProjects.TryGetValue(project.Id, out var reflectiveReason)
                                ? new UnusedSymbol(symbol.Name, "type", document.FilePath, line,
                                    UnusedConfidence.Low, reflectiveReason)
                                : new UnusedSymbol(symbol.Name, "type", document.FilePath, line));
                    }

                if (scope is "all" or "methods")
                    foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
                        if (symbol == null) continue;
                        if (externalInterfaceImplementations.Contains(symbol.OriginalDefinition) || ShouldExclude(symbol))
                            continue;

                        var refs = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken: cancellationToken);
                        var count = refs.Sum(r => r.Locations.Count());
                        if (count == 0)
                        {
                            var line = methodDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            results.Add(new UnusedSymbol(symbol.Name, "method", document.FilePath, line));
                        }
                    }
            }

        return results;
    }

    /// <summary>
    /// Scrutor-style selector calls. Their presence means registration is driven by an
    /// assembly walk, so any instantiable type in that assembly may be a live registration.
    /// </summary>
    private static readonly string[] AssemblyScanSelectors =
        ["FromAssemblyOf", "FromAssemblies", "AddClasses"];

    /// <summary>
    /// Reflection APIs that construct or discover types without naming them. A hand-rolled
    /// registry (rt's own IRtCommand discovery is one) leaves no source reference either,
    /// so the same uncertainty applies as for a Scrutor scan.
    /// </summary>
    private static readonly string[] ReflectiveDiscoveryApis =
        ["GetTypes", "GetExportedTypes", "DefinedTypes", "CreateInstance", "IsAssignableFrom"];

    /// <summary>
    /// Attribute class names marking a type whose instances a framework produces — a serializer,
    /// MVC, or a container — so source references understate real use. Names carry the compiler's
    /// "Attribute" suffix: `[Serializable]` binds to SerializableAttribute, so the bare spelling
    /// can never match and is not listed.
    /// </summary>
    private static readonly string[] FrameworkOwnedTypeAttributes =
    [
        "SerializableAttribute",
        "DataContractAttribute",
        "JsonObjectAttribute",
        "ApiControllerAttribute",
        "UsedImplicitlyAttribute"
    ];

    /// <summary>
    /// Maps each project that discovers types reflectively to the reason describing what the
    /// analysis cannot see through. Findings in those projects are reported at low confidence
    /// rather than suppressed: the caller — not rt — decides what an uncertain finding means,
    /// and hiding it would only trade a false positive for a false negative.
    /// </summary>
    private static async Task<Dictionary<ProjectId, string>> FindReflectivelyScannedProjectsAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        var projects = new Dictionary<ProjectId, string>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scans = false;
            var reflects = false;

            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null)
                    continue;

                var invoked = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Select(i => i.Expression.ToString())
                    .ToList();

                scans |= invoked.Any(e => AssemblyScanSelectors.Any(s => e.Contains(s, StringComparison.Ordinal)));
                reflects |= invoked.Any(e => ReflectiveDiscoveryApis.Any(s => e.Contains(s, StringComparison.Ordinal)));
            }

            // Every document is examined before choosing, and an assembly scan outranks bare
            // reflection: stopping at the first hit would make the reported reason depend on
            // document order in a project that does both.
            if (scans)
                projects[project.Id] = UnusedReason.AssemblyScan;
            else if (reflects)
                projects[project.Id] = UnusedReason.ReflectiveDiscovery;
        }

        return projects;
    }

    /// <summary>
    /// True when the type declares an extension method that something calls. `value.IsBlank()`
    /// binds to the method and never writes the declaring class's name, so an extension
    /// container shows zero references however heavily it is used — reporting it as dead
    /// offered a safe-delete that would break the build (found dogfooding rt's own
    /// StringExtensions, which has live callers). Only extension methods need this: a normal
    /// static call writes `Foo.Bar()`, which does reference the type.
    /// </summary>
    private static async Task<bool> HasReferencedExtensionMethodAsync(
        INamedTypeSymbol type,
        Solution solution,
        CancellationToken cancellationToken)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsExtensionMethod))
        {
            var refs = await SymbolFinder.FindReferencesAsync(method, solution, cancellationToken: cancellationToken);
            if (refs.SelectMany(r => r.Locations).Any())
                return true;
        }

        return false;
    }

    private static bool IsInstantiable(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsStatic;

    /// <summary>
    /// The name of the CLR entry-point method. The runtime calls it, so neither it nor the
    /// type declaring it is ever referenced from source.
    /// </summary>
    private const string EntryPointMethodName = "Main";

    /// <summary>
    /// True when the type declares the entry point. Excluding only the <c>Main</c> method — as
    /// this service originally did — still left its containing type reported as dead, the same
    /// method-excluded-but-type-not gap that reported xUnit fixtures. Deleting it breaks the build.
    /// </summary>
    private static bool DeclaresEntryPoint(INamedTypeSymbol type) =>
        type.GetMembers(EntryPointMethodName).OfType<IMethodSymbol>().Any(m => m.IsStatic);

    private static bool IsModuleInitializer(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
            == "System.Runtime.CompilerServices.ModuleInitializerAttribute");

    /// <summary>
    /// A derived type can introduce an external interface implemented by an inherited
    /// source method whose declaring type has no interfaces. Collect those mappings once
    /// instead of searching the entire solution for each unused-method candidate.
    /// </summary>
    private static async Task<List<IMethodSymbol>> FindExternalInterfaceImplementationsAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        var implementations = new List<IMethodSymbol>();
        var inspectedMappings = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            foreach (var type in EnumerateSourceTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var contract in type.AllInterfaces.Where(contract => contract.Locations.Any(location => location.IsInMetadata)))
                    foreach (var member in contract.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation
                            || !inspectedMappings.Add(implementation.OriginalDefinition))
                            continue;

                        var source = await SymbolFinder.FindSourceDefinitionAsync(
                            implementation.OriginalDefinition, solution, cancellationToken) as IMethodSymbol;
                        if (source is not null)
                            implementations.Add(source.OriginalDefinition);
                    }
            }
        }

        return implementations;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSourceTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamedTypeSymbol type)
                yield return type;
            if (member is INamespaceOrTypeSymbol nestedContainer)
                foreach (var nestedType in EnumerateSourceTypes(nestedContainer))
                    yield return nestedType;
        }
    }

    /// <summary>
    /// An external consumer can invoke its own virtual or interface slot without naming
    /// the implementation in source. Source-local slots remain reference-analyzed.
    /// </summary>
    private static bool HasExternalDispatchContract(IMethodSymbol method)
    {
        for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
            if (overridden.Locations.Any(location => location.IsInMetadata))
                return true;

        foreach (var contract in method.ContainingType.AllInterfaces)
        {
            if (!contract.Locations.Any(location => location.IsInMetadata))
                continue;

            foreach (var member in contract.GetMembers().OfType<IMethodSymbol>())
            {
                var implementation = method.ContainingType.FindImplementationForInterfaceMember(member);
                if (implementation is not IMethodSymbol implementedMethod)
                    continue;

                for (var candidate = method; candidate is not null; candidate = candidate.OverriddenMethod)
                    if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, implementedMethod.OriginalDefinition))
                        return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The method name and interface App.Discover() invokes reflectively (via
    /// <c>Type.GetMethod("Build", ...)</c> + <c>MethodInfo.Invoke</c>) on every command. Like
    /// <see cref="EntryPointMethodName"/>, this is a known, narrow reflective consumer inside rt
    /// itself — not a generic assembly scan — so the method is excluded outright rather than
    /// downgraded to low confidence.
    /// </summary>
    private const string CommandFactoryMethodName = "Build";

    private const string CommandInterfaceName = "IRtCommand";

    private static bool IsCommandFactoryMethod(IMethodSymbol method) =>
        method.Name == CommandFactoryMethodName
        && method.DeclaredAccessibility == Accessibility.Public
        && method.ContainingType is { } containingType
        && ImplementsInterface(containingType, CommandInterfaceName);

    private bool ShouldExclude(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol type)
        {
            if (HasAttribute(type, FrameworkOwnedTypeAttributes))
                return true;

            if (InheritsFrom(type, "ControllerBase"))
                return true;

            if (DeclaresEntryPoint(type))
                return true;

            if (type.GetMembers().OfType<IMethodSymbol>().Any(IsModuleInitializer))
                return true;

            if (IsXunitFixture(type))
                return true;
        }

        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return true;

        if (symbol.Name == EntryPointMethodName)
            return true;

        if (symbol is IMethodSymbol m && HasAttribute(m, "FactAttribute", "TheoryAttribute"))
            return true;

        if (symbol is IMethodSymbol method && (IsModuleInitializer(method) || HasExternalDispatchContract(method)))
            return true;

        if (symbol is IMethodSymbol factoryCandidate && IsCommandFactoryMethod(factoryCandidate))
            return true;

        return false;
    }

    private static bool IsXunitFixture(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (current.GetMembers().OfType<IMethodSymbol>()
                .Any(m => HasAttribute(m, "FactAttribute", "TheoryAttribute")))
                return true;

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string baseTypeName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseTypeName)
                return true;

        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceName) =>
        type.AllInterfaces.Any(i => i.Name == interfaceName);

    /// <summary>
    /// Matches by attribute class name. Names are the compiler's, so they always carry the
    /// "Attribute" suffix however the usage was written: `[Fact]` binds to FactAttribute.
    /// </summary>
    private static bool HasAttribute(ISymbol symbol, params string[] names)
    {
        var expected = names.ToHashSet(StringComparer.Ordinal);
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass is { } attribute && expected.Contains(attribute.Name));
    }
}
