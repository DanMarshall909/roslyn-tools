using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Models;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class AnalyzeInterfaceService : IRtService
{
    private readonly FindCallersService _findCallers;

    public AnalyzeInterfaceService()
        : this(new FindCallersService())
    {
    }

    public AnalyzeInterfaceService(FindCallersService findCallers)
    {
        _findCallers = findCallers;
    }

    internal async Task<AnalyzeInterfaceReport> AnalyzeAsync(
        Solution solution,
        string interfaceSymbolSpec,
        AnalyzeInterfaceOptions options)
    {
        var interfaceSymbol = await ResolveInterfaceAsync(solution, interfaceSymbolSpec);
        var trackedMembers = GetTrackedMembers(interfaceSymbol);
        var trackedMemberSet = trackedMembers.ToHashSet(SymbolEqualityComparer.Default);
        var consumers = new Dictionary<string, ConsumerAccumulator>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            if (!options.IncludeTests && IsTestProject(project.Name))
                continue;

            var scopeMatchesProject = options.Scope is not null
                && string.Equals(project.Name, options.Scope, StringComparison.OrdinalIgnoreCase);

            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel is null)
                    continue;

                AnalyzeDeclaredInterfaceCarriers(
                    root,
                    semanticModel,
                    interfaceSymbol,
                    project.Name,
                    document,
                    options.Scope,
                    scopeMatchesProject,
                    consumers);

                AnalyzeMemberReferences(
                    root,
                    semanticModel,
                    trackedMemberSet,
                    project.Name,
                    document,
                    options.Scope,
                    scopeMatchesProject,
                    consumers);
            }
        }

        var rawReports = consumers.Values
            .OrderBy(consumer => consumer.ConsumerDisplayName, StringComparer.Ordinal)
            .Select(consumer => consumer.ToReport())
            .ToList();

        var consumerReports = options.Suggest
            ? AttachSuggestions(rawReports, trackedMembers.Count)
            : rawReports;

        return new AnalyzeInterfaceReport(
            InterfaceName: interfaceSymbol.Name,
            InterfaceDisplayName: interfaceSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            InterfaceMemberCount: trackedMembers.Count,
            Scope: options.Scope,
            IncludeTests: options.IncludeTests,
            Consumers: consumerReports);
    }

    private void AnalyzeDeclaredInterfaceCarriers(
        SyntaxNode root,
        SemanticModel semanticModel,
        INamedTypeSymbol interfaceSymbol,
        string projectName,
        Document document,
        string? scope,
        bool scopeMatchesProject,
        Dictionary<string, ConsumerAccumulator> consumers)
    {
        foreach (var field in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var declared = semanticModel.GetDeclaredSymbol(field);
            switch (declared)
            {
                case IFieldSymbol fieldSymbol when SymbolEqualityComparer.Default.Equals(fieldSymbol.Type, interfaceSymbol):
                    AddTypeOnlyConsumer(field.SpanStart);
                    break;
                case ILocalSymbol localSymbol when SymbolEqualityComparer.Default.Equals(localSymbol.Type, interfaceSymbol):
                    AddTypeOnlyConsumer(field.SpanStart);
                    break;
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(property) is not IPropertySymbol propertySymbol
                || !SymbolEqualityComparer.Default.Equals(propertySymbol.Type, interfaceSymbol))
            {
                continue;
            }

            var consumer = GetContainingConsumerType(semanticModel, property.SpanStart);
            if (consumer is null || !MatchesScope(consumer, projectName, scope, scopeMatchesProject))
                continue;

            GetOrAddConsumer(consumers, consumer, projectName, document).MarkTypeReference();
        }

        foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(parameter) is not IParameterSymbol parameterSymbol
                || !SymbolEqualityComparer.Default.Equals(parameterSymbol.Type, interfaceSymbol))
            {
                continue;
            }

            var consumer = GetContainingConsumerType(semanticModel, parameter.SpanStart);
            if (consumer is null || !MatchesScope(consumer, projectName, scope, scopeMatchesProject))
                continue;

            GetOrAddConsumer(consumers, consumer, projectName, document).MarkTypeReference();
        }

        void AddTypeOnlyConsumer(int position)
        {
            var consumer = GetContainingConsumerType(semanticModel, position);
            if (consumer is null || !MatchesScope(consumer, projectName, scope, scopeMatchesProject))
                return;

            GetOrAddConsumer(consumers, consumer, projectName, document).MarkTypeReference();
        }
    }

    private void AnalyzeMemberReferences(
        SyntaxNode root,
        SemanticModel semanticModel,
        HashSet<ISymbol> trackedMembers,
        string projectName,
        Document document,
        string? scope,
        bool scopeMatchesProject,
        Dictionary<string, ConsumerAccumulator> consumers)
    {
        foreach (var simpleName in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (IsWithinNameof(simpleName))
                continue;

            var trackedMember = NormalizeTrackedMember(GetBestSymbol(semanticModel.GetSymbolInfo(simpleName)));
            if (trackedMember is null || !trackedMembers.Contains(trackedMember))
                continue;

            var consumer = GetContainingConsumerType(semanticModel, simpleName.SpanStart);
            if (consumer is null || !MatchesScope(consumer, projectName, scope, scopeMatchesProject))
                continue;

            GetOrAddConsumer(consumers, consumer, projectName, document).AddMember(trackedMember);
        }
    }

    private async Task<INamedTypeSymbol> ResolveInterfaceAsync(Solution solution, string interfaceSymbolSpec)
    {
        var symbol = LooksFullyQualified(interfaceSymbolSpec)
            ? await SymbolLocator.FindSymbolByFqnAsync(solution, interfaceSymbolSpec, "type")
            : await SymbolLocator.FindSymbolAsync(solution, interfaceSymbolSpec, "type");

        if (symbol is not INamedTypeSymbol namedType)
            throw new InvalidOperationException($"'{interfaceSymbolSpec}' is not a named type.");

        if (namedType.TypeKind != TypeKind.Interface)
        {
            throw new InvalidOperationException(
                $"'{namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' is not an interface; resolved type kind was {namedType.TypeKind}.");
        }

        return namedType;
    }

    private List<ISymbol> GetTrackedMembers(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.GetMembers()
            .Where(member => !member.IsImplicitlyDeclared)
            .Select(NormalizeTrackedMember)
            .Where(member => member is not null)
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<ISymbol>()
            .OrderBy(member => member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
            .ToList();
    }

    private ISymbol? NormalizeTrackedMember(ISymbol? symbol)
    {
        return symbol switch
        {
            IMethodSymbol
            {
                MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove,
                AssociatedSymbol: { } associated
            } => NormalizeTrackedMember(associated),
            IMethodSymbol method when method.MethodKind == MethodKind.Ordinary => method.OriginalDefinition,
            IPropertySymbol property => property.OriginalDefinition,
            IEventSymbol eventSymbol => eventSymbol.OriginalDefinition,
            _ => null,
        };
    }

    private ISymbol? GetBestSymbol(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol is not null)
            return symbolInfo.Symbol;

        return symbolInfo.CandidateSymbols.Length == 1
            ? symbolInfo.CandidateSymbols[0]
            : null;
    }

    private INamedTypeSymbol? GetContainingConsumerType(SemanticModel semanticModel, int position)
    {
        for (var current = semanticModel.GetEnclosingSymbol(position); current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol namedType
                && namedType.TypeKind is TypeKind.Class or TypeKind.Struct)
            {
                return namedType;
            }
        }

        return null;
    }

    private bool MatchesScope(
        INamedTypeSymbol consumer,
        string projectName,
        string? scope,
        bool scopeMatchesProject)
    {
        if (scope is null)
            return true;

        if (scopeMatchesProject)
            return true;

        if (string.Equals(projectName, scope, StringComparison.OrdinalIgnoreCase))
            return true;

        var namespaceName = consumer.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceName.StartsWith(scope, StringComparison.Ordinal);
    }

    private bool IsWithinNameof(SyntaxNode node)
    {
        return node.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(_findCallers.IsNameofExpression);
    }

    private ConsumerAccumulator GetOrAddConsumer(
        Dictionary<string, ConsumerAccumulator> consumers,
        INamedTypeSymbol consumer,
        string projectName,
        Document document)
    {
        var displayName = consumer.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!consumers.TryGetValue(displayName, out var accumulator))
        {
            accumulator = new ConsumerAccumulator(
                consumer.Name,
                displayName,
                consumer.TypeKind.ToString(),
                consumer.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                projectName);
            consumers.Add(displayName, accumulator);
        }

        accumulator.AddDocument(document.FilePath ?? document.Name);
        return accumulator;
    }

    private bool IsTestProject(string projectName)
    {
        return projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
               || projectName.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
               || projectName.Contains("Tests", StringComparison.OrdinalIgnoreCase);
    }

    private bool LooksFullyQualified(string symbolSpec)
    {
        return symbolSpec.StartsWith("global::", StringComparison.Ordinal)
               || symbolSpec.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>
    /// Derives advisory minimal-interface suggestions for each consumer from its used-member set.
    /// The suggestion is purely advisory and does not imply a named type already exists.
    /// </summary>
    private List<AnalyzeInterfaceConsumerReport> AttachSuggestions(
        List<AnalyzeInterfaceConsumerReport> reports,
        int totalInterfaceMemberCount)
    {
        // Build a canonical key for each consumer's used-member set so that consumers
        // sharing the same subset get the same SharedGroupId.
        var groupIds = new Dictionary<string, string>(StringComparer.Ordinal);

        return reports.Select(report =>
        {
            var usedCount = report.UsedMembers.Count;
            var hasNarrowingBenefit = usedCount > 0 && usedCount < totalInterfaceMemberCount;

            // Canonical key: sorted display names joined — same set => same key
            var memberKey = string.Join("|", report.UsedMembers.Select(m => m.DisplayName));

            if (!groupIds.TryGetValue(memberKey, out var groupId))
            {
                // Stable, deterministic identifier: short hash of the key
                groupId = ComputeGroupId(memberKey);
                groupIds[memberKey] = groupId;
            }

            var suggestion = new AnalyzeInterfaceSuggestion(
                HasNarrowingBenefit: hasNarrowingBenefit,
                SharedGroupId: groupId,
                SuggestedMembers: hasNarrowingBenefit ? report.UsedMembers : []);

            return report with { Suggestion = suggestion };
        }).ToList();
    }

    private string ComputeGroupId(string key)
    {
        // Stable 8-char hex group identifier derived from the member-set key.
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private sealed class ConsumerAccumulator
    {
        private readonly SortedSet<string> _documents = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, AnalyzeInterfaceMemberReport> _usedMembers = new(StringComparer.Ordinal);
        private bool _hasTypeReference;

        private readonly string _consumerName;
        private readonly string _consumerKind;
        private readonly string _consumerNamespace;
        private readonly string _projectName;

        public ConsumerAccumulator(
            string consumerName,
            string consumerDisplayName,
            string consumerKind,
            string consumerNamespace,
            string projectName)
        {
            _consumerName = consumerName;
            ConsumerDisplayName = consumerDisplayName;
            _consumerKind = consumerKind;
            _consumerNamespace = consumerNamespace;
            _projectName = projectName;
        }

        public string ConsumerDisplayName { get; }

        public void MarkTypeReference()
        {
            _hasTypeReference = true;
        }

        public void AddDocument(string filePath)
        {
            _documents.Add(Path.GetFileName(filePath));
        }

        public void AddMember(ISymbol member)
        {
            MarkTypeReference();

            var displayName = FormatMemberDisplay(member);
            _usedMembers.TryAdd(
                displayName,
                new AnalyzeInterfaceMemberReport(
                    Name: member.Name,
                    Kind: member.Kind.ToString(),
                    DisplayName: displayName));
        }

        public AnalyzeInterfaceConsumerReport ToReport()
        {
            return new AnalyzeInterfaceConsumerReport(
                ConsumerName: _consumerName,
                ConsumerDisplayName: ConsumerDisplayName,
                ConsumerKind: _consumerKind,
                Namespace: _consumerNamespace,
                ProjectName: _projectName,
                UsageKind: _usedMembers.Count == 0 && _hasTypeReference ? "type-only" : "members",
                Documents: [.. _documents],
                UsedMembers: [.. _usedMembers.Values]);
        }

        private static string FormatMemberDisplay(ISymbol member)
        {
            return member switch
            {
                IMethodSymbol method => $"{FormatType(method.ReturnType)} {FormatContainingType(method)}.{method.Name}({string.Join(", ", method.Parameters.Select(FormatParameter))})",
                IPropertySymbol property => $"{FormatType(property.Type)} {FormatContainingType(property)}.{property.Name} {{ {FormatPropertyAccessors(property)} }}",
                IEventSymbol eventSymbol => $"event {FormatType(eventSymbol.Type)} {FormatContainingType(eventSymbol)}.{eventSymbol.Name}",
                _ => member.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            };
        }

        private static string FormatContainingType(ISymbol member)
        {
            return member.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                   ?? member.ContainingNamespace?.ToDisplayString()
                   ?? string.Empty;
        }

        private static string FormatType(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        private static string FormatParameter(IParameterSymbol parameter)
        {
            var modifier = parameter.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => string.Empty,
            };

            return $"{modifier}{FormatType(parameter.Type)} {parameter.Name}";
        }

        private static string FormatPropertyAccessors(IPropertySymbol property)
        {
            var accessors = new List<string>();
            if (property.GetMethod is not null)
                accessors.Add("get;");
            if (property.SetMethod is not null)
                accessors.Add("set;");

            return string.Join(" ", accessors);
        }
    }
}
