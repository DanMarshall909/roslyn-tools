using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal sealed record ConsoleLoggingOptions(
    string LoggerExpression,
    string InformationMethod,
    string WarningMethod,
    string ErrorMethod,
    ConsoleLoggingStream Stream,
    IReadOnlyList<string> ExcludePaths,
    bool InferLevel);

internal enum ConsoleLoggingStream
{
    All,
    Stdout,
    Stderr,
}

internal static class ConsoleLoggingCore
{
    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    internal static async Task<ConsoleLoggingReview> AnalyzeAsync(Solution solution, ConsoleLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var review = new ConsoleLoggingReview();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null || ShouldExclude(document.FilePath, options.ExcludePaths))
                    continue;

                var semanticModel = await document.GetSemanticModelAsync();
                var analyzer = new ConsoleLoggingAnalyzer(options, document.FilePath ?? document.Name, semanticModel);
                analyzer.Visit(root);
                review.AddRange(analyzer.Entries);
            }
        }

        return review;
    }

    internal static async Task RunAsync(Solution solution, ConsoleLoggingOptions options, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(options);

        var originalSolution = solution;
        var newSolution = await ApplyAsync(solution, options);
        await SolutionWriter.ApplyChangesAsync(originalSolution, newSolution, dryRun);
    }

    internal static async Task<Solution> ApplyAsync(Solution solution, ConsoleLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                if (ShouldExclude(document.FilePath, options.ExcludePaths))
                    continue;

                var rewriter = new ConsoleLoggingRewriter(options);
                var newRoot = rewriter.Visit(root);
                if (newRoot is not null && !ReferenceEquals(root, newRoot))
                    solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
            }
        }

        return solution;
    }

    private static bool ShouldExclude(string? filePath, IReadOnlyList<string> excludePaths)
    {
        if (string.IsNullOrWhiteSpace(filePath) || excludePaths.Count == 0)
            return false;

        var normalizedPath = filePath.Replace('\\', '/');
        return excludePaths.Any(exclude =>
            normalizedPath.Contains(
                exclude.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ConsoleLoggingRewriter(ConsoleLoggingOptions options) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (!TryGetConsoleKind(visited, out var isError))
                return visited;

            if (!ShouldRewriteStream(isError))
                return visited;

            var converted = BuildMessage(visited.ArgumentList);
            var level = options.InferLevel
                ? InferLevel(converted.Template, isError)
                : isError ? ConsoleLoggingLevel.Error : ConsoleLoggingLevel.Information;
            var methodName = GetMethodName(options, level);

            var replacement = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseExpression(options.LoggerExpression),
                    SyntaxFactory.IdentifierName(methodName)),
                BuildArgumentList(converted));

            return replacement.WithTriviaFrom(visited);
        }

        private bool ShouldRewriteStream(bool isError) =>
            options.Stream switch
            {
                ConsoleLoggingStream.Stdout => !isError,
                ConsoleLoggingStream.Stderr => isError,
                _ => true,
            };

        internal static bool TryGetConsoleKind(InvocationExpressionSyntax invocation, out bool isError)
        {
            isError = false;

            if (invocation.Expression is not MemberAccessExpressionSyntax methodAccess)
                return false;

            var methodName = methodAccess.Name.Identifier.ValueText;
            if (methodName is not ("WriteLine" or "Write"))
                return false;

            return TryGetConsoleTarget(methodAccess.Expression, out isError);
        }

        private static bool TryGetConsoleTarget(ExpressionSyntax expression, out bool isError)
        {
            isError = false;

            if (expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == "Console")
            {
                return true;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Name.Identifier.ValueText == "Error"
                    && TryGetConsoleTarget(memberAccess.Expression, out _))
                {
                    isError = true;
                    return true;
                }

                if (memberAccess.Name.Identifier.ValueText == "Console"
                    && IsSystemExpression(memberAccess.Expression))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSystemExpression(ExpressionSyntax expression) =>
            expression is IdentifierNameSyntax identifier
            && identifier.Identifier.ValueText == "System";

        private static string GetMethodName(ConsoleLoggingOptions options, ConsoleLoggingLevel level) =>
            level switch
            {
                ConsoleLoggingLevel.Warning => options.WarningMethod,
                ConsoleLoggingLevel.Error => options.ErrorMethod,
                _ => options.InformationMethod,
            };

        internal static ConsoleLoggingLevel InferLevel(string template, bool isError)
        {
            if (!isError)
                return ConsoleLoggingLevel.Information;

            var trimmed = template.TrimStart();
            if (trimmed.StartsWith("warning", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("[warning]", StringComparison.OrdinalIgnoreCase))
            {
                return ConsoleLoggingLevel.Warning;
            }

            if (trimmed.StartsWith("error", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("[error]", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("cannot ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("failed ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("failed:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("fatal", StringComparison.OrdinalIgnoreCase))
            {
                return ConsoleLoggingLevel.Error;
            }

            return ConsoleLoggingLevel.Information;
        }

        internal static ConvertedMessage BuildMessage(ArgumentListSyntax original)
        {
            if (original.Arguments.Count == 0)
                return new ConvertedMessage("", []);

            var firstArg = original.Arguments[0];
            var remainingArgs = original.Arguments.Skip(1).ToList();
            return ConvertMessageExpression(firstArg.Expression, remainingArgs);
        }

        private static ArgumentListSyntax BuildArgumentList(ConvertedMessage converted)
        {
            var args = new List<ArgumentSyntax>
            {
                SyntaxFactory.Argument(StringLiteral(converted.Template))
            };

            args.AddRange(converted.Arguments.Select(SyntaxFactory.Argument));
            return SyntaxFactory.ArgumentList(CreateArgumentList(args));
        }

        private static SeparatedSyntaxList<ArgumentSyntax> CreateArgumentList(List<ArgumentSyntax> args)
        {
            if (args.Count == 0)
                return SyntaxFactory.SeparatedList<ArgumentSyntax>();

            var nodesAndSeparators = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < args.Count; i++)
            {
                nodesAndSeparators.Add(args[i]);
                if (i < args.Count - 1)
                {
                    nodesAndSeparators.Add(
                        SyntaxFactory.Token(SyntaxKind.CommaToken)
                            .WithTrailingTrivia(SyntaxFactory.Space));
                }
            }

            return SyntaxFactory.SeparatedList<ArgumentSyntax>(nodesAndSeparators);
        }

        private static ConvertedMessage ConvertMessageExpression(
            ExpressionSyntax expression,
            IReadOnlyList<ArgumentSyntax> remainingArgs)
        {
            if (expression is InterpolatedStringExpressionSyntax interpolated)
                return ConvertInterpolatedString(interpolated);

            if (TryConvertConcatenation(expression, out var concatenated))
                return concatenated;

            if (expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var template = literal.Token.ValueText;
                var args = remainingArgs.Select(a => a.Expression).ToList();
                if (args.Count > 0)
                    template = ConvertCompositeFormat(template, args);

                return new ConvertedMessage(template, args);
            }

            return new ConvertedMessage("{Value}", [expression.WithoutTrivia()]);
        }

        private static ConvertedMessage ConvertInterpolatedString(InterpolatedStringExpressionSyntax interpolated)
        {
            var template = new StringBuilder();
            var args = new List<ExpressionSyntax>();

            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    template.Append(EscapeTemplateText(text.TextToken.ValueText));
                    continue;
                }

                if (content is InterpolationSyntax interpolation)
                {
                    var name = CreatePlaceholderName(interpolation.Expression, args.Count);
                    template.Append('{').Append(name);
                    if (interpolation.FormatClause is not null)
                        template.Append(':').Append(interpolation.FormatClause.FormatStringToken.ValueText);
                    template.Append('}');
                    args.Add(interpolation.Expression.WithoutTrivia());
                }
            }

            return new ConvertedMessage(template.ToString(), args);
        }

        private static bool TryConvertConcatenation(ExpressionSyntax expression, out ConvertedMessage converted)
        {
            var parts = new List<ExpressionSyntax>();
            FlattenAdditions(expression, parts);

            var hasStringPart = parts.Any(p => IsStringLiteral(p) || p is InterpolatedStringExpressionSyntax);
            if (parts.Count <= 1 || !hasStringPart)
            {
                converted = default;
                return false;
            }

            var template = new StringBuilder();
            var args = new List<ExpressionSyntax>();

            foreach (var part in parts)
            {
                if (part is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    template.Append(EscapeTemplateText(literal.Token.ValueText));
                    continue;
                }

                if (part is InterpolatedStringExpressionSyntax interpolated)
                {
                    var inner = ConvertInterpolatedString(interpolated);
                    template.Append(inner.Template);
                    args.AddRange(inner.Arguments);
                    continue;
                }

                var name = CreatePlaceholderName(part, args.Count);
                template.Append('{').Append(name).Append('}');
                args.Add(part.WithoutTrivia());
            }

            converted = new ConvertedMessage(template.ToString(), args);
            return true;
        }

        private static void FlattenAdditions(ExpressionSyntax expression, List<ExpressionSyntax> parts)
        {
            if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.AddExpression))
            {
                FlattenAdditions(binary.Left, parts);
                FlattenAdditions(binary.Right, parts);
                return;
            }

            parts.Add(expression);
        }

        private static bool IsStringLiteral(ExpressionSyntax expression) =>
            expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression);

        private static string ConvertCompositeFormat(string template, List<ExpressionSyntax> args)
        {
            return Regex.Replace(
                template,
                @"\{(?<index>\d+)(?<format>:[^{}]+)?\}",
                match =>
                {
                    var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
                    if (index < 0 || index >= args.Count)
                        return match.Value;

                    var name = CreatePlaceholderName(args[index], index);
                    return "{" + name + match.Groups["format"].Value + "}";
                });
        }

        private static string CreatePlaceholderName(ExpressionSyntax expression, int index)
        {
            var raw = expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax memberAccess => CreateMemberAccessPlaceholderName(memberAccess),
                InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberAccessExpressionSyntax memberAccess
                    => CreateInvocationPlaceholderName(memberAccess),
                InvocationExpressionSyntax invocation
                    when invocation.Expression is IdentifierNameSyntax identifier
                    => identifier.Identifier.ValueText,
                _ => $"Value{index + 1}"
            };

            var builder = new StringBuilder();
            foreach (var ch in raw)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    builder.Append(ch);
            }

            if (builder.Length == 0)
                return $"Value{index + 1}";

            if (char.IsDigit(builder[0]))
                builder.Insert(0, "Value");

            builder[0] = char.ToUpperInvariant(builder[0]);
            return builder.ToString();
        }

        private static string CreateMemberAccessPlaceholderName(MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.ValueText;
            if (memberName == "Message"
                && memberAccess.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText is "ex" or "exception")
            {
                return "ErrorMessage";
            }

            return memberName;
        }

        private static string CreateInvocationPlaceholderName(MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName == "ToDisplayString")
                return "Symbol";

            if (methodName == "GetFileName"
                && memberAccess.Expression.ToString() == "Path")
            {
                return "FileName";
            }

            if (methodName == "Join"
                && memberAccess.Expression.ToString() == "string")
            {
                return "JoinedValues";
            }

            return methodName;
        }

        private static string EscapeTemplateText(string text) =>
            text.Replace("{", "{{", StringComparison.Ordinal)
                .Replace("}", "}}", StringComparison.Ordinal);

        private static LiteralExpressionSyntax StringLiteral(string value) =>
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(value));
    }

    private readonly record struct ConvertedMessage(string Template, List<ExpressionSyntax> Arguments);

    private sealed class ConsoleLoggingAnalyzer(
        ConsoleLoggingOptions options,
        string filePath,
        SemanticModel? semanticModel) : CSharpSyntaxWalker
    {
        public List<ConsoleLoggingReviewEntry> Entries { get; } = [];

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (ConsoleLoggingRewriter.TryGetConsoleKind(node, out var isError)
                && ShouldRewriteStream(isError))
            {
                var message = ConsoleLoggingRewriter.BuildMessage(node.ArgumentList);
                var level = options.InferLevel
                    ? ConsoleLoggingRewriter.InferLevel(message.Template, isError)
                    : isError ? ConsoleLoggingLevel.Error : ConsoleLoggingLevel.Information;

                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var targetType = ResolveContainingType(node);
                Entries.Add(new ConsoleLoggingReviewEntry(
                    filePath,
                    line,
                    isError ? "stderr" : "stdout",
                    level,
                    message.Template,
                    message.Template.Contains("{Value}", StringComparison.Ordinal),
                    targetType?.Name,
                    targetType?.ToDisplayString(TypeDisplayFormat)));
            }

            base.VisitInvocationExpression(node);
        }

        private bool ShouldRewriteStream(bool isError) =>
            options.Stream switch
            {
                ConsoleLoggingStream.Stdout => !isError,
                ConsoleLoggingStream.Stderr => isError,
                _ => true,
            };

        private INamedTypeSymbol? ResolveContainingType(SyntaxNode node)
        {
            var classDeclaration = node.AncestorsAndSelf()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();

            if (classDeclaration is null)
                return null;

            return semanticModel?.GetDeclaredSymbol(classDeclaration);
        }
    }
}

internal enum ConsoleLoggingLevel
{
    Information,
    Warning,
    Error,
}

internal sealed record ConsoleLoggingReviewEntry(
    string FilePath,
    int Line,
    string Stream,
    ConsoleLoggingLevel Level,
    string Template,
    bool NeedsReview,
    string? TypeName,
    string? TypeFullName);

internal sealed class ConsoleLoggingReview
{
    private readonly List<ConsoleLoggingReviewEntry> _entries = [];

    internal IReadOnlyList<ConsoleLoggingReviewEntry> Entries => _entries;

    internal void AddRange(IEnumerable<ConsoleLoggingReviewEntry> entries) =>
        _entries.AddRange(entries);
}
