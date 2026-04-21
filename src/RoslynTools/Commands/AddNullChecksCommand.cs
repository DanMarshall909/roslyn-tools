using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class AddNullChecksCommand : IRtCommand
{
    private AddNullChecksCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-null-checks",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["anc"],
        WhenToUse: "Retrofitting ArgumentNullException.ThrowIfNull guards onto reference-type parameters in an existing method. Prefer this over a manual edit when a method has several parameters and you want to skip any that are already guarded or are nullable (annotated with ?).",
        Examples:
        [
            "rt add-null-checks --type BookingService --method CreateBooking --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt anc --type OrderProcessor --method Process --dry-run",
            "rt add-null-checks --type ReportController --method Generate --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class containing the method") { IsRequired = true };
        var methodOpt = new Option<string>("--method", "Method name to add null checks to") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-null-checks", "Insert ArgumentNullException.ThrowIfNull guards for reference-type parameters");
        cmd.AddAlias("anc");
        cmd.AddAll(typeOpt, methodOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (type, method, solution, dryRun) =>
        {
            await ExecuteAsync(type, method, solution, dryRun);
        }, typeOpt, methodOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string methodName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = sln;

            var methodSymbol = await FindMethodSymbolAsync(sln, typeName, methodName);
            if (methodSymbol == null)
                throw new InvalidOperationException($"Method '{methodName}' not found in type '{typeName}'.");

            var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
                throw new InvalidOperationException($"No source declaration found for '{methodName}'.");

            var methodSyntax = (MethodDeclarationSyntax)await syntaxRef.GetSyntaxAsync();
            if (methodSyntax.Body == null)
            {
                Console.WriteLine("Method has no block body — skipped.");
                return;
            }

            // Determine which parameters need guards
            var eligibleParams = methodSymbol.Parameters
                .Where(p => !p.Type.IsValueType)
                .Where(p => p.NullableAnnotation != NullableAnnotation.Annotated)
                .Where(p => !HasExistingGuard(methodSyntax.Body, p.Name))
                .ToList();

            if (eligibleParams.Count == 0)
            {
                Console.WriteLine("No null checks needed.");
                return;
            }

            // Build ThrowIfNull statements to prepend (in declaration order)
            var newStatements = eligibleParams
                .Select(p => SyntaxFactory.ParseStatement($"ArgumentNullException.ThrowIfNull({p.Name});\n")
                    .WithLeadingTrivia(methodSyntax.Body.Statements.Count > 0
                        ? methodSyntax.Body.Statements[0].GetLeadingTrivia()
                        : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace("            "))))
                .ToArray();

            // Insert before existing statements
            var updatedStatements = new SyntaxList<StatementSyntax>(newStatements)
                .AddRange(methodSyntax.Body.Statements);

            var newBody = methodSyntax.Body.WithStatements(updatedStatements);
            var newMethod = methodSyntax.WithBody(newBody);

            // Replace the method in the document root
            var docId = SymbolDocumentLocator.FindDocumentId(sln, methodSymbol);
            if (docId == null)
                throw new InvalidOperationException("Cannot locate source document for the target method.");

            var doc = sln.GetDocument(docId)!;
            var root = (CompilationUnitSyntax)(await doc.GetSyntaxRootAsync())!;

            // Locate the method node first (before any root modification)
            var oldMethodNode = root.FindNode(syntaxRef.Span) as MethodDeclarationSyntax;
            if (oldMethodNode == null)
                throw new InvalidOperationException("Cannot locate method syntax node in document.");

            // Replace the method node
            root = root.ReplaceNode(oldMethodNode, newMethod);

            // Ensure `using System;` is present (after method replacement)
            root = RoslynSyntax.EnsureUsing(root, "System");

            sln = sln.WithDocumentSyntaxRoot(docId, root);
            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
    }

    private static bool HasExistingGuard(BlockSyntax body, string paramName)
    {
        foreach (var stmt in body.Statements)
        {
            var text = stmt.ToString();
            // Match ThrowIfNull(paramName) or null check patterns
            if (text.Contains($"ThrowIfNull({paramName})", StringComparison.Ordinal))
                return true;
            if (text.Contains($"{paramName} != null", StringComparison.Ordinal) ||
                text.Contains($"null != {paramName}", StringComparison.Ordinal) ||
                text.Contains($"{paramName} is null", StringComparison.Ordinal) ||
                text.Contains($"{paramName} == null", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static async Task<IMethodSymbol?> FindMethodSymbolAsync(Solution sln, string typeName, string methodName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
            {
                if (symbol is INamedTypeSymbol typeSymbol)
                {
                    var method = typeSymbol.GetMembers(methodName)
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault();
                    if (method != null)
                        return method;
                }
            }
        }
        return null;
    }

}
