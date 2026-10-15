using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynTools.Core;

internal sealed record StaticDependencyReference(
    Document Document,
    SyntaxNode Reference,
    IInvocationOperation? Operation,
    StaticDependencySite Site);

internal static class StaticDependencyMappingInventory
{
    internal static async Task<IReadOnlyList<StaticDependencyReference>> FindAsync(
        Solution solution, StaticDependencySymbol source, CancellationToken cancellationToken)
    {
        var references = new List<StaticDependencyReference>();
        foreach (var project in solution.Projects.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            var generated = await project.GetSourceGeneratedDocumentsAsync(cancellationToken);
            foreach (var document in project.Documents.Concat(generated).OrderBy(item => item.FilePath, StringComparer.Ordinal))
            {
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var model = await document.GetSemanticModelAsync(cancellationToken);
                if (root is null || model is null)
                    continue; // Completeness has already refused unavailable semantic inputs.
                foreach (var node in root.DescendantNodes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IInvocationOperation? operation = null;
                    IMethodSymbol? method = null;
                    if (node is InvocationExpressionSyntax invocation)
                    {
                        operation = model.GetOperation(invocation, cancellationToken) as IInvocationOperation;
                        method = operation?.TargetMethod;
                    }
                    else if (node is MemberAccessExpressionSyntax or SimpleNameSyntax)
                    {
                        // The invocation itself owns its callee; a member access owns its name.
                        if (node.Parent is InvocationExpressionSyntax parentCall && parentCall.Expression == node
                            || node.Parent is MemberAccessExpressionSyntax member && member.Name == node)
                            continue;
                        method = model.GetSymbolInfo(node, cancellationToken).Symbol as IMethodSymbol;
                        if (method is null)
                            method = model.GetMemberGroup(node, cancellationToken).OfType<IMethodSymbol>()
                                .FirstOrDefault(candidate => StaticDependencyMappingSemantics.Matches(candidate, source));
                    }
                    else
                        continue;
                    if (method is null || !StaticDependencyMappingSemantics.Matches(method, source))
                        continue;

                    var declaration = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
                    var type = declaration is null ? null : model.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
                    var typeId = type?.GetDocumentationCommentId() ?? "T:<top-level>";
                    var path = document.FilePath ?? document.Name;
                    var projectPath = project.FilePath ?? project.Name;
                    var id = "site:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        Project = projectPath,
                        project.Name,
                        project.AssemblyName,
                        Type = typeId,
                        Path = path,
                        node.SpanStart,
                        node.Span.Length
                    })));
                    references.Add(new StaticDependencyReference(document, node, operation,
                        new StaticDependencySite(id, projectPath, typeId, path, node.SpanStart, node.Span.Length,
                            StaticDependencySiteDisposition.Skipped, "selection.outside", "Exact source reference is outside the selected project, type or sites.")));
                }
            }
        }
        return references;
    }

    internal static async Task<IReadOnlyList<StaticDependencySite>> FindGeneratedDriftAsync(
        Solution original, Solution candidate, CancellationToken cancellationToken)
    {
        var sites = new List<StaticDependencySite>();
        foreach (var project in original.Projects.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            var before = (await project.GetSourceGeneratedDocumentsAsync(cancellationToken))
                .ToDictionary(document => (document.Name, Path: document.FilePath ?? document.Name));
            var after = (await candidate.GetProject(project.Id)!.GetSourceGeneratedDocumentsAsync(cancellationToken))
                .ToDictionary(document => (document.Name, Path: document.FilePath ?? document.Name));
            foreach (var key in before.Keys.Union(after.Keys).OrderBy(key => key.Path, StringComparer.Ordinal)
                         .ThenBy(key => key.Name, StringComparer.Ordinal))
            {
                before.TryGetValue(key, out var originalDocument);
                after.TryGetValue(key, out var candidateDocument);
                if (originalDocument is not null && candidateDocument is not null
                    && (await originalDocument.GetTextAsync(cancellationToken))
                        .ContentEquals(await candidateDocument.GetTextAsync(cancellationToken)))
                    continue;

                // The evidence names a real generated document from one of the two
                // snapshots. Never attach candidate acquisition to the original workspace.
                var document = originalDocument ?? candidateDocument!;
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                var model = await document.GetSemanticModelAsync(cancellationToken);
                var declaration = root?.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
                var typeId = declaration is null ? "T:<generated>"
                    : (model?.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol)
                        ?.GetDocumentationCommentId() ?? "T:<generated>";
                var projectPath = project.FilePath ?? project.Name;
                var id = "generated:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Project = projectPath,
                    project.Name,
                    project.AssemblyName,
                    Hint = key.Name,
                    key.Path
                })));
                sites.Add(new StaticDependencySite(id, projectPath, typeId, key.Path, 0, root?.FullSpan.Length ?? 0,
                    StaticDependencySiteDisposition.Blocked, "mapping.generated-drift",
                    $"Generated output '{key.Name}' changed membership or text after the authored rewrite; preserve the original inputs and review the generator's effects before preparing this selection."));
            }
        }
        return sites;
    }

    internal static string? UnsupportedReason(StaticDependencyReference entry)
    {
        if (entry.Document is SourceGeneratedDocument)
            return $"Generated selected context '{entry.Document.Name}' is unsupported; select authored sites or change the generator through a separately reviewed operation.";
        if (entry.Reference is not InvocationExpressionSyntax call || entry.Operation is null)
            return "Method-group references are unsupported; select a direct static method invocation.";
        if (call.Ancestors().Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            return "Lambda, local-function, anonymous-function and expression-tree contexts are unsupported; select an ordinary instance method invocation.";
        var method = call.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        if (method is not MethodDeclarationSyntax declaration)
            return "Initializer, constructor and accessor contexts are unsupported; select an ordinary instance method invocation.";
        if (declaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            return "A static method cannot use the existing instance receiver; select an instance method site.";
        if (call.Expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return "The static callee expression shape is unsupported; argument or expression adaptation is not available.";
        if (call.Expression.DescendantTrivia().Any(trivia => trivia.Span.Length > 0 && call.Expression.Span.Contains(trivia.Span)))
            return "Static callee interior trivia is unsupported; preserve it explicitly before selecting this site.";
        // Declaration validation has already refused optional/params signatures,
        // so a successfully bound call has only explicit ordinary arguments.
        return null;
    }
}
