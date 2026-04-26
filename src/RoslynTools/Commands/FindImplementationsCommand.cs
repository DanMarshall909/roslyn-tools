using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;

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

                var implementations = await FindImplementationsService.FindAsync(sln, typeSymbol);

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
}
