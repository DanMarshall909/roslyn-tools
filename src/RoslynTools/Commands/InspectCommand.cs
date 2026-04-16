using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class InspectCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    internal static Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified symbol name to inspect");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");

        var cmd = new Command("inspect", "Show type shape: members, base types, interfaces, locations (JSON)")
        {
            symbolArg, solutionOpt, kindOpt
        };

        cmd.SetHandler(async (symbol, solution, kind) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);
                var result = BuildInspection(sym, sln);
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
            }
        },
        symbolArg, solutionOpt, kindOpt);

        return cmd;
    }

    private static Dictionary<string, object?> BuildInspection(ISymbol symbol, Solution solution)
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

        // Locations
        var locations = symbol.Locations
            .Where(l => l.IsInSource)
            .Select(l =>
            {
                var span = l.GetLineSpan();
                return new Dictionary<string, object?>
                {
                    ["file"] = span.Path,
                    ["line"] = span.StartLinePosition.Line + 1,
                };
            })
            .ToList();
        result["locations"] = locations;

        // Containing info
        result["namespace"] = symbol.ContainingNamespace?.ToDisplayString();
        result["containingType"] = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (symbol is INamedTypeSymbol type)
        {
            result["typeKind"] = type.TypeKind.ToString();

            if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object)
                result["baseType"] = type.BaseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

            var interfaces = type.Interfaces
                .Select(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToList();
            if (interfaces.Count > 0)
                result["interfaces"] = interfaces;

            var typeParams = type.TypeParameters
                .Select(tp => tp.Name)
                .ToList();
            if (typeParams.Count > 0)
                result["typeParameters"] = typeParams;

            var members = type.GetMembers()
                .Where(m => !m.IsImplicitlyDeclared)
                .Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["kind"] = m.Kind.ToString(),
                    ["signature"] = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ["accessibility"] = m.DeclaredAccessibility.ToString(),
                    ["isStatic"] = m.IsStatic,
                })
                .ToList();
            result["members"] = members;

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
