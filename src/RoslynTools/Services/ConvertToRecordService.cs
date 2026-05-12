using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Converts a method into a record type.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ConvertToRecordService : IRtService
{
    private readonly ILogger<ConvertToRecordService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    private readonly IDocumentLocatorService _documents;

    public ConvertToRecordService()
        : this(new RtConsoleLogger<ConvertToRecordService>(), new SolutionChangeWriter())
    {
    }

    public ConvertToRecordService(ILogger<ConvertToRecordService> logger, ISolutionChangeWriter solutionChangeWriter,
        IDocumentLocatorService? documents = null)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
        _documents = documents ?? new DocumentLocatorService();
    }

    /// <summary>
    /// Runs the conversion process for a method into a record.
    /// </summary>
    internal async Task RunAsync(Solution sln, string typeName, bool dryRun)
    {
        var originalSolution = sln;

        var classSymbol = await FindClassSymbolAsync(sln, typeName);

        if (classSymbol.IsRecord)
        {
            _logger.RtInfo("'{TypeName}' is already a record.", typeName);
            return;
        }

        var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                        ?? throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

        var classSyntax = (ClassDeclarationSyntax)await syntaxRef.GetSyntaxAsync();

        var docId = _documents.FindDocumentId(sln, classSymbol)
                    ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

        var (initProps, remainingMembers) = CollectMembers(typeName, classSyntax);

        var parameters = BuildParameterList(initProps);
        var recordDecl = BuildRecordDeclaration(classSyntax, parameters, remainingMembers);

        sln = await ApplyRecordReplacementAsync(sln, docId, syntaxRef, recordDecl);

        _logger.RtInfo("Converting '{TypeName}' to a positional record...", typeName);
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
    }

    private async Task<INamedTypeSymbol> FindClassSymbolAsync(Solution sln, string typeName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
                if (symbol is INamedTypeSymbol namedType &&
                    namedType.TypeKind == TypeKind.Class &&
                    namedType.Name == typeName)
                    return namedType;
        }

        throw new InvalidOperationException($"Class '{typeName}' not found in solution.");
    }

    private (List<PropertyDeclarationSyntax> InitProps, List<MemberDeclarationSyntax> RemainingMembers)
        CollectMembers(string typeName, ClassDeclarationSyntax classSyntax)
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

        if (mutableProps.Count > 0)
        {
            var names = string.Join(", ", mutableProps.Select(p => p.Identifier.Text));
            _logger.RtWarning(
                "'{TypeName}' has mutable auto-properties ({PropertyNames}). They will remain as regular properties in the record body.",
                typeName, names);
        }

        var membersToRemove = BuildMembersToRemove(allMembers, initProps);
        var remainingMembers = allMembers.Where(m => !membersToRemove.Contains(m)).ToList();
        return (initProps, remainingMembers);
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
        RecordDeclarationSyntax recordDecl)
    {
        var document = sln.GetDocument(docId)!;
        var root = (await document.GetSyntaxRootAsync())!;
        var oldNode = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax
                      ?? throw new InvalidOperationException("Cannot locate class syntax node in document.");

        root = root.ReplaceNode(oldNode, recordDecl);
        return sln.WithDocumentSyntaxRoot(docId, root);
    }
}
