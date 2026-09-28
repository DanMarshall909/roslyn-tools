using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// A class used to replace type services in a solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ReplaceTypeService : IRtService
{
    internal sealed record Preparation(Solution Solution, int ChangedFiles, int ClassCount);
    private readonly ILogger<ReplaceTypeService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public ReplaceTypeService()
        : this(new RtConsoleLogger<ReplaceTypeService>(), new SolutionChangeWriter())
    {
    }

    public ReplaceTypeService(ILogger<ReplaceTypeService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Runs an asynchronous method to replace type services in a solution with a boolean parameter.
    /// </summary>
    internal async Task RunAsync(Solution solution, bool dryRun)
    {
        var originalSolution = solution;
        var prepared = await PrepareAsync(solution);
        if (prepared.ChangedFiles == 0)
        {
            _logger.RtInfo("No DateTime.UtcNow or DateTime.Now usage found.");
            return;
        }
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, prepared.Solution, dryRun);
    }

    internal async Task<Preparation> PrepareAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var changedFiles = 0;
        var classesNeedingInjection = new List<string>();

        foreach (var project in solution.Projects)
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath == null) continue;

                cancellationToken.ThrowIfCancellationRequested();
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                if (root == null) continue;

                var rewriter = new DateTimeRewriter();
                var newRoot = rewriter.Visit(root);

                if (!rewriter.Changed) continue;

                var injector = new ConstructorInjector(_logger);
                newRoot = injector.Visit(newRoot);

                if (newRoot is CompilationUnitSyntax compilationUnit)
                    newRoot = RoslynSyntax.EnsureUsing(compilationUnit, "System");

                solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
                changedFiles++;
                classesNeedingInjection.AddRange(rewriter.ClassesWithReplacements);
            }

        if (changedFiles == 0)
        {
            return new Preparation(solution, 0, 0);
        }

        return new Preparation(solution, changedFiles, classesNeedingInjection.Distinct().Count());
    }

    /// <summary>
    /// A named type used to rewrite datetime expressions in a solution.
    /// </summary>
    internal sealed class DateTimeRewriter : CSharpSyntaxRewriter
    {
        private string? _currentClass;
        public bool Changed { get; private set; }
        public List<string> ClassesWithReplacements { get; } = [];

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

    /// <summary>
    /// A named type used to provide constructor injection in a solution.
    /// </summary>
    internal sealed class ConstructorInjector(ILogger<ReplaceTypeService> logger) : CSharpSyntaxRewriter
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
                logger.RtWarning(
                    "Static class '{TypeName}' uses _timeProvider but cannot accept constructor injection.",
                    visited.Identifier.Text);
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
                for (var i = 0; i < members.Count; i++)
                    if (members[i] is FieldDeclarationSyntax)
                        insertIndex = i + 1;
                    else
                        break;

                members = members.Insert(insertIndex, fieldDecl);
                members = members.Insert(insertIndex + 1, ctorDecl);
                return visited.WithMembers(members);
            }
            else
            {
                var result = visited;
                var members = result.Members;
                var insertIndex = 0;
                for (var i = 0; i < members.Count; i++)
                    if (members[i] is FieldDeclarationSyntax)
                        insertIndex = i + 1;
                    else
                        break;
                members = members.Insert(insertIndex, fieldDecl);
                result = result.WithMembers(members);

                constructors = result.Members.OfType<ConstructorDeclarationSyntax>().ToList();

                var ctorReplacements =
                    new List<(ConstructorDeclarationSyntax Original, ConstructorDeclarationSyntax Replacement)>();

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
