using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class AddTestCommand : IRtCommand
{
    private AddTestCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-test",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["at"],
        WhenToUse: "Appending xUnit [Fact] stub methods to an existing test class — either a single named test or a batch from semicolon-separated acceptance criteria. Eliminates the boilerplate of writing throw-stub test shells by hand and optionally groups ACs into a nested class.",
        Examples:
        [
            "rt add-test --class BookingServiceTests --name Returns_404_When_Not_Found --solution RoslynTools.slnx",
            "rt at --class OrderTests --acs \"Returns total;Applies discount;Rejects empty cart\" --nested-class WhenCalculatingTotal",
            "rt add-test --class PaymentTests --acs \"Charges card;Sends receipt\" --file tests/PaymentTests.cs --dry-run",
        ]
    );

    public static Command Build()
    {
        var classOpt = new Option<string>("--class", "Test class name to add methods to") { IsRequired = true };
        var nameOpt = new Option<string?>("--name", "Method name for single-test mode");
        var acsOpt = new Option<string?>("--acs", "Semicolon-separated acceptance criteria (AC mode)");
        var nestedClassOpt = new Option<string?>("--nested-class", "Wrap AC-generated methods in a nested class with this name");
        var fileOpt = new Option<string?>("--file", "Path to the .cs file containing the test class");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-test", "Append xUnit test stubs to an existing test class");
        cmd.AddAlias("at");
        cmd.AddAll(classOpt, nameOpt, acsOpt, nestedClassOpt, fileOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (className, name, acs, nestedClass, file, solution, dryRun) =>
        {
            await ExecuteAsync(className, name, acs, nestedClass, file, solution, dryRun);
        }, classOpt, nameOpt, acsOpt, nestedClassOpt, fileOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string className,
        string? name,
        string? acs,
        string? nestedClass,
        string? filePath,
        string? solutionPath,
        bool dryRun)
    {
        if (name == null && acs == null)
        {
            Console.Error.WriteLine("Error: either --name or --acs is required.");
            Environment.ExitCode = 1;
            return;
        }

        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await AddTestCore.RunAsync(sln, className, name, acs, nestedClass, filePath, dryRun);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC → C# identifier conversion (delegates to Core; kept here for tests)
    // ──────────────────────────────────────────────────────────────────────────

    internal static string AcToMethodName(string ac) => AddTestCore.AcToMethodName(ac);
}
