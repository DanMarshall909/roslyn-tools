using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Prepares one coherent dependency, constructor, caller and callee proposal.</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "The internal planning service is composed through application operations.")]
internal sealed class StaticDependencyIntroductionService : IRtService
{
    internal async Task<StaticDependencyIntroductionPlan> PrepareAsync(
        Solution solution,
        StaticDependencyIntroductionRequest request,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = await SolutionRevision.ComputeAsync(solution, cancellationToken);
        var mappingFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request.Mapping)));
        var sites = new List<StaticDependencySite>();
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        var selection = request.Selection;
        void Block(string code, string message) => sites.Add(StaticDependencyPreparation.CreateBlocker(selection, code, message));
        StaticDependencyIntroductionPlan Empty(StaticDependencyPlanStatus status)
        {
            if (status == StaticDependencyPlanStatus.Blocked)
            {
                var reasons = string.Join("; ", sites.Where(site => site.Disposition == StaticDependencySiteDisposition.Blocked)
                    .Select(site => site.Code + ": " + site.Message).Distinct(StringComparer.Ordinal));
                for (var index = 0; index < sites.Count; index++)
                {
                    var site = sites[index];
                    if (selectedIds.Contains(site.Id) && site.Disposition != StaticDependencySiteDisposition.Blocked)
                    {
                        sites[index] = site with
                        {
                            Disposition = StaticDependencySiteDisposition.Blocked,
                            Code = "introduction.atomic-refusal",
                            Message = "No selected call can be converted because introduction was atomically refused: " + reasons
                        };
                    }
                }
            }
            return new StaticDependencyIntroductionPlan(
                "rt.static-dependency.introduction-plan.v1", fingerprint, mappingFingerprint, request, status,
                OrderSites(sites), [], [], null, new PreparedChangeSet(solution, solution));
        }

        var (preparation, acquisitionBlockers) = await StaticDependencyPreparation.AcquireAsync(
            solution, selection, request.ExpectedSourceFingerprint, cancellationToken);
        sites.AddRange(acquisitionBlockers);
        if (preparation is null)
            return Empty(StaticDependencyPlanStatus.Blocked);
        fingerprint = preparation.Fingerprint;
        var compilation = (await preparation.Project.GetCompilationAsync(cancellationToken))!;
        var mapping = new StaticDependencyMethodMapping(request.Mapping.SchemaVersion, request.Mapping.Source,
            request.ExistingReceiverFieldDocumentationId ?? "F:" + selection.TypeDocumentationId[2..] + "." + request.PreferredFieldName,
            request.Mapping.Target, request.Mapping.Preconditions);
        var problems = StaticDependencyMappingSemantics.ValidateDeclarations(mapping).ToList();
        var source = StaticDependencyMappingSemantics.ResolveMethod(compilation, mapping.Source);
        var target = StaticDependencyMappingSemantics.ResolveMethod(compilation, mapping.Target);
        var receivers = DocumentationCommentId.GetSymbolsForDeclarationId(request.Mapping.ReceiverType.DocumentationId, compilation)
            .OfType<INamedTypeSymbol>().Where(type => StaticDependencyMappingSemantics.Matches(type, request.Mapping.ReceiverType)).ToArray();
        var receiverType = receivers.Length == 1 ? receivers[0] : null;
        if (source is null || target is null || receiverType is null)
            problems.Add("Source, target and receiver type must each resolve to their exact declared symbol and assembly.");
        else
        {
            problems.AddRange(StaticDependencyMappingSemantics.ValidateMethods(source, target));
            if (!SymbolEqualityComparer.Default.Equals(target.ContainingType, receiverType)
                || receiverType.Arity != 0 || receiverType.ContainingType is not null
                || receiverType.TypeKind is not (TypeKind.Interface or TypeKind.Class))
                problems.Add("Target must be declared on the nongeneric reader class or interface; inherited and adapted receivers are unsupported.");
        }
        if (problems.Count != 0)
            Block("mapping", string.Join("; ", problems));

        IFieldSymbol? existingField = null;
        if (request.ExistingReceiverFieldDocumentationId is { } existingId)
        {
            var (field, reason) = StaticDependencyReceiverReuse.Resolve(compilation, preparation.Type, receiverType, existingId);
            existingField = field;
            if (reason is not null)
                Block("receiver", reason);
        }

        var inventory = await StaticDependencyMappingInventory.FindAsync(solution, mapping.Source, cancellationToken);
        var selected = inventory.Where(entry => entry.Document.Project.Id == preparation.Project.Id
            && entry.Site.TypeDocumentationId == selection.TypeDocumentationId
            && (selection.SiteIds is null || selection.SiteIds.Contains(entry.Site.Id, StringComparer.Ordinal))).ToArray();
        selectedIds.UnionWith(selected.Select(entry => entry.Site.Id));
        sites.AddRange(inventory.Select(entry => entry.Site));
        if (selection.SiteIds is not null && selection.SiteIds.Any(id => !selectedIds.Contains(id)))
            Block("sites", "Every selected site must resolve in the acquired original project and type.");
        foreach (var entry in selected)
        {
            var reason = StaticDependencyMappingInventory.UnsupportedReason(entry, allowConstructor: true);
            if (reason is null)
            {
                var model = (await entry.Document.GetSemanticModelAsync(cancellationToken))!;
                reason = StaticDependencyIntroductionClosure.SelectedReceiverUnsupportedReason(entry.Operation!, model, cancellationToken);
                if (reason is null && existingField is not null)
                    reason = StaticDependencyReceiverReuse.ConstructorUnavailableReason(entry, existingField, model, cancellationToken);
            }
            if (reason is null && selected.Any(other => other != entry && entry.Document.Id == other.Document.Id
                && entry.Reference.Span.Contains(other.Reference.Span)))
                reason = "Nested selected calls require unsupported argument rewriting.";
            // Reuse bypasses closure analysis, so retain its path admission here.
            // New-field introduction keeps the closure's existing document diagnostic.
            var path = entry.Document.FilePath;
            if (reason is null && existingField is not null && (path is null || !Path.IsPathFullyQualified(path)
                || solution.Projects.SelectMany(project => project.Documents).Count(document => document.FilePath == path) != 1))
                reason = "The selected document path must be absolute and unique; missing or shared document paths cannot form an exact independent candidate.";
            if (reason is not null)
                sites[sites.FindIndex(site => site.Id == entry.Site.Id)] = entry.Site with
                {
                    Disposition = StaticDependencySiteDisposition.Blocked,
                    Code = "introduction.context",
                    Message = reason
                };
        }
        if (sites.Any(site => site.Disposition == StaticDependencySiteDisposition.Blocked))
            return Empty(StaticDependencyPlanStatus.Blocked);
        if (selected.Length == 0)
        {
            sites.AddRange(await preparation.VerifyAsync(solution, cancellationToken));
            return Empty(sites.Any(site => site.Disposition == StaticDependencySiteDisposition.Blocked)
                ? StaticDependencyPlanStatus.Blocked : StaticDependencyPlanStatus.NoOp);
        }

        var introduced = solution;
        IReadOnlyList<StaticDependencyReference> moved = selected;
        StaticDependencyIntroductionClosure? closure = null;
        if (existingField is null)
        {
            // Resolve only existing parameters. No synthesized expression or guessed caller wiring.
            var callers = new List<IParameterSymbol>();
            if (request.Callers.GroupBy(caller => (caller.ProjectPath, caller.MethodDocumentationId)).Any(group => group.Count() != 1))
                Block("caller-duplicates", "Declare exactly one parameter binding per caller method.");
            foreach (var binding in request.Callers)
            {
                IMethodSymbol[] methods = binding.ProjectPath != preparation.Project.FilePath ? [] : DocumentationCommentId
                    .GetSymbolsForDeclarationId(binding.MethodDocumentationId, compilation).OfType<IMethodSymbol>()
                    .Where(method => method.GetDocumentationCommentId() == binding.MethodDocumentationId
                        && SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly)).ToArray();
                if (methods.Length != 1 || binding.ParameterOrdinal < 0 || binding.ParameterOrdinal >= methods[0].Parameters.Length)
                {
                    Block("caller:" + binding.MethodDocumentationId, "Caller binding must resolve to one existing parameter in the selected project.");
                    continue;
                }
                var parameter = methods[0].Parameters[binding.ParameterOrdinal];
                if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.IsOptional
                    || !SymbolEqualityComparer.Default.Equals(parameter.Type, receiverType))
                    Block("caller-type:" + binding.MethodDocumentationId, "Caller reader must be an ordinary parameter of exactly the declared receiver type.");
                callers.Add(parameter);
            }
            if (sites.Any(site => site.Disposition == StaticDependencySiteDisposition.Blocked))
                return Empty(StaticDependencyPlanStatus.Blocked);
            closure = new StaticDependencyIntroductionClosure(preparation, request, receiverType!);
            sites.AddRange(await closure.AnalyzeAsync(callers, cancellationToken));
            if (sites.Any(site => site.Disposition == StaticDependencySiteDisposition.Blocked))
                return Empty(StaticDependencyPlanStatus.Blocked);

            // Preserve original operations, inventory IDs and anchors while syntax offsets move.
            var annotations = selected.ToDictionary(entry => entry.Site.Id, _ => new SyntaxAnnotation());
            var annotated = solution;
            foreach (var group in selected.GroupBy(entry => entry.Document.Id))
            {
                var root = (await solution.GetDocument(group.Key)!.GetSyntaxRootAsync(cancellationToken))!;
                var byNode = group.ToDictionary(entry => entry.Reference);
                annotated = annotated.WithDocumentSyntaxRoot(group.Key, root.ReplaceNodes(byNode.Keys,
                    (before, current) => current.WithAdditionalAnnotations(annotations[byNode[before].Site.Id])));
            }
            introduced = await closure.IntroduceAsync(annotated, cancellationToken);
            var movedEntries = new List<StaticDependencyReference>();
            foreach (var entry in selected)
            {
                var document = introduced.GetDocument(entry.Document.Id)!;
                var root = (await document.GetSyntaxRootAsync(cancellationToken))!;
                movedEntries.Add(entry with { Document = document, Reference = root.GetAnnotatedNodes(annotations[entry.Site.Id]).Single() });
            }
            moved = movedEntries;
        }
        var candidateCompilation = (await introduced.GetProject(preparation.Project.Id)!.GetCompilationAsync(cancellationToken))!;
        var fields = DocumentationCommentId.GetSymbolsForDeclarationId(mapping.ReceiverFieldDocumentationId, candidateCompilation)
            .OfType<IFieldSymbol>().ToArray();
        var candidateTarget = StaticDependencyMappingSemantics.ResolveMethod(candidateCompilation, mapping.Target);
        if (fields.Length != 1 || candidateTarget is null)
        {
            Block("receiver", "Selected receiver field and exact target failed to resolve in the candidate.");
            return Empty(StaticDependencyPlanStatus.Blocked);
        }
        var (candidate, bindingBlockers) = await StaticDependencyMappingService.RewriteAsync(
            introduced, moved, mapping, fields[0], candidateTarget, cancellationToken);
        foreach (var blocker in bindingBlockers)
            sites[sites.FindIndex(site => site.Id == blocker.Id)] = blocker;
        var finalClosure = new List<StaticDependencyClosureChange>();
        if (closure is not null)
        {
            var (closureChanges, closureBlockers) = await closure.VerifyAsync(candidate, cancellationToken);
            finalClosure.AddRange(closureChanges);
            sites.AddRange(closureBlockers);
        }
        sites.AddRange(await preparation.VerifyAsync(candidate, cancellationToken));
        if (sites.Any(site => site.Disposition == StaticDependencySiteDisposition.Blocked))
            return Empty(StaticDependencyPlanStatus.Blocked);

        foreach (var entry in selected)
        {
            sites[sites.FindIndex(site => site.Id == entry.Site.Id)] = entry.Site with
            {
                Disposition = StaticDependencySiteDisposition.ConvertedInCandidate,
                Code = "introduction.converted",
                Message = existingField is null
                    ? "Exact instance target, original arguments and complete constructor/caller wiring verified."
                    : "Exact existing instance field, target and original argument bindings verified; selected constructor availability checked where required."
            };
            finalClosure.Add(new StaticDependencyClosureChange("closure:" + entry.Site.Id,
                StaticDependencyClosureChangeKind.MappedCall, entry.Site.ProjectPath, entry.Site.Path,
                entry.Site.SpanStart, entry.Site.SpanLength, mapping.Source.DocumentationId, mapping.Target.DocumentationId,
                existingField is null
                    ? "Replace the exact static callee with the initialized instance reader; retain the original arguments."
                    : "Replace the exact static callee with the explicitly selected existing field; retain the original arguments and declared receiver preconditions."));
        }
        var documents = new List<StaticDependencyDocumentChange>();
        foreach (var id in candidate.GetChanges(solution).GetProjectChanges().SelectMany(change => change.GetChangedDocuments())
                     .OrderBy(id => solution.GetDocument(id)!.FilePath, StringComparer.Ordinal))
        {
            var before = solution.GetDocument(id)!;
            documents.Add(new StaticDependencyDocumentChange(before.FilePath!,
                (await before.GetTextAsync(cancellationToken)).ToString(),
                (await candidate.GetDocument(id)!.GetTextAsync(cancellationToken)).ToString()));
        }
        var resolution = new StaticDependencyIntroductionResolution(existingField is not null, mapping.ReceiverFieldDocumentationId,
            existingField?.Name ?? closure!.FieldName, existingField is null ? request.PreferredParameterName : null);
        return new StaticDependencyIntroductionPlan("rt.static-dependency.introduction-plan.v1", fingerprint, mappingFingerprint,
            request, StaticDependencyPlanStatus.Prepared, OrderSites(sites), documents,
            finalClosure.OrderBy(change => change.ProjectPath, StringComparer.Ordinal).ThenBy(change => change.Path, StringComparer.Ordinal)
                .ThenBy(change => change.SpanStart).ThenBy(change => change.Kind).ThenBy(change => change.Id, StringComparer.Ordinal).ToArray(),
            resolution, new PreparedChangeSet(solution, candidate));
    }

    private static StaticDependencySite[] OrderSites(IEnumerable<StaticDependencySite> sites) => sites
        .OrderBy(site => site.ProjectPath, StringComparer.Ordinal).ThenBy(site => site.TypeDocumentationId, StringComparer.Ordinal)
        .ThenBy(site => site.Path, StringComparer.Ordinal).ThenBy(site => site.SpanStart).ThenBy(site => site.Id, StringComparer.Ordinal).ToArray();
}
