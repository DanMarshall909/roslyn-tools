using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Prepares static dependency migrations without writing repository files.</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "The internal planning service is composed through application operations.")]
internal sealed class StaticDependencyMappingService : IRtService
{
    internal async Task<StaticDependencyPlan> PrepareAsync(
        Solution solution,
        StaticDependencyMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = await SolutionRevision.ComputeAsync(solution, cancellationToken);
        var mappingFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request.Mapping)));
        var sites = new List<StaticDependencySite>();
        var selection = request.Selection;
        var mapping = request.Mapping;

        StaticDependencyPlan Empty(StaticDependencyPlanStatus status) => new(
            "rt.static-dependency.plan.v1", fingerprint, mappingFingerprint, request, status,
            OrderSites(sites), [], new PreparedChangeSet(solution, solution));

        void SelectionBlock(string code, string message)
        {
            sites.Add(new StaticDependencySite(
                "selection:" + code, selection.ProjectPath, selection.TypeDocumentationId,
                selection.ProjectPath, 0, 0, StaticDependencySiteDisposition.Blocked,
                "selection." + code, message));
        }

        void CheckFingerprint(string observed, string expected)
        {
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
                SelectionBlock("fingerprint", "Source fingerprint drift: reacquire the original inputs and supply their current fingerprint before preparation.");
        }

        // Resolve selection independently so duplicate physical paths have an explicit
        // selection error, even when authoritative acquisition is also Partial.
        var projects = solution.Projects.Where(project => string.Equals(
            project.FilePath, selection.ProjectPath, StringComparison.Ordinal)).ToArray();
        if (projects.Length != 1)
        {
            SelectionBlock("project", projects.Length == 0
                ? $"Selection project '{selection.ProjectPath}' is not loaded; select an exact loaded project path."
                : $"Selection project '{selection.ProjectPath}' is ambiguous across compilation contexts; select an unambiguous project.");
            return Empty(StaticDependencyPlanStatus.Blocked);
        }

        var project = projects[0];
        var compilation = await project.GetCompilationAsync(cancellationToken);
        INamedTypeSymbol[] types = compilation is null ? [] : DocumentationCommentId.GetSymbolsForDeclarationId(
            selection.TypeDocumentationId, compilation).OfType<INamedTypeSymbol>()
            .Where(type => type.GetDocumentationCommentId() == selection.TypeDocumentationId
                && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)).ToArray();
        if (types.Length != 1 || types[0].IsStatic || types[0].TypeKind != TypeKind.Class)
        {
            SelectionBlock("type", $"Selection type '{selection.TypeDocumentationId}' must resolve to one instance class in the selected project.");
            return Empty(StaticDependencyPlanStatus.Blocked);
        }

        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, cancellationToken);
        if (!WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence
            || !completeness.RequestedProjects.Contains(selection.ProjectPath, StringComparer.Ordinal))
        {
            SelectionBlock("acquisition", "Authoritative acquisition must cover the original selected scope and snapshot before preparation: "
                + string.Join("; ", completeness.Diagnostics));
            return Empty(StaticDependencyPlanStatus.Blocked);
        }
        CheckFingerprint(fingerprint, request.ExpectedSourceFingerprint);
        if (sites.Count > 0)
            return Empty(StaticDependencyPlanStatus.Blocked);

        var problems = StaticDependencyMappingSemantics.ValidateDeclarations(mapping).ToList();
        var source = StaticDependencyMappingSemantics.ResolveMethod(compilation!, mapping.Source);
        var target = StaticDependencyMappingSemantics.ResolveMethod(compilation!, mapping.Target);
        if (source is null)
            problems.Add($"Source mapping '{mapping.Source.DocumentationId}' in assembly '{mapping.Source.AssemblyIdentity}' must resolve to one exact method.");
        if (target is null)
            problems.Add($"Target mapping '{mapping.Target.DocumentationId}' in assembly '{mapping.Target.AssemblyIdentity}' must resolve to one exact method.");
        var fields = DocumentationCommentId.GetSymbolsForDeclarationId(mapping.ReceiverFieldDocumentationId, compilation!)
            .OfType<IFieldSymbol>().Where(field => field.GetDocumentationCommentId() == mapping.ReceiverFieldDocumentationId
                && SymbolEqualityComparer.Default.Equals(field.ContainingType, types[0])).ToArray();
        var receiver = fields.Length == 1 ? fields[0] : null;
        if (receiver is null || receiver.IsStatic || receiver.IsConst)
            problems.Add($"Receiver mapping '{mapping.ReceiverFieldDocumentationId}' must identify an existing instance field on the selected type.");
        if (source is not null && target is not null)
            problems.AddRange(StaticDependencyMappingSemantics.ValidateMethods(source, target).Select(problem =>
                $"Source mapping '{mapping.Source.DocumentationId}' in '{mapping.Source.AssemblyIdentity}', target mapping '{mapping.Target.DocumentationId}' in '{mapping.Target.AssemblyIdentity}': {problem}"));

        if (source is null)
        {
            SelectionBlock("mapping", string.Join("; ", problems));
            return Empty(StaticDependencyPlanStatus.Blocked);
        }

        var inventory = await StaticDependencyMappingInventory.FindAsync(solution, mapping.Source, cancellationToken);
        var selected = inventory.Where(site => site.Document.Project.Id == project.Id
            && site.Site.TypeDocumentationId == selection.TypeDocumentationId
            && (selection.SiteIds is null || selection.SiteIds.Contains(site.Site.Id, StringComparer.Ordinal))).ToArray();
        var selectedIds = selected.Select(site => site.Site.Id).ToHashSet(StringComparer.Ordinal);
        var selectedNodes = selected.Select(entry => entry.Reference).ToHashSet();
        if (selection.SiteIds is not null)
        {
            foreach (var id in selection.SiteIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!selectedIds.Contains(id))
                    SelectionBlock("site:" + id, $"Selection site '{id}' does not resolve within the exact selected project and type; refresh the site inventory.");
            }
        }

        foreach (var entry in inventory)
        {
            if (!selectedIds.Contains(entry.Site.Id))
            {
                sites.Add(entry.Site);
                continue;
            }

            var reason = problems.Count > 0 ? string.Join("; ", problems)
                : StaticDependencyMappingInventory.UnsupportedReason(entry);
            if (reason is null && entry.Reference.DescendantNodes().Any(selectedNodes.Contains))
                reason = "Nested selected invocations require an argument rewrite, which is unsupported; select independent invocation sites.";
            var path = entry.Document.FilePath;
            if (reason is null && (path is null || !Path.IsPathFullyQualified(path)
                || solution.Projects.SelectMany(item => item.Documents).Count(document => document.FilePath == path) != 1))
                reason = "The selected document path is missing or shared by multiple document contexts; an exact independent document candidate is required.";
            if (reason is not null)
                sites.Add(entry.Site with { Disposition = StaticDependencySiteDisposition.Blocked, Code = "mapping.unsupported", Message = reason });
            else
                sites.Add(entry.Site with { Code = "selection.ready", Message = "Selected site is eligible pending whole-selection verification." });
        }

        if (problems.Count > 0)
            SelectionBlock("mapping", string.Join("; ", problems));
        if (sites.Any(site => site.Disposition == StaticDependencySiteDisposition.Blocked))
        {
            RefuseEligible(sites, selectedIds);
            return Empty(StaticDependencyPlanStatus.Blocked);
        }
        if (selected.Length == 0)
            return Empty(StaticDependencyPlanStatus.NoOp);

        var (candidate, bindingBlockers) = await RewriteAsync(solution, selected, mapping, receiver!, target!, cancellationToken);
        foreach (var blocker in bindingBlockers)
            sites[sites.FindIndex(site => site.Id == blocker.Id)] = blocker;

        // Acquisition was assessed on the complete original scope. Candidate compilation
        // is additional evidence and never creates or upgrades an acquisition receipt.
        sites.AddRange(await StaticDependencyMappingInventory.FindGeneratedDriftAsync(solution, candidate, cancellationToken));
        foreach (var candidateProject in candidate.Projects.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            var candidateCompilation = await candidateProject.GetCompilationAsync(cancellationToken);
            var errors = candidateCompilation?.GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()).Order(StringComparer.Ordinal).ToArray();
            if (errors is null || errors.Length > 0)
                SelectionBlock("compilation:" + candidateProject.FilePath,
                    $"Candidate compilation failed for '{candidateProject.FilePath}': " + (errors is null ? "no compilation" : string.Join("; ", errors)));
        }
        CheckFingerprint(await SolutionRevision.ComputeAsync(solution, cancellationToken), fingerprint);
        if (sites.Any(site => site.Disposition == StaticDependencySiteDisposition.Blocked))
        {
            RefuseEligible(sites, selectedIds);
            return Empty(StaticDependencyPlanStatus.Blocked);
        }

        var documents = new List<StaticDependencyDocumentChange>();
        foreach (var id in selected.Select(entry => entry.Document.Id).Distinct()
                     .OrderBy(id => solution.GetDocument(id)!.FilePath, StringComparer.Ordinal))
        {
            var before = solution.GetDocument(id)!;
            documents.Add(new StaticDependencyDocumentChange(before.FilePath!,
                (await before.GetTextAsync(cancellationToken)).ToString(),
                (await candidate.GetDocument(id)!.GetTextAsync(cancellationToken)).ToString()));
        }
        for (var index = 0; index < sites.Count; index++)
        {
            if (selectedIds.Contains(sites[index].Id))
                sites[index] = sites[index] with
                {
                    Disposition = StaticDependencySiteDisposition.ConvertedInCandidate,
                    Code = "mapping.converted",
                    Message = "Exact existing-receiver target and argument bindings verified in the complete candidate."
                };
        }
        return new StaticDependencyPlan("rt.static-dependency.plan.v1", fingerprint, mappingFingerprint,
            request, StaticDependencyPlanStatus.Prepared, OrderSites(sites), documents,
            new PreparedChangeSet(solution, candidate));
    }

    // Shared by existing-field mapping and constructor introduction. References retain
    // original operations/sites even when their annotated syntax has moved in a candidate.
    internal static async Task<(Solution Candidate, IReadOnlyList<StaticDependencySite> Blockers)> RewriteAsync(
        Solution solution, IReadOnlyList<StaticDependencyReference> selected,
        StaticDependencyMethodMapping mapping, IFieldSymbol receiver, IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        // Only the callee is replaced. Arguments, separators and named associations
        // retain their syntax and trivia. Annotations survive changed source offsets.
        var blockers = new List<StaticDependencySite>();
        var candidate = solution;
        var annotations = selected.ToDictionary(entry => entry.Site.Id, _ => new SyntaxAnnotation());
        foreach (var group in selected.GroupBy(entry => entry.Document.Id))
        {
            var document = solution.GetDocument(group.Key)!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken))!;
            var byNode = group.ToDictionary(entry => entry.Reference);
            var rewritten = root.ReplaceNodes(byNode.Keys, (original, current) =>
            {
                var entry = byNode[original];
                var call = (InvocationExpressionSyntax)current;
                var expression = SyntaxFactory.ParseExpression("this." + Escape(receiver!.Name) + "." + Escape(target!.Name))
                    .WithTriviaFrom(call.Expression);
                return call.WithExpression(expression).WithAdditionalAnnotations(annotations[entry.Site.Id]);
            });
            candidate = candidate.WithDocumentSyntaxRoot(group.Key, rewritten);
        }

        foreach (var entry in selected)
        {
            var document = candidate.GetDocument(entry.Document.Id)!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken))!;
            var call = (InvocationExpressionSyntax)root.GetAnnotatedNodes(annotations[entry.Site.Id]).Single();
            var model = (await document.GetSemanticModelAsync(cancellationToken))!;
            var operation = model.GetOperation(call, cancellationToken) as IInvocationOperation;
            string? reason;
            if (operation is null)
                reason = "Target binding failed (inaccessible or ambiguous receiver call): "
                    + string.Join("; ", model.GetDiagnostics(call.Span, cancellationToken)
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.ToString()).Order(StringComparer.Ordinal));
            else
                reason = StaticDependencyMappingSemantics.ValidateBinding(entry.Operation!, operation, mapping, receiver!);
            if (reason is not null)
            {
                blockers.Add(entry.Site with { Disposition = StaticDependencySiteDisposition.Blocked, Code = "mapping.binding", Message = reason });
            }
        }

        return (candidate, blockers);
    }

    private static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static StaticDependencySite[] OrderSites(List<StaticDependencySite> sites) => sites
        .OrderBy(site => site.ProjectPath, StringComparer.Ordinal)
        .ThenBy(site => site.TypeDocumentationId, StringComparer.Ordinal)
        .ThenBy(site => site.Path, StringComparer.Ordinal).ThenBy(site => site.SpanStart)
        .ThenBy(site => site.Id, StringComparer.Ordinal).ToArray();

    private static void RefuseEligible(List<StaticDependencySite> sites, HashSet<string> selectedIds)
    {
        for (var index = 0; index < sites.Count; index++)
        {
            if (selectedIds.Contains(sites[index].Id) && sites[index].Disposition != StaticDependencySiteDisposition.Blocked)
                sites[index] = sites[index] with
                {
                    Disposition = StaticDependencySiteDisposition.Blocked,
                    Code = "selection.blocked",
                    Message = "The entire selection is blocked by another disposition; explicitly narrow the selected sites or resolve every blocker."
                };
        }
    }
}
