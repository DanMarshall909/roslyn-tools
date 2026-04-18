using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class MakeAsyncCommand : IRtCommand
{
    private MakeAsyncCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "make-async",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["ma"],
        WhenToUse: "Converting a synchronous method to async Task<T> and updating its direct call sites with await, without manually hunting every invocation. Use --no-rename to skip the Async suffix when the call sites already have naming that makes async clear.",
        Examples:
        [
            "rt make-async --type BookingService --method Save --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt ma --type EmailSender --method Send --no-rename --dry-run",
            "rt make-async --type HeroApiClient --method FetchAvailability --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class containing the method") { IsRequired = true };
        var methodOpt = new Option<string>("--method", "Method name to make async") { IsRequired = true };
        var noRenameOpt = new Option<bool>("--no-rename", "Skip renaming to Async suffix");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("make-async", "Convert a synchronous method to async Task<T> and update direct callers");
        cmd.AddAlias("ma");
        cmd.AddAll(typeOpt, methodOpt, noRenameOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (type, method, noRename, solution, dryRun) =>
        {
            await ExecuteAsync(type, method, noRename, solution, dryRun);
        }, typeOpt, methodOpt, noRenameOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string methodName, bool noRename, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = sln;

            // Find the target method symbol
            var methodSymbol = await FindMethodSymbolAsync(sln, typeName, methodName);
            if (methodSymbol == null)
                throw new InvalidOperationException($"Method '{methodName}' not found in type '{typeName}'.");

            // Check if already async — no-op
            if (methodSymbol.IsAsync)
            {
                Console.WriteLine("Method is already async.");
                return;
            }

            var newMethodName = noRename ? methodName : methodName + "Async";

            // Find the document containing the target method
            var targetDocId = FindDocumentIdContainingMethod(sln, methodSymbol);
            if (targetDocId == null)
                throw new InvalidOperationException("Cannot locate source document for the target method.");

            // Collect all call-site reference spans from the original solution
            var refs = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);
            var declarationSpan = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource)?.SourceSpan;
            var callSitesByDoc = refs
                .SelectMany(r => r.Locations)
                .Where(loc =>
                {
                    // Exclude the declaration itself
                    if (loc.Document.Id == targetDocId && loc.Location.SourceSpan == declarationSpan)
                        return false;
                    return true;
                })
                .GroupBy(loc => loc.Document.Id)
                .ToDictionary(g => g.Key, g => g.Select(l => l.Location.SourceSpan).ToList());

            // Process each document in one pass (handles same-doc case correctly)
            var allDocIds = new HashSet<DocumentId>(callSitesByDoc.Keys) { targetDocId };

            foreach (var docId in allDocIds)
            {
                var doc = sln.GetDocument(docId)!;
                var root = (await doc.GetSyntaxRootAsync())!;

                callSitesByDoc.TryGetValue(docId, out var callSpans);

                root = TransformDocument(
                    root,
                    isTargetDoc: docId == targetDocId,
                    methodSymbol,
                    newMethodName,
                    methodName,
                    callSpans ?? new List<TextSpan>());

                if (root is CompilationUnitSyntax cu)
                    root = EnsureUsing(cu, "System.Threading.Tasks");

                sln = sln.WithDocumentSyntaxRoot(docId, root);
            }

            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
    }

    /// <summary>
    /// Transforms one document in a single ReplaceNodes pass:
    /// - Renames + makes async the target method declaration (if this is the target doc)
    /// - Wraps call sites with await and renames the method reference
    /// - Promotes caller methods to async if needed
    /// </summary>
    private static SyntaxNode TransformDocument(
        SyntaxNode root,
        bool isTargetDoc,
        IMethodSymbol methodSymbol,
        string newMethodName,
        string oldMethodName,
        List<TextSpan> callSpans)
    {
        // Locate the target declaration node
        MethodDeclarationSyntax? targetDecl = null;
        if (isTargetDoc)
        {
            var declLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (declLocation != null)
            {
                var node = root.FindNode(declLocation.SourceSpan);
                targetDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            }
        }

        // Locate invocation nodes from the original span positions
        var callInvocations = new List<InvocationExpressionSyntax>();
        foreach (var span in callSpans)
        {
            var refNode = root.FindNode(span);
            var invocation = refNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation != null && !(invocation.Parent is AwaitExpressionSyntax))
                callInvocations.Add(invocation);
        }

        // Collect caller methods that need async promotion
        var methodsToPromote = callInvocations
            .Select(inv => inv.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault())
            .Where(m => m != null && !m!.Modifiers.Any(SyntaxKind.AsyncKeyword))
            .Select(m => m!)
            .Distinct(ReferenceEqualityComparer.Instance as IEqualityComparer<MethodDeclarationSyntax>)
            .ToList();

        // Build the combined node set for a single ReplaceNodes pass
        var nodesToReplace = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
        if (targetDecl != null) nodesToReplace.Add(targetDecl);
        foreach (var inv in callInvocations) nodesToReplace.Add(inv);
        foreach (var m in methodsToPromote) nodesToReplace.Add(m);

        if (nodesToReplace.Count == 0) return root;

        return root.ReplaceNodes(nodesToReplace, (original, rewritten) =>
        {
            // Transform the target method declaration
            if (targetDecl != null && ReferenceEquals(original, targetDecl))
                return TransformTargetDeclaration((MethodDeclarationSyntax)rewritten, newMethodName);

            // Wrap a call site with await + rename
            if (original is InvocationExpressionSyntax invOrig && callInvocations.Any(i => ReferenceEquals(i, invOrig)))
            {
                var inv = (InvocationExpressionSyntax)rewritten;
                if (newMethodName != oldMethodName)
                    inv = RenameInvocationTarget(inv, oldMethodName, newMethodName);

                return SyntaxFactory.AwaitExpression(
                    SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
                        .WithLeadingTrivia(inv.GetLeadingTrivia())
                        .WithTrailingTrivia(SyntaxFactory.Space),
                    inv.WithoutLeadingTrivia());
            }

            // Promote containing method to async
            if (original is MethodDeclarationSyntax mOrig && methodsToPromote.Any(m => ReferenceEquals(m, mOrig)))
                return PromoteMethodToAsync((MethodDeclarationSyntax)rewritten);

            return rewritten;
        });
    }

    private static MethodDeclarationSyntax TransformTargetDeclaration(MethodDeclarationSyntax method, string newMethodName)
    {
        // Add async modifier
        method = method.AddModifiers(
            SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        // Wrap return type: void -> Task, T -> Task<T>
        var returnTypeText = method.ReturnType.ToString().Trim();
        TypeSyntax newReturnType = returnTypeText == "void"
            ? SyntaxFactory.ParseTypeName("Task").WithTriviaFrom(method.ReturnType)
            : SyntaxFactory.ParseTypeName($"Task<{returnTypeText}>").WithTriviaFrom(method.ReturnType);
        method = method.WithReturnType(newReturnType);

        // Rename
        method = method.WithIdentifier(
            SyntaxFactory.Identifier(newMethodName).WithTriviaFrom(method.Identifier));

        return method;
    }

    private static InvocationExpressionSyntax RenameInvocationTarget(
        InvocationExpressionSyntax inv, string oldName, string newName)
    {
        // Rename the last matching identifier in the invocation expression (the method name)
        var identifiers = inv.Expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(id => id.Identifier.Text == oldName)
            .ToList();

        if (identifiers.Count == 0) return inv;

        var target = identifiers.Last();
        var renamed = target.WithIdentifier(
            SyntaxFactory.Identifier(newName).WithTriviaFrom(target.Identifier));
        return inv.ReplaceNode(target, renamed);
    }

    private static MethodDeclarationSyntax PromoteMethodToAsync(MethodDeclarationSyntax method)
    {
        method = method.AddModifiers(
            SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        var returnTypeText = method.ReturnType.ToString().Trim();
        if (returnTypeText == "void")
        {
            method = method.WithReturnType(
                SyntaxFactory.ParseTypeName("Task").WithTriviaFrom(method.ReturnType));
        }
        else if (!returnTypeText.StartsWith("Task", StringComparison.Ordinal))
        {
            method = method.WithReturnType(
                SyntaxFactory.ParseTypeName($"Task<{returnTypeText}>").WithTriviaFrom(method.ReturnType));
        }

        return method;
    }

    private static async Task<IMethodSymbol?> FindMethodSymbolAsync(Solution sln, string typeName, string methodName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
            {
                if (symbol is INamedTypeSymbol typeSymbol)
                {
                    var method = typeSymbol.GetMembers(methodName)
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault();
                    if (method != null)
                        return method;
                }
            }
        }
        return null;
    }

    private static DocumentId? FindDocumentIdContainingMethod(Solution sln, IMethodSymbol method)
    {
        var location = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null) return null;

        foreach (var project in sln.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                    return doc.Id;
            }
        }
        return null;
    }

    private static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax root, string ns)
    {
        var existing = root.Usings
            .Select(u => u.Name?.ToString().Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (existing.Contains(ns))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(" " + ns))
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        return root.AddUsings(newUsing);
    }
}
