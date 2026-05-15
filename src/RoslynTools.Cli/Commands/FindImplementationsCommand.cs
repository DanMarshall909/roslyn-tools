using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all implementations of a given symbol in the current file
/// </summary>
internal sealed class FindImplementationsCommand : IRtCommand, IStructuredOutputCommand
{
    private FindImplementationsCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "find-impl",
        false,
        true,
        "inspect",
        Aliases: ["impls"],
        WhenToUse:
        "Discovering all concrete implementations of an interface or derived classes of an abstract base, across the entire solution. Useful before making a breaking change to a contract, or to verify a new interface has been wired up everywhere expected.",
        Examples:
        [
            "rt inspect find-impl IEmailService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect impls ProjectX.Core.Services.IBookingRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-impl IRtCommand --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public static Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified interface or abstract class name");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");

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
                        RtLog.Warning("'{Symbol}' is not an interface or abstract class. Searching for derived types.",
                            symbol);

                    var implementations =
                        await RtServiceProvider.Get<FindImplementationsService>().FindAsync(sln, typeSymbol);

                    var result = new Dictionary<string, object?>
                    {
                        ["symbol"] = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        ["symbolKind"] = typeSymbol.TypeKind.ToString(),
                        ["implementationCount"] = implementations.Count,
                        ["implementations"] = implementations
                    };

                    StructuredOutput.Write(result, SymbolFormatter.DefaultJsonOptions);
                }
            },
            symbolArg, solutionOpt);

        return cmd;
    }
}
