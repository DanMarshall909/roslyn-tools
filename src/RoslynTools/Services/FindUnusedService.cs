using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        FindUnusedProfiler? profiler = null,
        CancellationToken cancellationToken = default)
    {
        profiler ??= new FindUnusedProfiler();
        var results = new List<UnusedSymbol>();
        var rtIgnore = RtIgnore.ForSolution(solution);
        var reflectionScanStarted = FindUnusedProfiler.Timestamp();
        var reflectivelyScannedProjects =
            await FindReflectivelyScannedProjectsAsync(solution, cancellationToken);
        profiler.RecordReflectionScan(reflectionScanStarted);

        // One candidate is one reference search, however many declarations name it. A partial type
        // has a declaration per file and a partial method has two, so searching where a declaration
        // is met rather than where a candidate is settled asks Roslyn the same question twice and
        // reports the answer twice.
        var searched = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var searchScope = new ReferenceSearchScope(solution);

        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (rtIgnore.IsIgnored(document.FilePath))
                    continue;

                profiler.CountDocument();

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (root == null || semanticModel == null)
                {
                    profiler.CountSkippedDocument();
                    continue;
                }

                // A document the parser could not understand is evidence of nothing. What it appears
                // to declare are the parser's guesses at fragments it could not fit to any rule, and
                // a guessed symbol has no references by construction — indistinguishable, from here
                // on, from a real symbol nobody uses. Absence of references only means something
                // where the source was understood, so this document makes no absence claim at all.
                if (CouldNotBeParsed(root))
                {
                    profiler.CountSkippedDocument();
                    continue;
                }

                if (scope is "all" or "types")
                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) is not INamedTypeSymbol symbol)
                            continue;

                        if (ShouldExclude(symbol)) continue;

                        profiler.CountTypeCandidate();
                        if (!searched.Add(Canonicalize(symbol)))
                            continue;

                        profiler.CountUniqueCandidate();

                        var typeScope = await searchScope.ForAsync(symbol, cancellationToken);

                        if (await HasAnyReferenceAsync(
                                symbol, solution, typeScope, profiler, cancellationToken))
                            continue;

                        if (await HasReferencedExtensionMethodAsync(
                                symbol, solution, typeScope, profiler, cancellationToken))
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
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!DeclaresAMethod(methodDecl)) continue;

                        var symbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
                        if (symbol == null) continue;
                        if (ShouldExclude(symbol)) continue;

                        profiler.CountMethodCandidate();
                        if (!searched.Add(Canonicalize(symbol)))
                            continue;

                        profiler.CountUniqueCandidate();

                        var methodScope = await searchScope.ForAsync(symbol, cancellationToken);

                        if (!await HasAnyReferenceAsync(
                                symbol, solution, methodScope, profiler, cancellationToken))
                        {
                            var line = methodDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            results.Add(new UnusedSymbol(symbol.Name, "method", document.FilePath, line));
                        }
                    }
            }

        return results;
    }

    /// <summary>
    /// The one symbol a set of declarations agree on. A partial type is declared once per file and a
    /// partial method twice — an implementation and a definition, which are distinct symbols — so
    /// without a canonical form the same question reaches Roslyn several times and the same answer
    /// is reported several times. The partial method's *definition* is the canonical half because it
    /// is the half that always exists.
    /// </summary>
    private static ISymbol Canonicalize(ISymbol symbol) =>
        symbol switch
        {
            IMethodSymbol { PartialDefinitionPart: { } definition } => definition.OriginalDefinition,
            _ => symbol.OriginalDefinition
        };

    /// <summary>
    /// Whether anything in the solution references the symbol. find-unused never needs the reference
    /// list, only whether it is empty, and keeping that distinction in one place is what makes it
    /// possible to stop the search at the first hit later without revisiting every call site.
    /// </summary>
    private static async Task<bool> HasAnyReferenceAsync(
        ISymbol symbol,
        Solution solution,
        IImmutableSet<Document> scope,
        FindUnusedProfiler profiler,
        CancellationToken cancellationToken)
    {
        var startedAt = FindUnusedProfiler.Timestamp();
        var references =
            await SymbolFinder.FindReferencesAsync(symbol, solution, scope, cancellationToken);
        var found = references.Any(reference => reference.Locations.Any());
        profiler.RecordReferenceSearch(startedAt, scope.Count);
        return found;
    }

    /// <summary>
    /// True when the document carries a syntax error. Roslyn recovers from syntax it cannot read by
    /// guessing at the nearest declaration it does know, and the guess is a first-class node with a
    /// first-class symbol — a C# <c>union</c> read by an older parser becomes a method whose return
    /// type is the word <c>union</c>. Nothing downstream can tell that apart from real source, which
    /// is why the refusal has to happen here, at the one place that can still see the diagnostic.
    /// </summary>
    private static bool CouldNotBeParsed(SyntaxNode root) =>
        root.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// True when the node really does declare a method. A method without a body has to say why it
    /// has none — <c>abstract</c>, <c>extern</c> or <c>partial</c>, or membership of an interface,
    /// which <see cref="ShouldExclude" /> already removes. Anything else the parser handed back as a
    /// method declaration is a fragment it could not otherwise place, and reporting one as an unused
    /// method asserts a parse that did not happen. This is deliberately independent of
    /// <see cref="CouldNotBeParsed" />: a bodiless declaration is a <em>semantic</em> error, so the
    /// tree carries no syntax diagnostic and the document-level refusal never sees it.
    /// </summary>
    private static bool DeclaresAMethod(MethodDeclarationSyntax declaration) =>
        declaration.Body is not null
        || declaration.ExpressionBody is not null
        || declaration.Modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.AbstractKeyword)
            || modifier.IsKind(SyntaxKind.ExternKeyword)
            || modifier.IsKind(SyntaxKind.PartialKeyword));

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
            var scans = false;
            var reflects = false;

            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // An assembly scan outranks bare reflection, so once one is found this project's
                // answer is settled and the rest of its documents cannot change it. The lower-
                // priority classification still has to see every document, because stopping at the
                // first hit would make the reported reason depend on document order.
                if (scans)
                    break;

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null)
                    continue;

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    // The invoked name, read from the syntax rather than rendered to a string.
                    // ToString() on every invocation expression in the solution allocated a string
                    // per call site only to run substring searches over it, and a substring search
                    // matched a method whose name merely contained a selector.
                    var invoked = InvokedName(invocation);
                    if (invoked is null)
                        continue;

                    scans |= AssemblyScanSelectors.Contains(invoked, StringComparer.Ordinal);
                    reflects |= ReflectiveDiscoveryApis.Contains(invoked, StringComparer.Ordinal);

                    if (scans)
                        break;
                }
            }

            if (scans)
                projects[project.Id] = UnusedReason.AssemblyScan;
            else if (reflects)
                projects[project.Id] = UnusedReason.ReflectiveDiscovery;
        }

        return projects;
    }

    /// <summary>
    /// The simple name an invocation calls — <c>Bar</c> for both <c>Bar()</c> and <c>Foo.Bar()</c> —
    /// or null where the callee is not a name at all, such as an invoked delegate expression.
    /// </summary>
    private static string? InvokedName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: { } name } => name.Identifier.ValueText,
            MemberBindingExpressionSyntax { Name: { } name } => name.Identifier.ValueText,
            _ => null
        };

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
        IImmutableSet<Document> scope,
        FindUnusedProfiler profiler,
        CancellationToken cancellationToken)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsExtensionMethod))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startedAt = FindUnusedProfiler.Timestamp();
            var references =
                await SymbolFinder.FindReferencesAsync(method, solution, scope, cancellationToken);
            var found = references.SelectMany(reference => reference.Locations).Any();
            profiler.RecordReferenceSearch(startedAt, scope.Count, forExtensionMethod: true);
            if (found)
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

            if (IsXunitFixture(type))
                return true;
        }

        if (symbol.ContainingType?.TypeKind == TypeKind.Interface)
            return true;

        if (symbol.Name == EntryPointMethodName)
            return true;

        if (symbol is IMethodSymbol m && HasAttribute(m, "FactAttribute", "TheoryAttribute"))
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
