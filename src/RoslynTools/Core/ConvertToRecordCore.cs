using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class ConvertToRecordCore
{
    internal static async Task RunAsync(Solution sln, string typeName, bool dryRun)
    {
        var originalSolution = sln;

        var classSymbol = await FindClassSymbolAsync(sln, typeName);

        if (classSymbol.IsRecord)
        {
            Console.Error.WriteLine($"'{typeName}' is already a record.");
            return;
        }

        var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()
            ?? throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

        var classSyntax = (ClassDeclarationSyntax)await syntaxRef.GetSyntaxAsync();

        var docId = FindDocumentId(sln, classSymbol)
            ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

        var (initProps, remainingMembers) = CollectMembers(typeName, classSyntax);

        var parameters = BuildParameterList(initProps);
        var recordDecl = BuildRecordDeclaration(classSyntax, parameters, remainingMembers);

        sln = await ApplyRecordReplacementAsync(sln, docId, syntaxRef, recordDecl);

        Console.Error.WriteLine($"Converting '{typeName}' to a positional record...");
        await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
    }

    private static async Task<INamedTypeSymbol> FindClassSymbolAsync(Solution sln, string typeName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
            {
                if (symbol is INamedTypeSymbol namedType &&
                    namedType.TypeKind == TypeKind.Class &&
                    namedType.Name == typeName)
                {
                    return namedType;
                }
            }
        }

        throw new InvalidOperationException($"Class '{typeName}' not found in solution.");
    }

    private static DocumentId? FindDocumentId(Solution sln, INamedTypeSymbol classSymbol)
    {
        var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null) return null;

        foreach (var project in sln.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return doc.Id;
                }
            }
        }

        return null;
    }

    private static (List<PropertyDeclarationSyntax> InitProps, List<MemberDeclarationSyntax> RemainingMembers)
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
            Console.Error.WriteLine(
                $"WARNING: '{typeName}' has mutable auto-properties ({names}). " +
                "They will remain as regular properties in the record body.");
        }

        var membersToRemove = BuildMembersToRemove(allMembers, initProps);
        var remainingMembers = allMembers.Where(m => !membersToRemove.Contains(m)).ToList();
        return (initProps, remainingMembers);
    }

    private static HashSet<MemberDeclarationSyntax> BuildMembersToRemove(
        SyntaxList<MemberDeclarationSyntax> allMembers,
        List<PropertyDeclarationSyntax> initProps)
    {
        var membersToRemove = new HashSet<MemberDeclarationSyntax>(initProps.Cast<MemberDeclarationSyntax>());

        foreach (var member in allMembers)
        {
            if (member is MethodDeclarationSyntax method)
            {
                if ((method.Identifier.Text == "Equals" || method.Identifier.Text == "GetHashCode") &&
                    method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
                {
                    membersToRemove.Add(member);
                }
            }
            else if (member is OperatorDeclarationSyntax op)
            {
                var kind = op.OperatorToken.Kind();
                if (kind == SyntaxKind.EqualsEqualsToken || kind == SyntaxKind.ExclamationEqualsToken)
                    membersToRemove.Add(member);
            }
        }

        return membersToRemove;
    }

    private static bool IsInitOnlyAutoProperty(PropertyDeclarationSyntax prop)
    {
        if (prop.AccessorList == null) return false;
        var accessors = prop.AccessorList.Accessors;
        if (accessors.Count != 2) return false;
        return accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration) && a.Body == null && a.ExpressionBody == null) &&
               accessors.Any(a => a.IsKind(SyntaxKind.InitAccessorDeclaration) && a.Body == null && a.ExpressionBody == null);
    }

    private static bool IsMutableAutoProperty(PropertyDeclarationSyntax prop)
    {
        if (prop.AccessorList == null) return false;
        var accessors = prop.AccessorList.Accessors;
        return accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) && a.Body == null && a.ExpressionBody == null);
    }

    private static SeparatedSyntaxList<ParameterSyntax> BuildParameterList(
        List<PropertyDeclarationSyntax> initProps)
    {
        var parameters = initProps.Select(prop =>
            SyntaxFactory.Parameter(
                attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                modifiers: SyntaxFactory.TokenList(),
                type: prop.Type.WithoutTrivia()
                    .WithTrailingTrivia(SyntaxFactory.Space),
                identifier: prop.Identifier.WithoutTrivia(),
                @default: null));

        return SyntaxFactory.SeparatedList(parameters);
    }

    private static RecordDeclarationSyntax BuildRecordDeclaration(
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
                SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithLeadingTrivia(classSyntax.CloseBraceToken.LeadingTrivia),
                SyntaxFactory.List<MemberDeclarationSyntax>()
              );

        return SyntaxFactory.RecordDeclaration(
            kind: SyntaxKind.RecordDeclaration,
            attributeLists: classSyntax.AttributeLists,
            modifiers: classSyntax.Modifiers,
            keyword: recordKeyword,
            classOrStructKeyword: default,
            identifier: identifier.WithTrailingTrivia(SyntaxFactory.TriviaList()),
            typeParameterList: classSyntax.TypeParameterList,
            parameterList: paramList,
            baseList: classSyntax.BaseList,
            constraintClauses: classSyntax.ConstraintClauses,
            openBraceToken: openBrace,
            members: members,
            closeBraceToken: closeBrace,
            semicolonToken: default);
    }

    private static async Task<Solution> ApplyRecordReplacementAsync(
        Solution sln,
        DocumentId docId,
        Microsoft.CodeAnalysis.SyntaxReference syntaxRef,
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
