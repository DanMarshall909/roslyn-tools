using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Adds trimming-safe references for types reached through reflection.</summary>
internal sealed class AddDynamicDependencyService : IRtService
{
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public AddDynamicDependencyService()
        : this(new SolutionChangeWriter())
    {
    }

    public AddDynamicDependencyService(ISolutionChangeWriter solutionChangeWriter)
    {
        _solutionChangeWriter = solutionChangeWriter;
    }

    internal async Task RunAsync(
        Solution solution,
        IReadOnlyCollection<string> typeNames,
        string rootMethod,
        bool dryRun)
    {
        if (typeNames.Count == 0)
            throw new ArgumentException("At least one --type must be supplied.", nameof(typeNames));

        var originalSolution = solution;
        var (document, method, semanticModel) = await FindRootMethodAsync(solution, rootMethod);
        var targetTypes = new List<INamedTypeSymbol>();

        foreach (var typeName in typeNames.Distinct(StringComparer.Ordinal))
            targetTypes.Add(await FindTypeAsync(solution, typeName)
                            ?? throw new InvalidOperationException($"Type '{typeName}' was not found."));

        var existingTargets = GetExistingTargets(method, semanticModel);
        var attributes = targetTypes
            .Where(type => !existingTargets.Contains(type, SymbolEqualityComparer.Default))
            .Select(BuildAttribute)
            .ToArray();

        if (attributes.Length == 0)
            return;

        var updatedMethod = method.AddAttributeLists(
            SyntaxFactory.AttributeList(SyntaxFactory.SeparatedList(attributes))
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed));

        var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException("The composition-root document has no syntax root."));
        var updatedRoot = root.ReplaceNode(method, updatedMethod);

        if (!updatedRoot.Usings.Any(u => u.Name?.ToString() == "System.Diagnostics.CodeAnalysis"))
        {
            updatedRoot = updatedRoot.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Diagnostics.CodeAnalysis"))
                    .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed));
        }

        solution = solution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private static AttributeSyntax BuildAttribute(INamedTypeSymbol targetType)
    {
        var typeName = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("DynamicDependency"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList(
                [
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("DynamicallyAccessedMemberTypes"),
                            SyntaxFactory.IdentifierName("PublicConstructors"))),
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.TypeOfExpression(SyntaxFactory.ParseTypeName(typeName)))
                ])));
    }

    private static HashSet<INamedTypeSymbol> GetExistingTargets(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in method.AttributeLists.SelectMany(list => list.Attributes))
        {
            var attributeType = semanticModel.GetTypeInfo(attribute).Type;
            if (attributeType?.Name is not "DynamicDependencyAttribute")
                continue;

            var typeOf = attribute.ArgumentList?.Arguments
                .Select(argument => argument.Expression)
                .OfType<TypeOfExpressionSyntax>()
                .FirstOrDefault();
            if (typeOf != null && semanticModel.GetTypeInfo(typeOf.Type).Type is INamedTypeSymbol target)
                result.Add(target);
        }

        return result;
    }

    private static async Task<(Document Document, MethodDeclarationSyntax Method, SemanticModel SemanticModel)>
        FindRootMethodAsync(Solution solution, string rootMethod)
    {
        var separator = rootMethod.LastIndexOf('.');
        if (separator <= 0 || separator == rootMethod.Length - 1)
            throw new ArgumentException("--root must use the form Type.Method.", nameof(rootMethod));

        var typeName = rootMethod[..separator];
        var methodName = rootMethod[(separator + 1)..];
        var rootType = await FindTypeAsync(solution, typeName)
                       ?? throw new InvalidOperationException($"Composition-root type '{typeName}' was not found.");

        var methodSymbol = rootType.GetMembers(methodName).OfType<IMethodSymbol>().SingleOrDefault()
                           ?? throw new InvalidOperationException(
                               $"Composition-root method '{rootMethod}' was not found or is overloaded.");
        var syntax = await methodSymbol.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
        var method = syntax as MethodDeclarationSyntax
                     ?? throw new InvalidOperationException("The composition root must be a source method.");
        var document = solution.GetDocument(method.SyntaxTree)
                       ?? throw new InvalidOperationException("The composition-root document could not be located.");
        var semanticModel = await document.GetSemanticModelAsync()
                            ?? throw new InvalidOperationException("The composition-root semantic model could not be loaded.");

        return (document, method, semanticModel);
    }

    private static async Task<INamedTypeSymbol?> FindTypeAsync(Solution solution, string typeName)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                continue;

            var exact = compilation.GetTypeByMetadataName(typeName);
            if (exact != null)
                return exact;

            var matches = compilation.GetSymbolsWithName(
                    name => name.Equals(typeName, StringComparison.Ordinal)
                            || name.Equals(typeName.Split('.').Last(), StringComparison.Ordinal),
                    SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .Where(symbol => symbol.ToDisplayString() == typeName || symbol.Name == typeName)
                .ToArray();
            if (matches.Length == 1)
                return matches[0];
        }

        return null;
    }
}
