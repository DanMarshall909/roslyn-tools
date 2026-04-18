using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class InspectCommand : IRtCommand
{
    private InspectCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "inspect",
        Mutates: false,
        JsonOutput: true,
        Category: "Navigation",
        Aliases: null,
        WhenToUse: "Getting a detailed JSON description of a symbol's shape — members, base types, interfaces, constructors, locations — without opening the file. Ideal for agents that need to understand a type's surface area before planning a refactor.",
        Examples:
        [
            "rt inspect BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect ProjectX.Core.Services.IEmailService --kind type --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect IRtCommand --solution RoslynTools.slnx",
        ],
        KnownIssues:
        [
            "Invoking via `cmd //c` from bash on Windows crashes during workspace load — see ai-improvements row #13.",
        ]
    );

    public static Command Build()
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
                Console.WriteLine(JsonSerializer.Serialize(result, SymbolFormatter.DefaultJsonOptions));
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
        result["locations"] = SymbolFormatter.ExtractSourceLocations(symbol);

        // Containing info
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
