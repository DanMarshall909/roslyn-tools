using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
/// Simulated acquisition evidence for explicitly selected deterministic in-memory test
/// fixtures. This is not a production collector and is never installed by the default builder.
/// </summary>
public static class ExplicitWorkspaceAcquisition
{
    /// <summary>
    /// Declares the exact expected project set and, when applicable, the generated hint
    /// names controlled by AdhocProjectBuilder's fixed-source generator. Other analyzers
    /// and generators cannot be certified by this fixture provider.
    /// </summary>
    public static async Task AttachAsync(
        Solution solution,
        IReadOnlyCollection<string> expectedProjectNames,
        IReadOnlyDictionary<string, string[]>? expectedGeneratedDocuments = null)
    {
        if (solution.Workspace is not AdhocWorkspace)
            throw new ArgumentException("Only explicitly controlled AdhocWorkspace fixtures can use simulated acquisition evidence.", nameof(solution));
        var projects = solution.Projects.ToArray();
        if (expectedProjectNames.Count == 0
            || expectedProjectNames.Distinct(StringComparer.Ordinal).Count() != expectedProjectNames.Count
            || !projects.Select(project => project.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expectedProjectNames.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ArgumentException("The loaded projects must exactly match the independently declared fixture project names.", nameof(expectedProjectNames));
        if (expectedGeneratedDocuments?.Keys.Any(name => !expectedProjectNames.Contains(name, StringComparer.Ordinal)) == true)
            throw new ArgumentException("Generated output was declared for an unexpected project.", nameof(expectedGeneratedDocuments));

        var inputs = new List<ExpectedSemanticInput>();
        var outcomes = new List<SemanticInputOutcome>();
        Add("closure", SemanticInputKind.ManifestClosure, "Explicit deterministic fixture; complete project list validated.");
        foreach (var project in projects)
        {
            var projectPath = WorkspaceCompleteness.ProjectIdentity(project);
            Add($"project:{project.Name}", SemanticInputKind.Project, "Declared fixture project acquired.", projectPath);
            Add($"context:{project.Name}", SemanticInputKind.CompilationContext,
                "Single in-memory C# compilation context with explicit fixture metadata references.", projectPath,
                context: WorkspaceCompleteness.CompilationContextIdentity(project));
            await AddDocumentsAsync(project.Documents, SemanticInputKind.SourceDocument, projectPath);
            await AddDocumentsAsync(project.AdditionalDocuments, SemanticInputKind.AdditionalDocument, projectPath);
            await AddDocumentsAsync(project.AnalyzerConfigDocuments, SemanticInputKind.AnalyzerConfigDocument, projectPath);
            foreach (var reference in project.ProjectReferences)
            {
                var target = solution.GetProject(reference.ProjectId)
                    ?? throw new ArgumentException("A required fixture project reference is absent.", nameof(solution));
                Add($"project-reference:{project.Name}:{target.Name}", SemanticInputKind.ProjectReference,
                    "Target belongs to the explicitly declared fixture project set.", projectPath,
                    WorkspaceCompleteness.ProjectIdentity(target));
            }
            var referenceIndex = 0;
            foreach (var reference in project.MetadataReferences)
            {
                Add($"metadata:{project.Name}:{referenceIndex}", SemanticInputKind.MetadataReference,
                    "Concrete metadata reference supplied by the deterministic fixture builder.", projectPath,
                    WorkspaceCompleteness.MetadataReferenceIdentity(reference, referenceIndex));
                referenceIndex++;
            }

            var expectedGenerated = expectedGeneratedDocuments is not null
                && expectedGeneratedDocuments.TryGetValue(project.Name, out var declaredGenerated) ? declaredGenerated : [];
            if (project.AnalyzerReferences.Any(reference => reference.GetType().DeclaringType != typeof(AdhocProjectBuilder)
                    || reference.GetAnalyzers(project.Language).Length != 0)
                || (project.AnalyzerReferences.Count != 0) != (expectedGenerated.Length != 0))
                throw new ArgumentException("Only explicitly declared fixed-source fixture generators are supported; no analyzers are allowed.", nameof(solution));
            for (var analyzerIndex = 0; analyzerIndex < project.AnalyzerReferences.Count; analyzerIndex++)
                Add($"analyzer-reference:{project.Name}:{analyzerIndex}", SemanticInputKind.AnalyzerReference,
                    "Explicit fixed-source fixture generator reference.", projectPath,
                    WorkspaceCompleteness.AnalyzerReferenceIdentity(project.AnalyzerReferences[analyzerIndex], analyzerIndex));
            var generated = await project.GetSourceGeneratedDocumentsAsync();
            if (!generated.Select(document => document.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(expectedGenerated.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new ArgumentException("Generated documents differ from the independently declared fixture output.", nameof(expectedGeneratedDocuments));
            await AddDocumentsAsync(generated, SemanticInputKind.GeneratedDocument, projectPath);
            Add($"generators:{project.Name}", SemanticInputKind.GeneratorStage,
                expectedGenerated.Length == 0 ? "Fixture has no generator references." : "Explicit fixed-source fixture generator ran and all expected outputs were verified.",
                projectPath, excluded: expectedGenerated.Length == 0);
            Add($"analyzers:{project.Name}", SemanticInputKind.AnalyzerStage,
                "Fixture has no analyzer references.", projectPath, excluded: true);
        }

        foreach (var kind in new[] { SemanticInputKind.EvaluationStage, SemanticInputKind.Sdk, SemanticInputKind.Package,
                     SemanticInputKind.BuildInput, SemanticInputKind.FrameworkReference })
            Add($"not-applicable:{kind}", kind,
                "The declared in-memory fixture uses explicit sources and concrete metadata; it does not evaluate MSBuild, SDK, package or import inputs.", excluded: true);
        var fingerprint = await SolutionRevision.ComputeAsync(solution);
        Add("input-set", SemanticInputKind.WorkspaceInputSet,
            "The deterministic in-memory fixture has no dynamic filesystem membership.", fingerprint: fingerprint);
        Add("snapshot", SemanticInputKind.SourceSnapshot, "Exact immutable fixture solution snapshot.", fingerprint: fingerprint);
        var manifest = new SemanticInputManifest(solution.FilePath, inputs, ManifestDiscoveryState.Closed);
        var provenance = new WorkspaceEvaluatorProvenance(null, solution.FilePath, fingerprint,
            "Explicit in-memory fixture context", [], "NotApplicable: supplied metadata", typeof(Compilation).Assembly.FullName!,
            "Simulated deterministic fixture evidence; only explicitly declared fixed-source generators", "Untrusted", "NoEvaluation");
        WorkspaceAcquisitionRegistry.Attach(solution.Workspace,
            new WorkspaceAcquisitionReceipt(solution, manifest, outcomes, provenance));

        void Add(string id, SemanticInputKind kind, string detail, string? projectPath = null,
            string? path = null, string? fingerprint = null, bool excluded = false, string? context = null)
        {
            inputs.Add(new ExpectedSemanticInput(id, kind, true, ProjectPath: projectPath, Context: context, Path: path, Fingerprint: fingerprint));
            outcomes.Add(new SemanticInputOutcome(id, excluded ? SemanticInputState.Excluded : SemanticInputState.Resolved,
                fingerprint, detail, ExclusionValidated: excluded));
        }

        async Task AddDocumentsAsync(IEnumerable<TextDocument> documents, SemanticInputKind kind, string projectPath)
        {
            foreach (var document in documents)
            {
                var text = await document.GetTextAsync();
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
                Add($"document:{kind}:{document.Id}", kind, "Explicit in-memory fixture document acquired.",
                    projectPath, document.FilePath ?? document.Name, hash);
            }
        }
    }
}
