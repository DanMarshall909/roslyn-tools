using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class ConvertToRecordCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to convert to a record") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("convert-to-record", "Convert a class to a record type with positional parameters");
        cmd.AddAlias("ctr");
        cmd.Add(typeOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, solution, dryRun) =>
        {
            await ExecuteAsync(type, solution, dryRun);
        }, typeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = sln;

            // Find the class symbol
            INamedTypeSymbol? classSymbol = null;
            DocumentId? docId = null;

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
                        classSymbol = namedType;
                        break;
                    }
                }

                if (classSymbol != null) break;
            }

            if (classSymbol == null)
                throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

            if (classSymbol.IsRecord)
            {
                Console.Error.WriteLine($"'{typeName}' is already a record.");
                return;
            }

            // Locate the syntax reference
            var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
                throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

            var classSyntax = (ClassDeclarationSyntax)await syntaxRef.GetSyntaxAsync();

            // Find the document
            var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
                throw new InvalidOperationException($"No source location found for '{typeName}'.");

            foreach (var project in sln.Projects)
            {
                foreach (var doc in project.Documents)
                {
                    if (doc.FilePath != null &&
                        string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        docId = doc.Id;
                        break;
                    }
                }
                if (docId != null) break;
            }

            if (docId == null)
                throw new InvalidOperationException("Cannot locate source document for the target class.");

            // Partition members: init-only props (become positional params) vs remaining
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

            // Members to remove: init-only props + Equals/GetHashCode/==/!= overrides
            var membersToRemove = new HashSet<MemberDeclarationSyntax>(
                initProps.Cast<MemberDeclarationSyntax>());

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

            var remainingMembers = allMembers
                .Where(m => !membersToRemove.Contains(m))
                .ToList();

            // Build positional parameter list from init-only props
            var parameters = BuildParameterList(initProps);

            // Build the record declaration
            var recordDecl = BuildRecordDeclaration(classSyntax, parameters, remainingMembers);

            // Replace the class node in the document root
            var document = sln.GetDocument(docId)!;
            var root = (await document.GetSyntaxRootAsync())!;
            var oldNode = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax;
            if (oldNode == null)
                throw new InvalidOperationException("Cannot locate class syntax node in document.");

            root = root.ReplaceNode(oldNode, recordDecl);
            sln = sln.WithDocumentSyntaxRoot(docId, root);

            Console.Error.WriteLine($"Converting '{typeName}' to a positional record...");
            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
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
        // record keyword token
        var recordKeyword = SyntaxFactory.Token(SyntaxKind.RecordKeyword)
            .WithLeadingTrivia(classSyntax.Keyword.LeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Identifier
        var identifier = classSyntax.Identifier.WithoutTrivia();

        // Parameter list  — record Foo(int X, int Y)
        var paramList = SyntaxFactory.ParameterList(
            SyntaxFactory.Token(SyntaxKind.OpenParenToken),
            parameters,
            SyntaxFactory.Token(SyntaxKind.CloseParenToken));

        // Build the member list (only add braces when there are remaining members)
        SyntaxToken openBrace;
        SyntaxToken closeBrace;
        SyntaxList<MemberDeclarationSyntax> members;

        if (remainingMembers.Count > 0)
        {
            openBrace = classSyntax.OpenBraceToken;
            closeBrace = classSyntax.CloseBraceToken;
            members = SyntaxFactory.List(remainingMembers);
        }
        else
        {
            // Emit empty braces with a semicolon alternative — but for simplicity use empty body
            openBrace = SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(SyntaxFactory.Space);
            closeBrace = SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(classSyntax.CloseBraceToken.LeadingTrivia);
            members = SyntaxFactory.List<MemberDeclarationSyntax>();
        }

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
}
