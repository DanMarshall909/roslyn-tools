using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record MagicStringOccurrence(
    string Value,
    string FilePath,
    int Line,
    string? TypeName,
    string? MemberName);

internal sealed record MagicStringGroup(
    string Value,
    int Count,
    IReadOnlyList<MagicStringOccurrence> Occurrences);
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class MagicStringService
{
    internal async Task<IReadOnlyList<MagicStringGroup>> AnalyzeAsync(
        Solution solution,
        int minLength,
        int minOccurrences)
    {
        var occurrences = new List<MagicStringOccurrence>();
        var rtIgnore = RtIgnore.ForSolution(solution);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (rtIgnore.IsIgnored(document.FilePath, "find-magic-strings")
                    || IsGeneratedOrIntermediate(document.FilePath))
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    if (!IsCandidateLiteral(literal, minLength))
                        continue;

                    var line = literal.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    occurrences.Add(new MagicStringOccurrence(
                        Value: literal.Token.ValueText,
                        FilePath: document.FilePath ?? document.Name,
                        Line: line,
                        TypeName: literal.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                        MemberName: GetMemberName(literal)));
                }
            }
        }

        return occurrences
            .GroupBy(o => o.Value, StringComparer.Ordinal)
            .Where(g => g.Count() >= minOccurrences)
            .Select(g => new MagicStringGroup(g.Key, g.Count(), g.ToList()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Value, StringComparer.Ordinal)
            .ToList();
    }

    internal async Task ExtractConstantAsync(
        Solution solution,
        string filePath,
        int line,
        string constantName,
        bool dryRun)
    {
        var originalSolution = solution;
        var document = FindDocument(solution, filePath);
        var root = await document.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException($"Cannot read syntax root for '{document.FilePath}'.");

        var literal = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression)
                && l.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == line)
            ?? throw new InvalidOperationException($"No string literal found at line {line} in '{filePath}'.");

        if (!IsCandidateLiteral(literal, minLength: 1))
            throw new InvalidOperationException($"String literal at line {line} is not a candidate for extraction.");

        var containingType = literal.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("Magic string extraction requires the literal to be inside a type declaration.");

        if (containingType.Members
            .OfType<FieldDeclarationSyntax>()
            .Any(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == constantName)))
        {
            throw new InvalidOperationException($"Type '{containingType.Identifier.Text}' already contains a member named '{constantName}'.");
        }

        var value = literal.Token.ValueText;
        var rewrittenType = ReplaceMatchingLiterals(containingType, value, constantName);
        rewrittenType = InsertConstant(rewrittenType, constantName, value);

        var newRoot = root.ReplaceNode(containingType, rewrittenType);
        solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private TypeDeclarationSyntax ReplaceMatchingLiterals(
        TypeDeclarationSyntax containingType,
        string value,
        string constantName)
    {
        var literals = containingType.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)
                && l.Token.ValueText == value
                && !IsInConstStringDeclaration(l))
            .ToList();

        return containingType.ReplaceNodes(
            literals,
            (original, _) => SyntaxFactory.IdentifierName(constantName)
                .WithTriviaFrom(original));
    }

    private TypeDeclarationSyntax InsertConstant(
        TypeDeclarationSyntax containingType,
        string constantName,
        string value)
    {
        var literal = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(value));
        var variable = SyntaxFactory.VariableDeclarator(constantName)
            .WithInitializer(SyntaxFactory.EqualsValueClause(literal));
        var declaration = SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(variable));
        var field = SyntaxFactory.FieldDeclaration(declaration)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.ConstKeyword)))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        var insertIndex = containingType.Members.TakeWhile(m => m is FieldDeclarationSyntax).Count();
        return containingType.WithMembers(containingType.Members.Insert(insertIndex, field));
    }

    private bool IsCandidateLiteral(LiteralExpressionSyntax literal, int minLength)
    {
        if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
            return false;

        var value = literal.Token.ValueText;
        return value.Length >= minLength
            && !string.IsNullOrWhiteSpace(value)
            && !IsInConstStringDeclaration(literal)
            && !IsInUsingDirective(literal);
    }

    private bool IsInConstStringDeclaration(SyntaxNode node) =>
        node.Ancestors().OfType<FieldDeclarationSyntax>().Any(field =>
            field.Modifiers.Any(SyntaxKind.ConstKeyword)
            && field.Declaration.Type is PredefinedTypeSyntax predefined
            && predefined.Keyword.IsKind(SyntaxKind.StringKeyword));

    private bool IsInUsingDirective(SyntaxNode node) =>
        node.Ancestors().OfType<UsingDirectiveSyntax>().Any();

    private Document FindDocument(Solution solution, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"File '{filePath}' not found in solution.");
    }

    private string? GetMemberName(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax method)
                return method.Identifier.Text;
            if (ancestor is ConstructorDeclarationSyntax constructor)
                return constructor.Identifier.Text;
            if (ancestor is PropertyDeclarationSyntax property)
                return property.Identifier.Text;
            if (ancestor is FieldDeclarationSyntax field)
                return string.Join(", ", field.Declaration.Variables.Select(v => v.Identifier.Text));
        }

        return null;
    }

    private bool IsGeneratedOrIntermediate(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }
}
