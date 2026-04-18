using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class FindImplementationsCommand : IRtCommand
{
    private FindImplementationsCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-impl",
        Mutates: false,
        JsonOutput: true,
        Category: "Navigation",
        Aliases: ["impls"],
        WhenToUse: "Discovering all concrete implementations of an interface or derived classes of an abstract base, across the entire solution. Useful before making a breaking change to a contract, or to verify a new interface has been wired up everywhere expected.",
        Examples:
        [
            "rt find-impl IEmailService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt impls ProjectX.Core.Services.IBookingRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-impl IRtCommand --solution RoslynTools.slnx",
        ],
        KnownIssues:
        [
            "Invoking via `cmd //c` from bash on Windows crashes during workspace load — see ai-improvements row #13.",
        ]
    );

    public static Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified interface or abstract class name");
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");

        var cmd = new Command("find-impl", "Find all implementations of an interface or abstract class (JSON)")
        {
            symbolArg, solutionOpt
        };
        cmd.AddAlias("impls");

        cmd.SetHandler(async (symbol, solution) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, "type");

                if (sym is not INamedTypeSymbol typeSymbol)
                    throw new InvalidOperationException($"'{symbol}' is not a type (found {sym.Kind}).");

                if (typeSymbol.TypeKind != TypeKind.Interface && !typeSymbol.IsAbstract)
                    Console.Error.WriteLine($"Warning: '{symbol}' is not an interface or abstract class. Searching for derived types.");

                var implementations = await FindImplementationsAsync(sln, typeSymbol);

                var result = new Dictionary<string, object?>
                {
                    ["symbol"] = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    ["symbolKind"] = typeSymbol.TypeKind.ToString(),
                    ["implementationCount"] = implementations.Count,
                    ["implementations"] = implementations,
                };

                Console.WriteLine(JsonSerializer.Serialize(result, SymbolFormatter.DefaultJsonOptions));
            }
        },
        symbolArg, solutionOpt);

        return cmd;
    }

    private static async Task<List<Dictionary<string, object?>>> FindImplementationsAsync(
        Solution solution,
        INamedTypeSymbol typeSymbol)
    {
        var results = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution);

        foreach (var impl in implementations)
        {
            if (impl is not INamedTypeSymbol implType) continue;

            var fqn = implType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (!seen.Add(fqn)) continue;

            var locations = SymbolFormatter.ExtractSourceLocations(implType);

            var entry = new Dictionary<string, object?>
            {
                ["name"] = implType.Name,
                ["fqn"] = fqn,
                ["typeKind"] = implType.TypeKind.ToString(),
                ["accessibility"] = implType.DeclaredAccessibility.ToString(),
                ["isAbstract"] = implType.IsAbstract,
                ["project"] = implType.ContainingAssembly?.Name,
                ["locations"] = locations,
            };

            // Show which interfaces this impl covers (useful when searching for a base interface)
            var directInterfaces = implType.Interfaces
                .Select(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .ToList();
            if (directInterfaces.Count > 0)
                entry["directInterfaces"] = directInterfaces;

            if (implType.BaseType != null && implType.BaseType.SpecialType != SpecialType.System_Object)
                entry["baseType"] = implType.BaseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

            results.Add(entry);
        }

        // Also find derived classes (for abstract classes)
        if (typeSymbol.TypeKind == TypeKind.Class && typeSymbol.IsAbstract)
        {
            var derived = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, solution);
            foreach (var d in derived)
            {
                var fqn = d.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                if (!seen.Add(fqn)) continue;

                var locations = SymbolFormatter.ExtractSourceLocations(d);

                results.Add(new Dictionary<string, object?>
                {
                    ["name"] = d.Name,
                    ["fqn"] = fqn,
                    ["typeKind"] = d.TypeKind.ToString(),
                    ["accessibility"] = d.DeclaredAccessibility.ToString(),
                    ["isAbstract"] = d.IsAbstract,
                    ["project"] = d.ContainingAssembly?.Name,
                    ["locations"] = locations,
                });
            }
        }

        return results;
    }
}
