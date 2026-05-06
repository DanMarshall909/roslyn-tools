using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class AddDependencyService : IRtService
{
    private readonly ILogger<AddDependencyService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public AddDependencyService()
        : this(new RtConsoleLogger<AddDependencyService>(), new SolutionChangeWriter())
    {
    }

    public AddDependencyService(ILogger<AddDependencyService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }
    private readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    internal sealed record ResolvedTarget(
        Document Document,
        SyntaxNode Root,
        SemanticModel SemanticModel,
        ClassDeclarationSyntax Declaration,
        INamedTypeSymbol Symbol);

    internal async Task RunAsync(
        Solution solution,
        string typeName,
        string depType,
        string? fieldName,
        bool dryRun)
    {
        var originalSolution = solution;
        var result = await ApplyAsync(solution, typeName, depType, fieldName);
        if (result.Solution == solution)
            return;

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, result.Solution, dryRun);
        _logger.RtInfo("Injected {DepType} into {TargetType}.", depType, result.TargetType ?? typeName);
    }

    internal async Task<DependencyInjectionResult> ApplyAsync(
        Solution solution,
        string typeName,
        string depType,
        string? fieldName)
    {
        var resolvedField = fieldName ?? DeriveFieldName(depType);
        var paramName = resolvedField.TrimStart('_');

        var target = await ResolveTargetClassAsync(solution, typeName);
        if (target is null)
            return new DependencyInjectionResult(solution, false, null);

        var dependencyTypeSymbol = ResolveDependencyTypeSymbol(target.SemanticModel, target.Declaration, depType);
        var injector = new DependencyInjector(
            typeName,
            depType,
            resolvedField,
            paramName,
            target.SemanticModel,
            dependencyTypeSymbol,
            target.Symbol);

        var newRoot = injector.Visit(target.Root);

        switch (injector.Status)
        {
            case DependencyInjectionStatus.ClassNotFound:
                _logger.RtError("Class '{TypeName}' not found in solution.", typeName);
                Environment.ExitCode = 1;
                return new DependencyInjectionResult(solution, false, target.Symbol.ToDisplayString(TypeDisplayFormat));

            case DependencyInjectionStatus.AlreadyInjected:
                _logger.RtInfo("Dependency '{DepType}' is already injected into '{TargetType}'.", depType, target.Symbol.ToDisplayString(TypeDisplayFormat));
                return new DependencyInjectionResult(solution, false, target.Symbol.ToDisplayString(TypeDisplayFormat));

            case DependencyInjectionStatus.Unsupported:
                _logger.RtError("{Message}", injector.ValidationError ??
                    $"Cannot inject '{depType}' into '{target.Symbol.ToDisplayString(TypeDisplayFormat)}'.");
                Environment.ExitCode = 1;
                return new DependencyInjectionResult(solution, false, target.Symbol.ToDisplayString(TypeDisplayFormat));

            case DependencyInjectionStatus.Applied:
                solution = solution.WithDocumentSyntaxRoot(target.Document.Id, newRoot!);
                return new DependencyInjectionResult(solution, true, target.Symbol.ToDisplayString(TypeDisplayFormat));

            default:
                throw new InvalidOperationException($"Unknown dependency injection status '{injector.Status}'.");
        }
    }

    internal sealed record DependencyInjectionResult(
        Solution Solution,
        bool Changed,
        string? TargetType);

    internal async Task<ResolvedTarget?> ResolveTargetClassAsync(Solution solution, string typeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        var isQualifiedName = normalizedTypeName.Contains('.', StringComparison.Ordinal);
        var matches = new List<ResolvedTarget>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel is null)
                    continue;

                foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                        continue;

                    if (!IsTypeMatch(symbol, normalizedTypeName, isQualifiedName))
                        continue;

                    matches.Add(new ResolvedTarget(document, root, semanticModel, declaration, symbol));
                }
            }
        }

        if (matches.Count == 0)
        {
            _logger.RtError("Class '{TypeName}' not found in solution.", typeName);
            Environment.ExitCode = 1;
            return null;
        }

        if (matches.Count > 1)
        {
            var candidateList = string.Join(", ", matches
                .Select(m => m.Symbol.ToDisplayString(TypeDisplayFormat))
                .Distinct(StringComparer.Ordinal));
            _logger.RtError("Class '{TypeName}' is ambiguous across the loaded solution: {CandidateList}. Pass a fully qualified type name.", typeName, candidateList);
            Environment.ExitCode = 1;
            return null;
        }

        return matches[0];
    }

    internal ITypeSymbol? ResolveDependencyTypeSymbol(
        SemanticModel semanticModel,
        ClassDeclarationSyntax declaration,
        string depType)
    {
        var typeSyntax = SyntaxFactory.ParseTypeName(depType);
        var speculative = semanticModel.GetSpeculativeTypeInfo(
            declaration.OpenBraceToken.SpanStart,
            typeSyntax,
            SpeculativeBindingOption.BindAsTypeOrNamespace);

        if (speculative.Type is not null)
            return speculative.Type;

        return semanticModel.LookupNamespacesAndTypes(declaration.Identifier.SpanStart, name: depType)
            .OfType<ITypeSymbol>()
            .FirstOrDefault();
    }

    internal bool IsTypeMatch(INamedTypeSymbol symbol, string normalizedTypeName, bool isQualifiedName)
    {
        if (isQualifiedName)
            return NormalizeTypeName(symbol.ToDisplayString(TypeDisplayFormat)) == normalizedTypeName;

        return symbol.Name == normalizedTypeName;
    }

    internal string NormalizeTypeName(string typeName) =>
        typeName.Replace("global::", string.Empty, StringComparison.Ordinal).Trim();

    internal string DeriveFieldName(string depType)
    {
        var name = depType;
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name[1..];

        name = char.ToLower(name[0], CultureInfo.InvariantCulture) + name[1..];
        return "_" + name;
    }
}
