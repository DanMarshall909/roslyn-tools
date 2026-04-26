using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class InspectService
{
    internal static Dictionary<string, object?> BuildInspection(ISymbol symbol, Solution solution)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = symbol.Name,
            ["kind"] = symbol.Kind.ToString(),
            ["fqn"] = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ["accessibility"] = symbol.DeclaredAccessibility.ToString(),
            ["isStatic"] = symbol.IsStatic,
            ["isAbstract"] = symbol.IsAbstract,
            ["isSealed"] = symbol.IsSealed,
        };

        result["locations"] = SymbolFormatter.ExtractSourceLocations(symbol);
        result["namespace"] = symbol.ContainingNamespace?.ToDisplayString();
        result["containingType"] = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (symbol is INamedTypeSymbol type)
        {
            result["typeKind"] = type.TypeKind.ToString();

            SymbolFormatter.AddTypeHierarchyInfo(result, type);

            var typeParams = type.TypeParameters
                .Select(tp => tp.Name)
                .ToList();
            if (typeParams.Count > 0)
                result["typeParameters"] = typeParams;

            result["members"] = SymbolFormatter.ExtractMembers(type);

            var constructors = type.Constructors
                .Where(c => !c.IsImplicitlyDeclared)
                .Select(c => new Dictionary<string, object?>
                {
                    ["signature"] = c.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ["parameters"] = c.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    }).ToList(),
                })
                .ToList();
            if (constructors.Count > 0)
                result["constructors"] = constructors;
        }
        else if (symbol is IMethodSymbol method)
        {
            result["returnType"] = method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            result["parameters"] = method.Parameters.Select(p => new Dictionary<string, object?>
            {
                ["name"] = p.Name,
                ["type"] = p.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            }).ToList();
        }
        else if (symbol is IPropertySymbol prop)
        {
            result["type"] = prop.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            result["hasGetter"] = prop.GetMethod != null;
            result["hasSetter"] = prop.SetMethod != null;
        }
        else if (symbol is IFieldSymbol field)
        {
            result["type"] = field.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            result["isConst"] = field.IsConst;
            result["isReadOnly"] = field.IsReadOnly;
            if (field.HasConstantValue)
                result["constantValue"] = field.ConstantValue;
        }

        return result;
    }
}
