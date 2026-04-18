using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        cmd.Add(classOpt); cmd.Add(nameOpt); cmd.Add(acsOpt); cmd.Add(nestedClassOpt);
        cmd.Add(fileOpt); cmd.Add(solutionOpt); cmd.Add(dryRunOpt);

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
            var originalSolution = sln;
            bool found = false;

            foreach (var project in sln.Projects)
            {
                foreach (var doc in project.Documents)
                {
                    if (doc.FilePath == null) continue;

                    // If --file specified, restrict to that file
                    if (filePath != null &&
                        !string.Equals(doc.FilePath, Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
                        continue;

                    var root = await doc.GetSyntaxRootAsync();
                    if (root == null) continue;

                    var classDecl = root.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault(c => c.Identifier.Text == className);

                    if (classDecl == null) continue;

                    found = true;
                    Console.Error.WriteLine($"Found '{className}' in {doc.FilePath}");

                    var cu = (CompilationUnitSyntax)root;
                    cu = EnsureUsing(cu, "Xunit");
                    cu = EnsureUsing(cu, "System");

                    // Re-find classDecl after root may have changed
                    classDecl = cu.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .First(c => c.Identifier.Text == className);

                    ClassDeclarationSyntax updatedClass;

                    if (name != null)
                    {
                        // Single mode
                        var method = BuildFactMethod(name);
                        updatedClass = classDecl.AddMembers(method);
                    }
                    else
                    {
                        // AC mode
                        var methodNames = acs!
                            .Split(';')
                            .Select(ac => ac.Trim())
                            .Where(ac => ac.Length > 0)
                            .Select(AcToMethodName)
                            .ToList();

                        var methods = methodNames.Select(BuildFactMethod).ToArray();

                        if (nestedClass != null)
                        {
                            // Wrap in nested class — build via text then add members
                            var nestedText = $"public class {nestedClass}\n{{\n}}\n";
                            var nestedShell = (ClassDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(nestedText)!;
                            var nested = nestedShell.AddMembers(methods);

                            updatedClass = classDecl.AddMembers(nested);
                        }
                        else
                        {
                            updatedClass = classDecl.AddMembers(methods);
                        }
                    }

                    cu = cu.ReplaceNode(classDecl, updatedClass);
                    sln = sln.WithDocumentSyntaxRoot(doc.Id, cu);
                    break;
                }

                if (found) break;
            }

            if (!found)
            {
                Console.Error.WriteLine($"Class '{className}' not found in solution.");
                Environment.ExitCode = 1;
                return;
            }

            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC → C# identifier conversion
    // ──────────────────────────────────────────────────────────────────────────

    internal static string AcToMethodName(string ac)
    {
        // Strip non-alphanumeric, non-space characters
        var cleaned = new string(ac.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());

        // Split on whitespace and title-case each word
        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0]) + w[1..].ToLower());

        return string.Join("_", words);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Build a [Fact] stub method node
    // ──────────────────────────────────────────────────────────────────────────

    private static MemberDeclarationSyntax BuildFactMethod(string methodName)
    {
        var text =
            $"[Fact]\n" +
            $"public void {methodName}()\n" +
            $"{{\n" +
            $"    throw new NotImplementedException();\n" +
            $"}}\n";

        return SyntaxFactory.ParseMemberDeclaration(text)!;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Ensure a using directive is present
    // ──────────────────────────────────────────────────────────────────────────

    private static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax root, string ns)
    {
        var existing = root.Usings
            .Select(u => u.Name?.ToString().Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (existing.Contains(ns))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(
                SyntaxFactory.ParseName(" " + ns))
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        return root.AddUsings(newUsing);
    }
}
