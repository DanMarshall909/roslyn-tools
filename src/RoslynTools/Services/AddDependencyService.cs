using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class AddDependencyService
{
    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    internal sealed record ResolvedTarget(
        Document Document,
        SyntaxNode Root,
        SemanticModel SemanticModel,
        ClassDeclarationSyntax Declaration,
        INamedTypeSymbol Symbol);

    internal static async Task RunAsync(
        Solution solution,
        string typeName,
        string depType,
        string? fieldName,
        bool dryRun)
    {
        var originalSolution = solution;
        var resolvedField = fieldName ?? DeriveFieldName(depType);
        var paramName = resolvedField.TrimStart('_');

        var target = await ResolveTargetClassAsync(solution, typeName);
        if (target is null)
            return;

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
                Console.Error.WriteLine($"Class '{typeName}' not found in solution.");
                Environment.ExitCode = 1;
                return;

            case DependencyInjectionStatus.AlreadyInjected:
                Console.Error.WriteLine($"Dependency '{depType}' is already injected into '{target.Symbol.ToDisplayString(TypeDisplayFormat)}'.");
                return;

            case DependencyInjectionStatus.Unsupported:
                Console.Error.WriteLine(injector.ValidationError ??
                    $"Cannot inject '{depType}' into '{target.Symbol.ToDisplayString(TypeDisplayFormat)}'.");
                Environment.ExitCode = 1;
                return;

            case DependencyInjectionStatus.Applied:
                solution = solution.WithDocumentSyntaxRoot(target.Document.Id, newRoot!);
                await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
                Console.Error.WriteLine($"Injected {depType} into {target.Symbol.ToDisplayString(TypeDisplayFormat)}.");
                return;

            default:
                throw new InvalidOperationException($"Unknown dependency injection status '{injector.Status}'.");
        }
    }

    internal static async Task<ResolvedTarget?> ResolveTargetClassAsync(Solution solution, string typeName)
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
            Console.Error.WriteLine($"Class '{typeName}' not found in solution.");
            Environment.ExitCode = 1;
            return null;
        }

        if (matches.Count > 1)
        {
            var candidateList = string.Join(", ", matches
                .Select(m => m.Symbol.ToDisplayString(TypeDisplayFormat))
                .Distinct(StringComparer.Ordinal));
            Console.Error.WriteLine($"Class '{typeName}' is ambiguous across the loaded solution: {candidateList}. Pass a fully qualified type name.");
            Environment.ExitCode = 1;
            return null;
        }

        return matches[0];
    }

    internal static ITypeSymbol? ResolveDependencyTypeSymbol(
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

    internal static bool IsTypeMatch(INamedTypeSymbol symbol, string normalizedTypeName, bool isQualifiedName)
    {
        if (isQualifiedName)
            return NormalizeTypeName(symbol.ToDisplayString(TypeDisplayFormat)) == normalizedTypeName;

        return symbol.Name == normalizedTypeName;
    }

    internal static string NormalizeTypeName(string typeName) =>
        typeName.Replace("global::", string.Empty, StringComparison.Ordinal).Trim();

    internal static string DeriveFieldName(string depType)
    {
        var name = depType;
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name[1..];

        name = char.ToLower(name[0], CultureInfo.InvariantCulture) + name[1..];
        return "_" + name;
    }
}
