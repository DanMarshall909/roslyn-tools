using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services;

/// <summary>
/// A class used to replace XUnit asserts in a solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ReplaceXunitAssertsService : IRtService
{
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public ReplaceXunitAssertsService()
        : this(new SolutionChangeWriter())
    {
    }

    public ReplaceXunitAssertsService(ISolutionChangeWriter solutionChangeWriter)
    {
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Analyzes a solution to identify XUnit assert replacements.
    /// </summary>
    internal async Task<ReplaceXunitAssertsReview> AnalyzeAsync(Solution solution)
    {
        var review = new ReplaceXunitAssertsReview();

        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var analyzer = new ReplaceXunitAssertsAnalyzer(document.FilePath ?? document.Name, this);
                analyzer.Visit(root);
                review.AddRange(analyzer.Entries);
            }

        return review;
    }

    /// <summary>
    /// Runs an asynchronous method to replace XUnit asserts in a solution with a boolean parameter.
    /// </summary>
    internal async Task RunAsync(Solution solution, bool dryRun)
    {
        var originalSolution = solution;
        var newSolution = await ApplyAsync(solution);
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, newSolution, dryRun);
    }

    /// <summary>
    /// Applies XUnit assert replacements to a solution asynchronously.
    /// </summary>
    internal async Task<Solution> ApplyAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var rewriter = new ReplaceXunitAssertsRewriter(this);
                var rewritten = rewriter.Visit(root);
                if (rewritten is null || rewriter.ReplacementCount == 0)
                    continue;

                if (rewritten is CompilationUnitSyntax compilationUnit)
                    rewritten = AddShouldlyUsing(compilationUnit);

                solution = solution.WithDocumentSyntaxRoot(document.Id, rewritten);
            }

        return solution;
    }

    private CompilationUnitSyntax AddShouldlyUsing(CompilationUnitSyntax root)
    {
        if (root.Usings.Any(u => !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                                 && u.Name?.ToString() == "Shouldly"))
            return root;

        var usingDirective = SyntaxFactory.ParseCompilationUnit("using Shouldly;" + Environment.NewLine)
            .Usings
            .Single();

        return root.AddUsings(usingDirective);
    }

    private bool TryRewrite(
        InvocationExpressionSyntax invocation,
        out InvocationExpressionSyntax replacement)
    {
        replacement = invocation;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || !IsAssertTarget(memberAccess.Expression))
            return false;

        var arguments = invocation.ArgumentList.Arguments;
        var methodName = memberAccess.Name.Identifier.ValueText;

        return methodName switch
        {
            "Equal" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldBe", arguments[0].Expression, out replacement),
            "NotEqual" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldNotBe", arguments[0].Expression,
                    out replacement),
            "Same" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldBeSameAs", arguments[0].Expression,
                    out replacement),
            "NotSame" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldNotBeSameAs", arguments[0].Expression,
                    out replacement),
            "True" when arguments.Count == 1
                => RewriteActualOnly(arguments[0].Expression, "ShouldBeTrue", out replacement),
            "False" when arguments.Count == 1
                => RewriteActualOnly(arguments[0].Expression, "ShouldBeFalse", out replacement),
            "Null" when arguments.Count == 1
                => RewriteActualOnly(arguments[0].Expression, "ShouldBeNull", out replacement),
            "NotNull" when arguments.Count == 1
                => RewriteActualOnly(arguments[0].Expression, "ShouldNotBeNull", out replacement),
            "Empty" when arguments.Count == 1
                => RewriteActualOnly(arguments[0].Expression, "ShouldBeEmpty", out replacement),
            "NotEmpty" when arguments.Count == 1
                => RewriteActualOnly(arguments[0].Expression, "ShouldNotBeEmpty", out replacement),
            "Contains" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldContain", arguments[0].Expression,
                    out replacement),
            "DoesNotContain" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldNotContain", arguments[0].Expression,
                    out replacement),
            "StartsWith" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldStartWith", arguments[0].Expression,
                    out replacement),
            "EndsWith" when arguments.Count == 2
                => RewriteActualExpected(arguments[1].Expression, "ShouldEndWith", arguments[0].Expression,
                    out replacement),
            "IsType" when arguments.Count == 1 && memberAccess.Name is GenericNameSyntax isType
                => RewriteGenericActualOnly(arguments[0].Expression, "ShouldBeOfType", isType.TypeArgumentList,
                    out replacement),
            "IsAssignableFrom" when arguments.Count == 1 && memberAccess.Name is GenericNameSyntax assignable
                => RewriteGenericActualOnly(arguments[0].Expression, "ShouldBeAssignableTo",
                    assignable.TypeArgumentList, out replacement),
            "Throws" when arguments.Count == 1 && memberAccess.Name is GenericNameSyntax throws
                => RewriteStaticGeneric("Should", "Throw", throws.TypeArgumentList, arguments[0].Expression,
                    out replacement),
            _ => false
        };
    }

    private bool IsAssertTarget(ExpressionSyntax expression)
    {
        if (expression is IdentifierNameSyntax identifier)
            return identifier.Identifier.ValueText == "Assert";

        return expression is MemberAccessExpressionSyntax memberAccess
               && memberAccess.Name.Identifier.ValueText == "Assert"
               && memberAccess.Expression is IdentifierNameSyntax target
               && target.Identifier.ValueText == "Xunit";
    }

    private bool RewriteActualExpected(
        ExpressionSyntax actual,
        string shouldlyMethod,
        ExpressionSyntax expected,
        out InvocationExpressionSyntax replacement)
    {
        replacement = SyntaxFactory.InvocationExpression(
            ShouldlyMemberAccess(actual, SyntaxFactory.IdentifierName(shouldlyMethod)),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(expected.WithoutTrivia()))));
        return true;
    }

    private bool RewriteActualOnly(
        ExpressionSyntax actual,
        string shouldlyMethod,
        out InvocationExpressionSyntax replacement)
    {
        replacement = SyntaxFactory.InvocationExpression(
            ShouldlyMemberAccess(actual, SyntaxFactory.IdentifierName(shouldlyMethod)));
        return true;
    }

    private bool RewriteGenericActualOnly(
        ExpressionSyntax actual,
        string shouldlyMethod,
        TypeArgumentListSyntax typeArguments,
        out InvocationExpressionSyntax replacement)
    {
        replacement = SyntaxFactory.InvocationExpression(
            ShouldlyMemberAccess(actual,
                SyntaxFactory.GenericName(SyntaxFactory.Identifier(shouldlyMethod), typeArguments)));
        return true;
    }

    private bool RewriteStaticGeneric(
        string typeName,
        string methodName,
        TypeArgumentListSyntax typeArguments,
        ExpressionSyntax argument,
        out InvocationExpressionSyntax replacement)
    {
        replacement = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(typeName),
                SyntaxFactory.GenericName(SyntaxFactory.Identifier(methodName), typeArguments)),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(argument.WithoutTrivia()))));
        return true;
    }

    private MemberAccessExpressionSyntax ShouldlyMemberAccess(
        ExpressionSyntax actual,
        SimpleNameSyntax shouldlyMethod)
    {
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            NormalizeReceiver(actual),
            shouldlyMethod);
    }

    private ExpressionSyntax NormalizeReceiver(ExpressionSyntax actual)
    {
        var receiver = actual.WithoutTrivia();
        return receiver switch
        {
            IdentifierNameSyntax => receiver,
            MemberAccessExpressionSyntax => receiver,
            InvocationExpressionSyntax => receiver,
            ElementAccessExpressionSyntax => receiver,
            ObjectCreationExpressionSyntax => receiver,
            ImplicitArrayCreationExpressionSyntax => receiver,
            ArrayCreationExpressionSyntax => receiver,
            LiteralExpressionSyntax literal when !literal.IsKind(SyntaxKind.NullLiteralExpression) => receiver,
            _ => SyntaxFactory.ParenthesizedExpression(receiver)
        };
    }

    private sealed class ReplaceXunitAssertsRewriter(ReplaceXunitAssertsService service) : CSharpSyntaxRewriter
    {
        public int ReplacementCount { get; private set; }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (!service.TryRewrite(visited, out var replacement))
                return visited;

            ReplacementCount++;
            return replacement.WithTriviaFrom(visited);
        }
    }

    private sealed class ReplaceXunitAssertsAnalyzer(string filePath, ReplaceXunitAssertsService service)
        : CSharpSyntaxWalker
    {
        public List<ReplaceXunitAssertsEntry> Entries { get; } = [];

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (service.TryRewrite(node, out var replacement))
            {
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                Entries.Add(new ReplaceXunitAssertsEntry(
                    filePath,
                    line,
                    node.ToString(),
                    replacement.ToString()));
            }

            base.VisitInvocationExpression(node);
        }
    }
}

/// <summary>
/// A class used to represent entries for XUnit assert replacements.
/// </summary>
internal sealed record ReplaceXunitAssertsEntry(
    string FilePath,
    int Line,
    string Original,
    string Replacement);

/// <summary>
/// A class used to manage and review XUnit assert replacements in a solution.
/// </summary>
internal sealed class ReplaceXunitAssertsReview
{
    private readonly List<ReplaceXunitAssertsEntry> _entries = [];

    /// <summary>
    /// A property used to access the list of entries for XUnit assert replacements.
    /// </summary>
    internal IReadOnlyList<ReplaceXunitAssertsEntry> Entries => _entries;

    /// <summary>
    /// Adds multiple XUnit assert entries to the review collection.
    /// </summary>
    internal void AddRange(IEnumerable<ReplaceXunitAssertsEntry> entries)
    {
        _entries.AddRange(entries);
    }
}
