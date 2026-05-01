using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class NarrowDependencyService
{
    internal static async Task ExecuteAsync(
        Solution solution,
        string sourceInterfaceSpec,
        string[] memberNames,
        string newInterfaceName,
        bool dryRun)
    {
        // Resolve the source interface symbol
        var interfaceSymbol = await ResolveInterfaceAsync(solution, sourceInterfaceSpec);

        // Validate that all requested member names exist on the source interface
        ValidateMemberNames(interfaceSymbol, memberNames);

        // Collect the selected member symbols
        var selectedMembers = GetSelectedMembers(interfaceSymbol, memberNames);

        // Check if the target interface already exists and matches the desired shape
        var existingNarrow = await FindExistingInterfaceAsync(solution, newInterfaceName, selectedMembers);

        // Determine source file location for the new interface
        var interfaceFilePath = ResolveNewInterfaceFilePath(interfaceSymbol, newInterfaceName);

        Solution updatedSolution = solution;
        bool newFileCreated = false;

        if (existingNarrow is null)
        {
            // Build new interface text
            var targetNamespace = interfaceSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var interfaceText = BuildInterfaceText(targetNamespace, newInterfaceName, selectedMembers);

            // Find owning project
            var owningProject = FindOwningProject(solution, interfaceSymbol)
                ?? throw new InvalidOperationException(
                    $"Cannot find project containing '{interfaceSymbol.Name}'.");

            var interfaceSourceText = SourceText.From(interfaceText, Encoding.UTF8);
            updatedSolution = updatedSolution.AddDocument(
                DocumentId.CreateNewId(owningProject.Id),
                newInterfaceName + ".cs",
                interfaceSourceText,
                filePath: interfaceFilePath);

            newFileCreated = true;
        }

        // Analyze consumers and narrow those that only use the selected subset
        var narrowResult = await NarrowConsumersAsync(
            solution,
            interfaceSymbol,
            selectedMembers,
            newInterfaceName,
            updatedSolution);

        updatedSolution = narrowResult.Solution;

        // Report results
        PrintReport(narrowResult, newInterfaceName, newFileCreated, existingNarrow, dryRun);

        // Apply or preview
        RtLog.Info("Applying narrow-dependency '{Name}' → '{NewInterfaceName}'...", interfaceSymbol.Name, newInterfaceName);
        await SolutionWriter.ApplyChangesAsync(solution, updatedSolution, dryRun);
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private static void ValidateMemberNames(INamedTypeSymbol interfaceSymbol, string[] memberNames)
    {
        var available = interfaceSymbol.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var invalid = memberNames.Where(n => !available.Contains(n)).ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Member(s) [{string.Join(", ", invalid)}] not found on interface '{interfaceSymbol.Name}'. " +
                $"Available members: [{string.Join(", ", available.OrderBy(n => n, StringComparer.Ordinal))}].");
        }
    }

    // -------------------------------------------------------------------------
    // Symbol helpers
    // -------------------------------------------------------------------------

    private static async Task<INamedTypeSymbol> ResolveInterfaceAsync(Solution solution, string interfaceSpec)
    {
        var symbol = LooksFullyQualified(interfaceSpec)
            ? await SymbolLocator.FindSymbolByFqnAsync(solution, interfaceSpec, "type")
            : await SymbolLocator.FindSymbolAsync(solution, interfaceSpec, "type");

        if (symbol is not INamedTypeSymbol namedType)
            throw new InvalidOperationException($"'{interfaceSpec}' is not a named type.");

        if (namedType.TypeKind != TypeKind.Interface)
        {
            throw new InvalidOperationException(
                $"'{namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' is not an interface.");
        }

        return namedType;
    }

    private static List<ISymbol> GetSelectedMembers(INamedTypeSymbol interfaceSymbol, string[] memberNames)
    {
        var nameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
        return interfaceSymbol.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared && nameSet.Contains(m.Name))
            .ToList();
    }

    private static async Task<INamedTypeSymbol?> FindExistingInterfaceAsync(
        Solution solution,
        string newInterfaceName,
        List<ISymbol> selectedMembers)
    {
        // Try to find a type with the new name in the solution
        foreach (var project in solution.Projects)
        {
            var found = await SymbolFinder.FindDeclarationsAsync(
                project, newInterfaceName, ignoreCase: false, SymbolFilter.Type);

            var match = found
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(t => t.TypeKind == TypeKind.Interface && t.Name == newInterfaceName);

            if (match is not null)
                return match;
        }

        return null;
    }

    private static Project? FindOwningProject(Solution solution, INamedTypeSymbol interfaceSymbol)
    {
        var sourceFilePath = interfaceSymbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        if (string.IsNullOrEmpty(sourceFilePath))
            return null;

        return solution.Projects.FirstOrDefault(p =>
            p.Documents.Any(d =>
                string.Equals(d.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ResolveNewInterfaceFilePath(INamedTypeSymbol interfaceSymbol, string newInterfaceName)
    {
        var sourceFilePath = interfaceSymbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        var dir = string.IsNullOrEmpty(sourceFilePath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(sourceFilePath)!;

        return Path.Combine(dir, newInterfaceName + ".cs");
    }

    // -------------------------------------------------------------------------
    // Interface text generation
    // -------------------------------------------------------------------------

    private static string BuildInterfaceText(
        string namespaceName,
        string interfaceName,
        List<ISymbol> members)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLineF($"namespace {namespaceName};");
            sb.AppendLine();
        }

        sb.AppendLineF($"public interface {interfaceName}");
        sb.AppendLine("{");

        foreach (var member in members)
        {
            switch (member)
            {
                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var paramList = BuildParamList(method);
                    sb.AppendLineF($"    {returnType} {method.Name}({paramList});");
                    break;

                case IPropertySymbol property:
                    var propType = property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var accessors = BuildAccessors(property);
                    sb.AppendLineF($"    {propType} {property.Name} {{ {accessors} }}");
                    break;

                case IEventSymbol eventSymbol:
                    var eventType = eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    sb.AppendLineF($"    event {eventType} {eventSymbol.Name};");
                    break;
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildParamList(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p =>
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return $"{typeName} {p.Name}";
        }));
    }

    private static string BuildAccessors(IPropertySymbol property)
    {
        var parts = new List<string>();
        if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            parts.Add("get;");
        if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
            parts.Add("set;");
        return string.Join(" ", parts);
    }

    // -------------------------------------------------------------------------
    // Consumer narrowing
    // -------------------------------------------------------------------------

    private sealed record NarrowResult(
        Solution Solution,
        List<string> NarrowedConsumers,
        List<(string Name, string Reason)> SkippedConsumers);

    private static async Task<NarrowResult> NarrowConsumersAsync(
        Solution originalSolution,
        INamedTypeSymbol interfaceSymbol,
        List<ISymbol> selectedMembers,
        string newInterfaceName,
        Solution workingSolution)
    {
        var selectedMemberNames = selectedMembers
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var narrowed = new List<string>();
        var skipped = new List<(string Name, string Reason)>();

        // Find all classes/structs that have a field or ctor param of the interface type
        var consumers = await FindConsumersAsync(originalSolution, interfaceSymbol);

        foreach (var (consumerSymbol, consumerDoc) in consumers)
        {
            // Determine which interface members this consumer actually uses
            var usedMembers = await GetUsedMembersAsync(
                originalSolution, consumerSymbol, interfaceSymbol);

            // Check if all used members are within the selected subset
            var usedOutside = usedMembers
                .Where(m => !selectedMemberNames.Contains(m))
                .ToList();

            if (usedOutside.Count > 0)
            {
                skipped.Add((
                    consumerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    $"uses members outside the selected subset: {string.Join(", ", usedOutside)}"));
                continue;
            }

            // Narrow the consumer document
            var updatedDoc = await NarrowConsumerDocumentAsync(
                workingSolution,
                consumerDoc,
                consumerSymbol,
                interfaceSymbol.Name,
                newInterfaceName);

            if (updatedDoc is not null)
            {
                workingSolution = updatedDoc;
                narrowed.Add(consumerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            }
        }

        return new NarrowResult(workingSolution, narrowed, skipped);
    }

    private static async Task<List<(INamedTypeSymbol Symbol, Document Document)>> FindConsumersAsync(
        Solution solution,
        INamedTypeSymbol interfaceSymbol)
    {
        var result = new List<(INamedTypeSymbol, Document)>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root is null || semanticModel is null)
                    continue;

                // Find fields typed as the interface
                foreach (var field in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(field) is IFieldSymbol fieldSym
                        && SymbolEqualityComparer.Default.Equals(fieldSym.Type, interfaceSymbol))
                    {
                        var containingType = fieldSym.ContainingType;
                        if (containingType is not null)
                        {
                            result.Add((containingType, document));
                        }
                    }
                }

                // Find ctor parameters typed as the interface
                foreach (var param in root.DescendantNodes().OfType<ParameterSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(param) is IParameterSymbol paramSym
                        && SymbolEqualityComparer.Default.Equals(paramSym.Type, interfaceSymbol)
                        && paramSym.ContainingSymbol is IMethodSymbol
                        { MethodKind: MethodKind.Constructor } ctorSym)
                    {
                        var containingType = ctorSym.ContainingType;
                        if (containingType is not null)
                        {
                            result.Add((containingType, document));
                        }
                    }
                }
            }
        }

        // Deduplicate by type display name
        return result
            .GroupBy(r => r.Item1.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Select(g => g.First())
            .ToList();
    }

    private static async Task<HashSet<string>> GetUsedMembersAsync(
        Solution solution,
        INamedTypeSymbol consumerSymbol,
        INamedTypeSymbol interfaceSymbol)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        var interfaceFqn = interfaceSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        // Each Locations entry covers one partial declaration — scan all of them
        foreach (var location in consumerSymbol.Locations.Where(l => l.IsInSource))
        {
            var sourceTree = location.SourceTree;
            if (sourceTree is null)
                continue;

            // Find the document in the solution that owns this syntax tree
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath is not null
                    && string.Equals(d.FilePath, sourceTree.FilePath, StringComparison.OrdinalIgnoreCase));

            if (document is null)
                continue;

            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root is null || semanticModel is null)
                continue;

            // Find the type declaration node(s) in this tree for this consumer
            var typeDecls = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(t =>
                {
                    var sym = semanticModel.GetDeclaredSymbol(t);
                    return sym is INamedTypeSymbol namedType
                        && namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                           == consumerSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                })
                .ToList();

            foreach (var typeDecl in typeDecls)
            {
                foreach (var simpleName in typeDecl.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(simpleName);
                    var sym = symbolInfo.Symbol
                              ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

                    if (sym is null)
                        continue;

                    // Normalize accessors back to their parent member
                    sym = sym switch
                    {
                        IMethodSymbol
                        {
                            MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
                                        or MethodKind.EventAdd or MethodKind.EventRemove,
                            AssociatedSymbol: { } assoc
                        } => assoc,
                        _ => sym,
                    };

                    // Match by containing-type FQN to avoid cross-compilation reference issues
                    var containingFqn = sym.ContainingType?
                        .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

                    if (containingFqn != interfaceFqn)
                        continue;

                    used.Add(sym.Name);
                }
            }
        }

        return used;
    }

    private static async Task<Solution?> NarrowConsumerDocumentAsync(
        Solution solution,
        Document consumerDocument,
        INamedTypeSymbol consumerSymbol,
        string oldInterfaceName,
        string newInterfaceName)
    {
        var root = await consumerDocument.GetSyntaxRootAsync();
        var semanticModel = await consumerDocument.GetSemanticModelAsync();
        if (root is null || semanticModel is null)
            return null;

        var rewriter = new NarrowTypeRewriter(semanticModel, oldInterfaceName, newInterfaceName);
        var newRoot = rewriter.Visit(root);

        if (newRoot is null || newRoot.IsEquivalentTo(root))
            return null;

        return solution.WithDocumentSyntaxRoot(consumerDocument.Id, newRoot);
    }

    // -------------------------------------------------------------------------
    // Syntax rewriter — replaces type annotations of the old interface name
    // -------------------------------------------------------------------------

    private sealed class NarrowTypeRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly string _oldName;
        private readonly string _newName;

        internal NarrowTypeRewriter(SemanticModel semanticModel, string oldName, string newName)
        {
            _semanticModel = semanticModel;
            _oldName = oldName;
            _newName = newName;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.Text != _oldName)
                return base.VisitIdentifierName(node);

            // Only rewrite if the semantic model confirms this identifier refers to the interface type
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var sym = symbolInfo.Symbol ?? (symbolInfo.CandidateSymbols.Length == 1 ? symbolInfo.CandidateSymbols[0] : null);

            if (sym is INamedTypeSymbol namedType
                && namedType.TypeKind == TypeKind.Interface
                && namedType.Name == _oldName)
            {
                return node.WithIdentifier(
                    SyntaxFactory.Identifier(node.Identifier.LeadingTrivia, _newName, node.Identifier.TrailingTrivia));
            }

            return base.VisitIdentifierName(node);
        }
    }

    // -------------------------------------------------------------------------
    // Reporting
    // -------------------------------------------------------------------------

    private static void PrintReport(
        NarrowResult result,
        string newInterfaceName,
        bool newFileCreated,
        INamedTypeSymbol? reusedInterface,
        bool dryRun)
    {
        var modeLabel = dryRun ? "[dry-run] " : string.Empty;

        if (reusedInterface is not null)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"{modeLabel}Reusing existing interface '{newInterfaceName}' — shape matches selected members.");
        }
        else
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"{modeLabel}New interface '{newInterfaceName}' will be created.");
        }

        if (result.NarrowedConsumers.Count > 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"\n{modeLabel}Narrowed {result.NarrowedConsumers.Count} consumer(s):");
            foreach (var consumer in result.NarrowedConsumers)
                RtConsole.WriteLine(RtOutputContext.Result, $"  - {consumer}");

            RtConsole.WriteLine(RtOutputContext.Result,
                $"\nNOTE: DI registration(s) for '{newInterfaceName}' must be updated manually. " +
                "Register the concrete implementation against the new narrower interface in your composition root.");
        }

        if (result.SkippedConsumers.Count > 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"\n{modeLabel}Skipped {result.SkippedConsumers.Count} consumer(s):");
            foreach (var (name, reason) in result.SkippedConsumers)
                RtConsole.WriteLine(RtOutputContext.Result, $"  - {name}: skipped ({reason})");
        }
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private static bool LooksFullyQualified(string symbolSpec)
    {
        return symbolSpec.StartsWith("global::", StringComparison.Ordinal)
               || symbolSpec.Contains('.', StringComparison.Ordinal);
    }
}
