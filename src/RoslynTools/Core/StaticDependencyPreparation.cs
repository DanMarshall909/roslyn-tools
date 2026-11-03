using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>Retains original acquisition authority; candidate checks never issue receipts.</summary>
internal sealed class StaticDependencyPreparation
{
    private readonly StaticDependencySelection _selection;
    private readonly WorkspaceAcquisitionReceipt? _receipt;

    private StaticDependencyPreparation(Solution original, StaticDependencySelection selection,
        string fingerprint, Project project, INamedTypeSymbol type)
    {
        Original = original;
        Fingerprint = fingerprint;
        Project = project;
        Type = type;
        _selection = selection;
        _receipt = WorkspaceAcquisitionRegistry.Get(original.Workspace).Receipt;
    }

    internal Solution Original { get; }
    internal string Fingerprint { get; }
    internal Project Project { get; }
    internal INamedTypeSymbol Type { get; }

    internal static async Task<(StaticDependencyPreparation? Preparation, IReadOnlyList<StaticDependencySite> Blockers)> AcquireAsync(
        Solution original, StaticDependencySelection selection, string expectedFingerprint, CancellationToken cancellationToken)
    {
        StaticDependencySite Block(string code, string message) => CreateBlocker(selection, code, message);

        var fingerprint = await SolutionRevision.ComputeAsync(original, cancellationToken);
        if (!string.Equals(fingerprint, expectedFingerprint, StringComparison.Ordinal))
            return (null, [Block("fingerprint", "Original source fingerprint is stale; reacquire before preparation.")]);
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(original, cancellationToken);
        if (!WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence
            || !completeness.RequestedProjects.Contains(selection.ProjectPath, StringComparer.Ordinal))
            return (null, [Block("acquisition", "Complete authoritative acquisition of the original selected scope is required.")]);
        var projects = original.Projects.Where(project => project.FilePath == selection.ProjectPath).ToArray();
        if (projects.Length != 1)
            return (null, [Block("project", "Select one unambiguous loaded project path.")]);
        var compilation = await projects[0].GetCompilationAsync(cancellationToken);
        INamedTypeSymbol[] types = compilation is null ? [] : DocumentationCommentId
            .GetSymbolsForDeclarationId(selection.TypeDocumentationId, compilation).OfType<INamedTypeSymbol>()
            .Where(type => SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)).ToArray();
        if (types.Length != 1 || types[0].TypeKind != TypeKind.Class || types[0].IsStatic)
            return (null, [Block("type", "Select exactly one source instance class in the acquired project.")]);
        return (new StaticDependencyPreparation(original, selection, fingerprint, projects[0], types[0]), []);
    }

    internal async Task<IReadOnlyList<StaticDependencySite>> VerifyAsync(Solution candidate, CancellationToken cancellationToken)
    {
        var blockers = new List<StaticDependencySite>();
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(Original, cancellationToken);
        if (!ReferenceEquals(_receipt, WorkspaceAcquisitionRegistry.Get(Original.Workspace).Receipt)
            || !WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence
            || !string.Equals(Fingerprint, await SolutionRevision.ComputeAsync(Original, cancellationToken), StringComparison.Ordinal))
            blockers.Add(CreateBlocker(_selection, "original-drift", "Original acquisition or semantic inputs changed during preparation."));

        // Only authored text may change. Keep every compilation context and input owner.
        if (candidate.Workspace != Original.Workspace || !candidate.ProjectIds.SequenceEqual(Original.ProjectIds))
            return [CreateBlocker(_selection, "candidate-scope", "Candidate changed workspace or project membership.")];
        foreach (var project in Original.Projects)
        {
            var changed = candidate.GetProject(project.Id)!;
            if (project.Name != changed.Name || project.AssemblyName != changed.AssemblyName
                || project.FilePath != changed.FilePath || project.Language != changed.Language
                || !Equals(project.CompilationOptions, changed.CompilationOptions)
                || !Equals(project.ParseOptions, changed.ParseOptions)
                || !project.ProjectReferences.SequenceEqual(changed.ProjectReferences)
                || !project.MetadataReferences.SequenceEqual(changed.MetadataReferences)
                || !project.AnalyzerReferences.SequenceEqual(changed.AnalyzerReferences)
                || !project.DocumentIds.SequenceEqual(changed.DocumentIds)
                || !project.AdditionalDocumentIds.SequenceEqual(changed.AdditionalDocumentIds)
                || !project.AnalyzerConfigDocuments.Select(document => document.Id)
                    .SequenceEqual(changed.AnalyzerConfigDocuments.Select(document => document.Id)))
                blockers.Add(CreateBlocker(_selection, "candidate-inputs:" + project.FilePath,
                    "Candidate must preserve original project metadata, references and document membership."));
            foreach (var document in project.AdditionalDocuments.Concat(project.AnalyzerConfigDocuments))
            {
                var after = changed.GetAdditionalDocument(document.Id) ?? changed.GetAnalyzerConfigDocument(document.Id);
                if (after is null || !(await document.GetTextAsync(cancellationToken)).ContentEquals(await after.GetTextAsync(cancellationToken)))
                    blockers.Add(CreateBlocker(_selection, "candidate-input:" + document.FilePath,
                        "Candidate changed a non-source semantic input."));
            }
            var compilation = await changed.GetCompilationAsync(cancellationToken);
            var errors = compilation?.GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString()).Order(StringComparer.Ordinal).ToArray();
            if (errors is null || errors.Length != 0)
                blockers.Add(CreateBlocker(_selection, "compilation:" + project.FilePath,
                    "Candidate compilation failed: " + (errors is null ? "no compilation" : string.Join("; ", errors))));
        }
        blockers.AddRange(await StaticDependencyMappingInventory.FindGeneratedDriftAsync(Original, candidate, cancellationToken));
        return blockers;
    }

    internal static StaticDependencySite CreateBlocker(StaticDependencySelection selection, string code, string message) => new(
        "introduction:" + code, selection.ProjectPath, selection.TypeDocumentationId, selection.ProjectPath, 0, 0,
        StaticDependencySiteDisposition.Blocked, "introduction." + code, message);
}
