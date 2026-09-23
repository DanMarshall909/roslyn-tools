using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
/// Checks a collector's closed declaration against observable inputs in its exact Roslyn snapshot.
/// This detects contradictory closure claims; it cannot discover MSBuild inputs or certify production closure.
/// </summary>
internal static class WorkspaceObservedInputVerifier
{
    internal static async Task<IReadOnlyList<WorkspaceAcquisitionDiagnostic>> VerifyAsync(Solution solution,
        SemanticInputManifest manifest, IReadOnlyCollection<SemanticInputOutcome> outcomes, CancellationToken cancellationToken)
    {
        var observed = new List<ExpectedSemanticInput>();
        var diagnostics = new List<WorkspaceAcquisitionDiagnostic>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = WorkspaceCompleteness.ProjectIdentity(project);
            var context = WorkspaceCompleteness.CompilationContextIdentity(project);
            Add(SemanticInputKind.Project, identity);
            Add(SemanticInputKind.CompilationContext, identity, context: context);
            await AddDocumentsAsync(project.Documents, SemanticInputKind.SourceDocument, identity, context);
            await AddDocumentsAsync(project.AdditionalDocuments, SemanticInputKind.AdditionalDocument, identity, context);
            await AddDocumentsAsync(project.AnalyzerConfigDocuments, SemanticInputKind.AnalyzerConfigDocument, identity, context);
            await AddDocumentsAsync(await project.GetSourceGeneratedDocumentsAsync(cancellationToken), SemanticInputKind.GeneratedDocument, identity, context);
            foreach (var reference in project.ProjectReferences)
                Add(SemanticInputKind.ProjectReference, identity,
                    solution.GetProject(reference.ProjectId) is { } target ? WorkspaceCompleteness.ProjectIdentity(target) : reference.ProjectId.ToString(), context);
            var index = 0;
            foreach (var reference in project.MetadataReferences)
                Add(SemanticInputKind.MetadataReference, identity, WorkspaceCompleteness.MetadataReferenceIdentity(reference, index++), context);
            for (var analyzerIndex = 0; analyzerIndex < project.AnalyzerReferences.Count; analyzerIndex++)
                Add(SemanticInputKind.AnalyzerReference, identity,
                    WorkspaceCompleteness.AnalyzerReferenceIdentity(project.AnalyzerReferences[analyzerIndex], analyzerIndex), context);
        }

        foreach (var actual in observed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = manifest.Inputs.Where(expected => Matches(expected, actual)).ToArray();
            if (matches.Length != 1 || !matches[0].Required)
                Report("acquisition.undeclared-observed-input", actual,
                    $"Observed {actual.Kind} '{actual.Path ?? actual.Context ?? actual.ProjectPath}' has {matches.Length} declarations; exactly one required declaration is needed in the closed manifest.");
            else if (matches[0].Fingerprint is not null
                && !string.Equals(matches[0].Fingerprint, actual.Fingerprint, StringComparison.Ordinal)
                && IsDocument(actual.Kind))
                Report("acquisition.observed-fingerprint", matches[0],
                    "An observed document contradicts its declared content fingerprint.");
            else if (outcomes.Any(outcome => outcome.InputId == matches[0].Id && outcome.State == SemanticInputState.Excluded))
                Report("acquisition.excluded-observed-input", matches[0],
                    "An excluded input is present in the semantic snapshot; its exclusion cannot establish absence proof.");
        }
        foreach (var expected in manifest.Inputs.Where(input => IsObservable(input.Kind)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!observed.Any(actual => Matches(expected, actual))
                && outcomes.Any(outcome => outcome.InputId == expected.Id
                    && outcome.State is SemanticInputState.Loaded or SemanticInputState.Resolved))
                Report("acquisition.unobserved-successful-input", expected,
                    "A successful acquisition receipt has no matching input in the semantic snapshot.");
        }
        return diagnostics;

        void Add(SemanticInputKind kind, string projectPath, string? path = null, string? context = null, string? fingerprint = null)
            => observed.Add(new ExpectedSemanticInput($"observed:{kind}:{observed.Count}", kind, true,
                ProjectPath: projectPath, Context: context, Path: path, Fingerprint: fingerprint));

        async Task AddDocumentsAsync(IEnumerable<TextDocument> documents, SemanticInputKind kind, string projectPath, string context)
        {
            foreach (var document in documents)
            {
                var text = await document.GetTextAsync(cancellationToken);
                Add(kind, projectPath, document.FilePath ?? document.Name,
                    context, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
            }
        }

        bool Matches(ExpectedSemanticInput expected, ExpectedSemanticInput actual)
            => expected.Kind == actual.Kind
                && comparer.Equals(expected.Kind == SemanticInputKind.Project ? expected.ProjectPath ?? expected.Path ?? expected.Id : expected.ProjectPath,
                    actual.ProjectPath)
                && expected.Kind switch
                {
                    SemanticInputKind.Project => true,
                    SemanticInputKind.CompilationContext => string.Equals(expected.Context, actual.Context, StringComparison.Ordinal),
                    _ => comparer.Equals(expected.Path, actual.Path)
                         && (expected.Context is null || string.Equals(expected.Context, actual.Context, StringComparison.Ordinal))
                };

        void Report(string id, ExpectedSemanticInput input, string message)
            => diagnostics.Add(new WorkspaceAcquisitionDiagnostic(id, DiagnosticSeverity.Warning,
                CompletenessDiagnosticImpact.RequiredInputIncomplete, "acquisition", message, input.Id, input.ProjectPath));
    }

    private static bool IsDocument(SemanticInputKind kind)
        => kind is SemanticInputKind.SourceDocument or SemanticInputKind.AdditionalDocument
            or SemanticInputKind.AnalyzerConfigDocument or SemanticInputKind.GeneratedDocument;

    private static bool IsObservable(SemanticInputKind kind)
        => IsDocument(kind) || kind is SemanticInputKind.Project or SemanticInputKind.CompilationContext
            or SemanticInputKind.ProjectReference or SemanticInputKind.MetadataReference or SemanticInputKind.AnalyzerReference;
}
