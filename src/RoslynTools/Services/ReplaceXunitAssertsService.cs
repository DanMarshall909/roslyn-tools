using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// A class used to replace XUnit asserts in a solution.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ReplaceXunitAssertsService : IRtService
{
    /// <summary>
    /// Analyzes a solution to identify XUnit assert replacements.
    /// </summary>
    internal async Task<ReplaceXunitAssertsReview> AnalyzeAsync(
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        var review = new ReplaceXunitAssertsReview();

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                    continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var analyzer = new ReplaceXunitAssertsAnalyzer(document.FilePath ?? document.Name, this, semanticModel);
                analyzer.Visit(root);
                review.AddRange(analyzer.Entries);
            }
        }

        return review;
    }

    internal async Task<Solution> PrepareAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ApplyAsync(solution, cancellationToken);
    }

    /// <summary>
    /// Applies XUnit assert replacements to a solution asynchronously.
    /// </summary>
    internal async Task<Solution> ApplyAsync(
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                    continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var rewriter = new ReplaceXunitAssertsRewriter(this, semanticModel);
                var rewritten = rewriter.Visit(root);
                if (rewritten is null || rewriter.ReplacementCount == 0)
                    continue;

                if (rewritten is CompilationUnitSyntax compilationUnit)
                    rewritten = AddShouldlyUsing(compilationUnit);

                solution = solution.WithDocumentSyntaxRoot(document.Id, rewritten);
            }
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
        AssertContext context,
        out ExpressionSyntax replacement)
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
            "True" when arguments.Count == 1 && context.FirstArgIsNullableBool
                => RewriteActualExpected(arguments[0].Expression, "ShouldBe",
                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression), out replacement),
            "False" when arguments.Count == 1 && context.FirstArgIsNullableBool
                => RewriteActualExpected(arguments[0].Expression, "ShouldBe",
                    SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression), out replacement),
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
                => RewriteContains(arguments, context, "ShouldContain", out replacement),
            "DoesNotContain" when arguments.Count == 2
                => RewriteContains(arguments, context, "ShouldNotContain", out replacement),
            "StartsWith" when arguments.Count == 2
                => RewriteStringComparison(
                    SuppressIfNullable(arguments[1].Expression, context.SecondArgIsMaybeNull),
                    "ShouldStartWith", arguments[0].Expression,
                    context.SecondArgIsString && context.FirstArgIsString,
                    out replacement),
            "EndsWith" when arguments.Count == 2
                => RewriteStringComparison(
                    SuppressIfNullable(arguments[1].Expression, context.SecondArgIsMaybeNull),
                    "ShouldEndWith", arguments[0].Expression,
                    context.SecondArgIsString && context.FirstArgIsString,
                    out replacement),
            "IsType" when arguments.Count == 1 && memberAccess.Name is GenericNameSyntax isType
                => RewriteGenericActualOnly(arguments[0].Expression, "ShouldBeOfType", isType.TypeArgumentList,
                    suppressNullableWarning: false, out replacement),
            "IsAssignableFrom" when arguments.Count == 1 && memberAccess.Name is GenericNameSyntax assignable
                => RewriteGenericActualOnly(arguments[0].Expression, "ShouldBeAssignableTo",
                    assignable.TypeArgumentList,
                    suppressNullableWarning: !context.ResultIsDiscarded, out replacement),
            "Throws" when arguments.Count == 1 && memberAccess.Name is GenericNameSyntax throws
                => RewriteStaticGeneric("Should", "Throw", throws.TypeArgumentList, arguments[0].Expression,
                    out replacement),
            _ => false
        };
    }

    /// <summary>
    /// Rewrites Assert.Contains / Assert.DoesNotContain. The (collection, predicate) overload
    /// requires the collection to be the receiver; all other 2-arg overloads
    /// ((expectedSubstring, actualString), (expectedItem, collection)) put the second arg as
    /// the receiver. Predicates incompatible with Shouldly's Expression&lt;Func&lt;T,bool&gt;&gt;
    /// overload (containing ?. , is patterns, switch expressions, etc.) are left unrewritten.
    /// </summary>
    private bool RewriteContains(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        AssertContext context,
        string shouldlyMethod,
        out ExpressionSyntax replacement)
    {
        replacement = null!;
        if (arguments[1].Expression is LambdaExpressionSyntax lambda)
        {
            if (HasNonExpressionTreeFeatures(lambda))
                return false;
            var actual = SuppressIfNullable(arguments[0].Expression, context.FirstArgIsMaybeNull);
            return RewriteActualExpected(actual, shouldlyMethod, lambda, out replacement);
        }

        var receiver = SuppressIfNullable(arguments[1].Expression, context.SecondArgIsMaybeNull);
        return RewriteStringComparison(
            receiver, shouldlyMethod, arguments[0].Expression,
            context.SecondArgIsString && context.FirstArgIsString,
            out replacement);
    }

    /// <summary>
    /// Emits <c>receiver.ShouldXxx(expected)</c>, adding <c>Case.Sensitive</c> when the receiver
    /// is a string. xUnit's string Assert.Contains/DoesNotContain/StartsWith/EndsWith are
    /// case-sensitive, but Shouldly's defaults are case-insensitive — preserve xUnit semantics.
    /// </summary>
    private bool RewriteStringComparison(
        ExpressionSyntax receiver,
        string shouldlyMethod,
        ExpressionSyntax expected,
        bool addCaseSensitive,
        out ExpressionSyntax replacement)
    {
        if (!addCaseSensitive)
            return RewriteActualExpected(receiver, shouldlyMethod, expected, out replacement);

        var caseSensitive = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName("Case"),
            SyntaxFactory.IdentifierName("Sensitive"));

        var commaWithSpace = SyntaxFactory.Token(SyntaxKind.CommaToken)
            .WithTrailingTrivia(SyntaxFactory.Space);
        replacement = SyntaxFactory.InvocationExpression(
            ShouldlyMemberAccess(receiver, SyntaxFactory.IdentifierName(shouldlyMethod)),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                new[]
                {
                    SyntaxFactory.Argument(expected.WithoutTrivia()),
                    SyntaxFactory.Argument(caseSensitive)
                },
                new[] { commaWithSpace })));
        return true;
    }

    private static ExpressionSyntax SuppressIfNullable(ExpressionSyntax expression, bool maybeNull)
    {
        if (!maybeNull)
            return expression;
        return SyntaxFactory.PostfixUnaryExpression(
            SyntaxKind.SuppressNullableWarningExpression,
            expression.WithoutTrivia());
    }

    private static bool HasNonExpressionTreeFeatures(LambdaExpressionSyntax lambda)
    {
        return lambda.DescendantNodes().Any(n =>
            n is ConditionalAccessExpressionSyntax
            || n is IsPatternExpressionSyntax
            || n is SwitchExpressionSyntax
            || n is RangeExpressionSyntax
            || n is InterpolatedStringExpressionSyntax
            || (n is AssignmentExpressionSyntax assign
                && !assign.IsKind(SyntaxKind.SimpleAssignmentExpression)));
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
        out ExpressionSyntax replacement)
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
        out ExpressionSyntax replacement)
    {
        replacement = SyntaxFactory.InvocationExpression(
            ShouldlyMemberAccess(actual, SyntaxFactory.IdentifierName(shouldlyMethod)));
        return true;
    }

    private bool RewriteGenericActualOnly(
        ExpressionSyntax actual,
        string shouldlyMethod,
        TypeArgumentListSyntax typeArguments,
        bool suppressNullableWarning,
        out ExpressionSyntax replacement)
    {
        var invocation = SyntaxFactory.InvocationExpression(
            ShouldlyMemberAccess(actual,
                SyntaxFactory.GenericName(SyntaxFactory.Identifier(shouldlyMethod), typeArguments)));

        replacement = suppressNullableWarning
            ? SyntaxFactory.PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, invocation)
            : invocation;
        return true;
    }

    private bool RewriteStaticGeneric(
        string typeName,
        string methodName,
        TypeArgumentListSyntax typeArguments,
        ExpressionSyntax argument,
        out ExpressionSyntax replacement)
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
            PostfixUnaryExpressionSyntax pue when pue.IsKind(SyntaxKind.SuppressNullableWarningExpression) => receiver,
            LiteralExpressionSyntax literal when !literal.IsKind(SyntaxKind.NullLiteralExpression) => receiver,
            _ => SyntaxFactory.ParenthesizedExpression(receiver)
        };
    }

    /// <summary>
    /// Semantic context computed up-front from the original (pre-rewrite) invocation node.
    /// Computed before <c>base.VisitInvocationExpression</c> descends, so the syntax tree
    /// is still the one the SemanticModel was bound against.
    /// </summary>
    private readonly record struct AssertContext(
        bool FirstArgIsNullableBool,
        bool FirstArgIsMaybeNull,
        bool FirstArgIsString,
        bool SecondArgIsMaybeNull,
        bool SecondArgIsString,
        bool ResultIsDiscarded);

    private static AssertContext ComputeContext(
        InvocationExpressionSyntax original,
        SemanticModel? semanticModel)
    {
        var arguments = original.ArgumentList.Arguments;
        var first = arguments.Count >= 1 ? arguments[0].Expression : null;
        var second = arguments.Count >= 2 ? arguments[1].Expression : null;

        return new AssertContext(
            FirstArgIsNullableBool: IsNullableBool(first, semanticModel),
            FirstArgIsMaybeNull: IsMaybeNull(first, semanticModel),
            FirstArgIsString: IsString(first, semanticModel),
            SecondArgIsMaybeNull: IsMaybeNull(second, semanticModel),
            SecondArgIsString: IsString(second, semanticModel),
            ResultIsDiscarded: original.Parent is ExpressionStatementSyntax);
    }

    private static bool IsString(ExpressionSyntax? expression, SemanticModel? semanticModel)
    {
        if (expression is null || semanticModel is null)
            return false;
        return semanticModel.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_String;
    }

    private static bool IsNullableBool(ExpressionSyntax? expression, SemanticModel? semanticModel)
    {
        if (expression is null || semanticModel is null)
            return false;
        var type = semanticModel.GetTypeInfo(expression).Type;
        return type is INamedTypeSymbol named
               && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
               && named.TypeArguments.Length == 1
               && named.TypeArguments[0].SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsMaybeNull(ExpressionSyntax? expression, SemanticModel? semanticModel)
    {
        if (expression is null || semanticModel is null)
            return false;
        return semanticModel.GetTypeInfo(expression).Nullability.FlowState == NullableFlowState.MaybeNull;
    }

    private sealed class ReplaceXunitAssertsRewriter(
        ReplaceXunitAssertsService service,
        SemanticModel? semanticModel) : CSharpSyntaxRewriter
    {
        public int ReplacementCount { get; private set; }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var context = ComputeContext(node, semanticModel);
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (!service.TryRewrite(visited, context, out var replacement))
                return visited;

            ReplacementCount++;
            return replacement.WithTriviaFrom(visited);
        }
    }

    private sealed class ReplaceXunitAssertsAnalyzer(
        string filePath,
        ReplaceXunitAssertsService service,
        SemanticModel? semanticModel)
        : CSharpSyntaxWalker
    {
        public List<ReplaceXunitAssertsEntry> Entries { get; } = [];

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var context = ComputeContext(node, semanticModel);
            if (service.TryRewrite(node, context, out var replacement))
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
