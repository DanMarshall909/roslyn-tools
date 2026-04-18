using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class ReplaceTypeCommand : IRtCommand
{
    private ReplaceTypeCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "replace-datetime",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: null,
        WhenToUse: "Replacing all DateTime.UtcNow and DateTime.Now usages with injected TimeProvider calls (_timeProvider.GetUtcNow().UtcDateTime / GetLocalNow().DateTime) and automatically wiring TimeProvider into the affected class constructors. Run solution-wide before adding unit tests that need controllable time.",
        Examples:
        [
            "rt replace-datetime --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt replace-datetime --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt replace-datetime --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("replace-datetime",
            "Replace DateTime.UtcNow / DateTime.Now with injected TimeProvider, adding constructor injection")
        {
            solutionOpt, dryRunOpt
        };

        cmd.SetHandler(async (solution, dryRun) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                await ExecuteAsync(workspace, sln, dryRun);
            }
        },
        solutionOpt, dryRunOpt);

        return cmd;
    }

    private static async Task ExecuteAsync(Workspace workspace, Solution solution, bool dryRun)
    {
        var originalSolution = workspace.CurrentSolution;
        int changedFiles = 0;
        var classesNeedingInjection = new List<string>();

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath == null) continue;

                var root = await doc.GetSyntaxRootAsync();
                if (root == null) continue;

                var rewriter = new DateTimeRewriter();
                var newRoot = rewriter.Visit(root);

                if (!rewriter.Changed) continue;

                // Add constructor injection for TimeProvider to classes that need it
                var injector = new ConstructorInjector();
                newRoot = injector.Visit(newRoot);

                // Add 'using System;' if not present (for TimeProvider)
                if (newRoot is CompilationUnitSyntax compilationUnit)
                {
                    newRoot = EnsureUsing(compilationUnit, "System");
                }

                solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
                changedFiles++;
                classesNeedingInjection.AddRange(rewriter.ClassesWithReplacements);
            }
        }

        if (changedFiles == 0)
        {
            Console.WriteLine("No DateTime.UtcNow or DateTime.Now usage found.");
            return;
        }

        Console.Error.WriteLine(
            $"Replaced DateTime.UtcNow/Now in {changedFiles} file(s). " +
            $"Classes needing TimeProvider injection: {classesNeedingInjection.Distinct().Count()}");

        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);

        // Report classes that couldn't be auto-injected (top-level statements, static classes, etc.)
        Console.Error.WriteLine("\n--- Manual review needed ---");
        Console.Error.WriteLine("Verify constructor injection was added correctly in each class.");
        Console.Error.WriteLine("Static classes and top-level statements need manual wiring.");
    }

    private static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax root, string ns)
    {
        var existing = root.Usings
            .Select(u => u.Name?.ToString().Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (existing.Contains(ns))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(" " + ns))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        return root.AddUsings(newUsing);
    }

    // ──────────────────────────────────────────────────────────────
    //  Phase 1: Rewrite DateTime.UtcNow / DateTime.Now call sites
    // ──────────────────────────────────────────────────────────────

    private class DateTimeRewriter : CSharpSyntaxRewriter
    {
        public bool Changed { get; private set; }
        public List<string> ClassesWithReplacements { get; } = new();

        private string? _currentClass;

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var prev = _currentClass;
            _currentClass = node.Identifier.Text;
            var result = base.VisitClassDeclaration(node);
            _currentClass = prev;
            return result;
        }

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var prev = _currentClass;
            _currentClass = node.Identifier.Text;
            var result = base.VisitStructDeclaration(node);
            _currentClass = prev;
            return result;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;

            var text = visited.ToString();

            if (text == "DateTime.UtcNow")
            {
                Changed = true;
                if (_currentClass != null)
                    ClassesWithReplacements.Add(_currentClass);
                return SyntaxFactory.ParseExpression("_timeProvider.GetUtcNow().UtcDateTime")
                    .WithTriviaFrom(visited);
            }

            if (text == "DateTime.Now")
            {
                Changed = true;
                if (_currentClass != null)
                    ClassesWithReplacements.Add(_currentClass);
                return SyntaxFactory.ParseExpression("_timeProvider.GetLocalNow().DateTime")
                    .WithTriviaFrom(visited);
            }

            return visited;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Phase 2: Add TimeProvider field + constructor parameter
    // ──────────────────────────────────────────────────────────────

    private class ConstructorInjector : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            // Only inject if this class has _timeProvider references
            if (!visited.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(id => id.Identifier.Text == "_timeProvider"))
                return visited;

            // Skip if _timeProvider field already exists
            if (visited.Members
                .OfType<FieldDeclarationSyntax>()
                .Any(f => f.Declaration.Variables
                    .Any(v => v.Identifier.Text == "_timeProvider")))
                return visited;

            // Skip static classes — can't inject
            if (visited.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                Console.Error.WriteLine(
                    $"  MANUAL: static class '{visited.Identifier.Text}' uses _timeProvider but cannot accept constructor injection");
                return visited;
            }

            // Add the field
            var fieldDecl = SyntaxFactory.ParseMemberDeclaration(
                "    private readonly TimeProvider _timeProvider;\n")!;

            // Find existing constructors
            var constructors = visited.Members.OfType<ConstructorDeclarationSyntax>().ToList();

            if (constructors.Count == 0)
            {
                // No constructor — create one
                var ctorText =
                    $"    public {visited.Identifier.Text}(TimeProvider timeProvider)\n" +
                    $"    {{\n" +
                    $"        _timeProvider = timeProvider;\n" +
                    $"    }}\n";
                var ctorDecl = SyntaxFactory.ParseMemberDeclaration(ctorText)!;

                // Insert field before first member, constructor after field
                var members = visited.Members;
                var insertIndex = 0;

                // Find where existing fields end to insert after them
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i] is FieldDeclarationSyntax)
                        insertIndex = i + 1;
                    else
                        break;
                }

                members = members.Insert(insertIndex, fieldDecl);
                members = members.Insert(insertIndex + 1, ctorDecl);

                return visited.WithMembers(members);
            }
            else
            {
                // Add parameter to existing constructor(s) and assignment in body
                var result = visited;

                // Insert field before first member, after existing fields
                var members = result.Members;
                var insertIndex = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    if (members[i] is FieldDeclarationSyntax)
                        insertIndex = i + 1;
                    else
                        break;
                }
                members = members.Insert(insertIndex, fieldDecl);
                result = result.WithMembers(members);

                // Re-find constructors after inserting field
                constructors = result.Members.OfType<ConstructorDeclarationSyntax>().ToList();

                // Build (original → replacement) pairs, then apply all at once via ReplaceNodes
                // so that later iterations don't reference stale nodes from earlier replacements.
                var ctorReplacements = new List<(ConstructorDeclarationSyntax Original, ConstructorDeclarationSyntax Replacement)>();

                foreach (var ctor in constructors)
                {
                    if (ctor.ParameterList.Parameters
                        .Any(p => p.Type?.ToString().Contains("TimeProvider") == true))
                        continue;

                    var newParam = SyntaxFactory.Parameter(
                        SyntaxFactory.Identifier("timeProvider"))
                        .WithType(SyntaxFactory.ParseTypeName("TimeProvider "));

                    var newParamList = ctor.ParameterList.AddParameters(newParam);
                    var newCtor = ctor.WithParameterList(newParamList);

                    var assignment = SyntaxFactory.ParseStatement(
                        "        _timeProvider = timeProvider;\n");

                    if (newCtor.Body != null)
                    {
                        var newBody = newCtor.Body.WithStatements(
                            newCtor.Body.Statements.Insert(0, assignment));
                        newCtor = newCtor.WithBody(newBody);
                    }

                    ctorReplacements.Add((ctor, newCtor));
                }

                if (ctorReplacements.Count > 0)
                    result = result.ReplaceNodes(
                        ctorReplacements.Select(r => r.Original),
                        (original, _) => ctorReplacements
                            .First(r => ReferenceEquals(r.Original, original))
                            .Replacement);

                return result;
            }
        }
    }
}
