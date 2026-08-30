using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Converts a method into a record type.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ConvertToRecordService : IRtService
{
    private readonly IDocumentLocatorService _documents;
    private readonly ISymbolResolutionService _symbols;

    public ConvertToRecordService(
        ISymbolResolutionService symbols,
        IDocumentLocatorService documents)
    {
        _symbols = symbols;
        _documents = documents;
    }

    internal sealed record Preparation(
        Solution Solution,
        bool Changed,
        IReadOnlyList<MutationDiagnostic> Diagnostics);

    /// <summary>
    /// Runs the conversion process for a method into a record.
    /// </summary>
    internal async Task<Preparation> PrepareAsync(
        Solution sln,
        string typeName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var symbol = await _symbols.FindSymbolAsync(sln, typeName, "type");
        if (symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } classSymbol)
            throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

        if (classSymbol.IsRecord)
            return new Preparation(
                sln,
                false,
                [new MutationDiagnostic(
                    MutationDiagnosticSeverity.Information,
                    $"'{typeName}' is already a record.")]);

        var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                        ?? throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

        var classSyntax = (ClassDeclarationSyntax)await syntaxRef.GetSyntaxAsync(cancellationToken);

        var docId = _documents.FindDocumentId(sln, classSymbol)
                    ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

        var (initProps, remainingMembers, mutablePropertyNames) = CollectMembers(classSyntax);

        var parameters = BuildParameterList(initProps);
        var recordDecl = BuildRecordDeclaration(classSyntax, parameters, remainingMembers);

        sln = await ApplyRecordReplacementAsync(
            sln,
            docId,
            syntaxRef,
            recordDecl,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = mutablePropertyNames.Count == 0
            ? []
            : new MutationDiagnostic[]
            {
                new(
                    MutationDiagnosticSeverity.Warning,
                    $"'{typeName}' has mutable auto-properties ({string.Join(", ", mutablePropertyNames)}). " +
                    "They will remain as regular properties in the record body.")
            };
        return new Preparation(sln, true, diagnostics);
    }

    private (List<PropertyDeclarationSyntax> InitProps,
        List<MemberDeclarationSyntax> RemainingMembers,
        List<string> MutablePropertyNames) CollectMembers(ClassDeclarationSyntax classSyntax)
    {
        var allMembers = classSyntax.Members;

        var initProps = allMembers
            .OfType<PropertyDeclarationSyntax>()
            .Where(IsInitOnlyAutoProperty)
            .ToList();

        var mutableProps = allMembers
            .OfType<PropertyDeclarationSyntax>()
            .Where(p => !IsInitOnlyAutoProperty(p) && IsMutableAutoProperty(p))
            .ToList();

        var membersToRemove = BuildMembersToRemove(allMembers, initProps);
        var remainingMembers = allMembers.Where(m => !membersToRemove.Contains(m)).ToList();
        return (
            initProps,
            remainingMembers,
            mutableProps.Select(property => property.Identifier.Text).ToList());
    }

    private HashSet<MemberDeclarationSyntax> BuildMembersToRemove(
        SyntaxList<MemberDeclarationSyntax> allMembers,
        List<PropertyDeclarationSyntax> initProps)
    {
        var membersToRemove = new HashSet<MemberDeclarationSyntax>(initProps);

        foreach (var member in allMembers)
            if (member is MethodDeclarationSyntax method)
            {
                if ((method.Identifier.Text == "Equals" || method.Identifier.Text == "GetHashCode") &&
                    method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
                    membersToRemove.Add(member);
            }
            else if (member is OperatorDeclarationSyntax op)
            {
                var kind = op.OperatorToken.Kind();
                if (kind == SyntaxKind.EqualsEqualsToken || kind == SyntaxKind.ExclamationEqualsToken)
                    membersToRemove.Add(member);
            }

        return membersToRemove;
    }

    private bool IsInitOnlyAutoProperty(PropertyDeclarationSyntax prop)
    {
        if (prop.AccessorList == null) return false;
        var accessors = prop.AccessorList.Accessors;
        if (accessors.Count != 2) return false;
        return accessors.Any(a =>
                   a.IsKind(SyntaxKind.GetAccessorDeclaration) && a.Body == null && a.ExpressionBody == null) &&
               accessors.Any(a =>
                   a.IsKind(SyntaxKind.InitAccessorDeclaration) && a.Body == null && a.ExpressionBody == null);
    }

    private bool IsMutableAutoProperty(PropertyDeclarationSyntax prop)
    {
        if (prop.AccessorList == null) return false;
        var accessors = prop.AccessorList.Accessors;
        return accessors.Any(a =>
            a.IsKind(SyntaxKind.SetAccessorDeclaration) && a.Body == null && a.ExpressionBody == null);
    }

    private SeparatedSyntaxList<ParameterSyntax> BuildParameterList(
        List<PropertyDeclarationSyntax> initProps)
    {
        var parameters = initProps.Select(prop =>
            SyntaxFactory.Parameter(
                SyntaxFactory.List<AttributeListSyntax>(),
                SyntaxFactory.TokenList(),
                prop.Type.WithoutTrivia()
                    .WithTrailingTrivia(SyntaxFactory.Space),
                prop.Identifier.WithoutTrivia(),
                null));

        return SyntaxFactory.SeparatedList(parameters);
    }

    private RecordDeclarationSyntax BuildRecordDeclaration(
        ClassDeclarationSyntax classSyntax,
        SeparatedSyntaxList<ParameterSyntax> parameters,
        List<MemberDeclarationSyntax> remainingMembers)
    {
        var recordKeyword = SyntaxFactory.Token(SyntaxKind.RecordKeyword)
            .WithLeadingTrivia(classSyntax.Keyword.LeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var identifier = classSyntax.Identifier.WithoutTrivia();

        var paramList = SyntaxFactory.ParameterList(
            SyntaxFactory.Token(SyntaxKind.OpenParenToken),
            parameters,
            SyntaxFactory.Token(SyntaxKind.CloseParenToken));

        var (openBrace, closeBrace, members) = remainingMembers.Count > 0
            ? (classSyntax.OpenBraceToken, classSyntax.CloseBraceToken, SyntaxFactory.List(remainingMembers))
            : (
                SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(SyntaxFactory.Space),
                SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithLeadingTrivia(classSyntax.CloseBraceToken.LeadingTrivia),
                SyntaxFactory.List<MemberDeclarationSyntax>()
            );

        return SyntaxFactory.RecordDeclaration(
            SyntaxKind.RecordDeclaration,
            classSyntax.AttributeLists,
            classSyntax.Modifiers,
            recordKeyword,
            default,
            identifier.WithTrailingTrivia(SyntaxFactory.TriviaList()),
            classSyntax.TypeParameterList,
            paramList,
            classSyntax.BaseList,
            classSyntax.ConstraintClauses,
            openBrace,
            members,
            closeBrace,
            default);
    }

    private async Task<Solution> ApplyRecordReplacementAsync(
        Solution sln,
        DocumentId docId,
        SyntaxReference syntaxRef,
        RecordDeclarationSyntax recordDecl,
        CancellationToken cancellationToken)
    {
        var document = sln.GetDocument(docId)!;
        var root = (await document.GetSyntaxRootAsync(cancellationToken))!;
        var oldNode = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax
                      ?? throw new InvalidOperationException("Cannot locate class syntax node in document.");

        root = root.ReplaceNode(oldNode, recordDecl);
        return sln.WithDocumentSyntaxRoot(docId, root);
    }
}
