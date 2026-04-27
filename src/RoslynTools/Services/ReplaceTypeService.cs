using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class ReplaceTypeService
{
    internal static async Task RunAsync(Solution solution, bool dryRun)
    {
        var originalSolution = solution;
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

                var injector = new ConstructorInjector();
                newRoot = injector.Visit(newRoot);

                if (newRoot is CompilationUnitSyntax compilationUnit)
                    newRoot = RoslynSyntax.EnsureUsing(compilationUnit, "System");

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

        RtLog.Info(
            "Replaced DateTime.UtcNow/Now in {ChangedFileCount} file(s). Classes needing TimeProvider injection: {ClassCount}",
            changedFiles,
            classesNeedingInjection.Distinct().Count());

        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);

        RtLog.Warning("\n--- Manual review needed ---");
        RtLog.Warning("Verify constructor injection was added correctly in each class.");
        RtLog.Warning("Static classes and top-level statements need manual wiring.");
    }

    internal sealed class DateTimeRewriter : CSharpSyntaxRewriter
    {
        public bool Changed { get; private set; }
        public List<string> ClassesWithReplacements { get; } = [];

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

    internal sealed class ConstructorInjector : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            if (!visited.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(id => id.Identifier.Text == "_timeProvider"))
                return visited;

            if (visited.Members
                .OfType<FieldDeclarationSyntax>()
                .Any(f => f.Declaration.Variables
                    .Any(v => v.Identifier.Text == "_timeProvider")))
                return visited;

            if (visited.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                RtLog.Warning("Static class '{TypeName}' uses _timeProvider but cannot accept constructor injection.", visited.Identifier.Text);
                return visited;
            }

            var fieldDecl = SyntaxFactory.ParseMemberDeclaration(
                "    private readonly TimeProvider _timeProvider;\n")!;

            var constructors = visited.Members.OfType<ConstructorDeclarationSyntax>().ToList();

            if (constructors.Count == 0)
            {
                var ctorText =
                    $"    public {visited.Identifier.Text}(TimeProvider timeProvider)\n" +
                    $"    {{\n" +
                    $"        _timeProvider = timeProvider;\n" +
                    $"    }}\n";
                var ctorDecl = SyntaxFactory.ParseMemberDeclaration(ctorText)!;

                var members = visited.Members;
                var insertIndex = 0;
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
                var result = visited;
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

                constructors = result.Members.OfType<ConstructorDeclarationSyntax>().ToList();

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
