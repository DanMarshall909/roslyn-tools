using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynTools.Core;

/// <summary>Whether the loaded semantic inputs can support absence-based conclusions.</summary>
internal enum WorkspaceCompletenessState
{
    Complete,
    Partial,
    Failed
}

/// <summary>Compatibility projection over an explicit acquisition assessment used to gate proof-sensitive operations.</summary>
internal sealed record WorkspaceCompleteness(
    WorkspaceCompletenessState State,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<RtWorkspaceInput> Inputs,
    IReadOnlyList<string> RequestedProjects,
    IReadOnlyList<string> FailedProjects,
    string CompilerVersion,
    string EvaluatorPolicy)
{
    internal WorkspaceAcquisitionResult? Acquisition { get; init; }
    internal IReadOnlyList<WorkspaceAcquisitionDiagnostic> StructuredDiagnostics => Acquisition?.Diagnostics ?? [];

    internal RtWorkspaceProvenance ToResultProvenance(Solution solution) => new(
        State.ToString(),
        solution.ProjectIds.Count,
        Diagnostics,
        Inputs,
        RequestedProjects,
        FailedProjects,
        CompilerVersion,
        EvaluatorPolicy,
        Acquisition?.Provenance,
        Acquisition?.Manifest.DiscoveryState.ToString());

    internal RtStructuredDiagnostic[] ToResultDiagnostics() => StructuredDiagnostics
        .Select(diagnostic =>
        {
            var context = new Dictionary<string, string>
            {
                ["origin"] = diagnostic.Origin,
                ["completenessImpact"] = diagnostic.Impact.ToString()
            };
            if (diagnostic.InputId is { } inputId)
                context["inputId"] = inputId;
            if (diagnostic.ProjectPath is { } projectPath)
                context["projectPath"] = projectPath;
            return new RtStructuredDiagnostic(
                diagnostic.Id,
                diagnostic.Severity.ToString(),
                diagnostic.Message,
                context);
        })
        .ToArray();

    internal static async Task<WorkspaceCompleteness> AssessSemanticAsync(
        Solution solution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceDiagnosticRegistry.Capture(solution.Workspace);
        var (declaredManifest, receipt) = WorkspaceAcquisitionRegistry.Get(solution.Workspace);
        var recordedProvenance = receipt?.Provenance ?? WorkspaceAcquisitionRegistry.GetProvenance(solution.Workspace);
        var manifest = declaredManifest ?? new SemanticInputManifest(solution.FilePath,
            WorkspaceManifestDiscovery.UnresolvedScopeInputs());
        var outcomes = receipt?.Outcomes.ToList() ?? [];
        var diagnostics = new List<WorkspaceAcquisitionDiagnostic>();
        var observed = new List<RtWorkspaceInput>();
        var projects = solution.Projects.ToArray();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var usableProjects = new HashSet<string>(comparer);
        var failedProjects = new HashSet<string>(comparer);

        if (outcomes.GroupBy(outcome => outcome.InputId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            diagnostics.Add(new WorkspaceAcquisitionDiagnostic("acquisition.duplicate-receipt", DiagnosticSeverity.Warning,
                CompletenessDiagnosticImpact.RequiredInputIncomplete, "acquisition", "Acquisition receipts contain duplicate input identities."));

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = ProjectIdentity(project);
            observed.Add(new RtWorkspaceInput($"observed-project:{project.Id}", "project", "Loaded", true, identity));
            AddDocuments(observed, project.Documents, "source-document", identity);
            AddDocuments(observed, project.AdditionalDocuments, "additional-document", identity);
            AddDocuments(observed, project.AnalyzerConfigDocuments, "analyzer-config", identity);
            foreach (var reference in project.ProjectReferences)
            {
                var referenced = solution.GetProject(reference.ProjectId);
                observed.Add(new RtWorkspaceInput($"project-reference:{project.Id}:{reference.ProjectId}",
                    "project-reference", referenced is null ? "Failed" : "Resolved", true, identity,
                    referenced is null ? reference.ProjectId.ToString() : ProjectIdentity(referenced)));
                if (referenced is null)
                {
                    failedProjects.Add(identity);
                    diagnostics.Add(new WorkspaceAcquisitionDiagnostic("acquisition.project-reference", DiagnosticSeverity.Error,
                        CompletenessDiagnosticImpact.RequiredInputIncomplete, "acquisition",
                        "A loaded project's required project reference has no semantic project.", ProjectPath: identity));
                }
            }

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                failedProjects.Add(identity);
                observed.Add(new RtWorkspaceInput($"compilation:{project.Id}", "compilation", "Failed", true, identity));
                diagnostics.Add(new WorkspaceAcquisitionDiagnostic("acquisition.compilation", DiagnosticSeverity.Error,
                    CompletenessDiagnosticImpact.RequiredInputIncomplete, "acquisition",
                    "The requested project did not produce a compilation.", ProjectPath: identity));
                continue;
            }

            usableProjects.Add(identity);
            observed.Add(new RtWorkspaceInput($"compilation:{project.Id}", "compilation", "Resolved", true, identity));
            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
            {
                // These compiler warning IDs report analyzer/generator acquisition failures, not ordinary source warnings.
                var degrades = diagnostic.Severity == DiagnosticSeverity.Error
                    || diagnostic.Id is "CS8032" or "CS8784" or "CS8785" or "CS9057";
                diagnostics.Add(new WorkspaceAcquisitionDiagnostic(diagnostic.Id, diagnostic.Severity,
                    degrades ? CompletenessDiagnosticImpact.RequiredInputIncomplete : CompletenessDiagnosticImpact.None,
                    "compiler", diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    $"compiler:{project.Id}:{diagnostic.Location.SourceTree?.FilePath}:{diagnostic.Location.SourceSpan.Start}", identity));
                if (degrades)
                    failedProjects.Add(identity);
            }
        }

        var requested = manifest.Inputs.Where(input => input.Kind == SemanticInputKind.Project).ToArray();
        foreach (var input in requested)
        {
            var identity = input.ProjectPath ?? input.Path ?? input.Id;
            var loaded = projects.Any(project => comparer.Equals(ProjectIdentity(project), identity));
            MergeObservation(outcomes, new SemanticInputOutcome(input.Id,
                loaded ? SemanticInputState.Loaded : SemanticInputState.Failed,
                Detail: loaded ? "The requested project has a loaded Roslyn project. Context closure is assessed separately."
                    : "The requested project is absent from the acquired solution."), diagnostics);
            if (!loaded || outcomes.Any(outcome => outcome.InputId == input.Id
                    && outcome.State is SemanticInputState.Failed or SemanticInputState.PolicyBlocked))
                failedProjects.Add(identity);
        }

        foreach (var input in manifest.Inputs.Where(input => input.Kind == SemanticInputKind.BuildInput
                     && input.Path is not null && input.Fingerprint is not null))
        {
            if (outcomes.Any(outcome => outcome.InputId == input.Id
                    && outcome.State is SemanticInputState.Failed or SemanticInputState.PolicyBlocked or SemanticInputState.Unknown))
                continue;
            try
            {
                var contents = await File.ReadAllBytesAsync(input.Path!, cancellationToken);
                MergeObservation(outcomes, new SemanticInputOutcome(input.Id, SemanticInputState.Resolved,
                    Convert.ToHexString(SHA256.HashData(contents))), diagnostics);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                MergeObservation(outcomes, new SemanticInputOutcome(input.Id, SemanticInputState.Failed, Detail: exception.Message), diagnostics);
            }
        }
        foreach (var input in manifest.Inputs.Where(input => input.Kind == SemanticInputKind.WorkspaceInputSet))
        {
            if (input.Context != "repository-membership-v1")
                continue;
            if (outcomes.Any(outcome => outcome.InputId == input.Id
                    && outcome.State is SemanticInputState.Failed or SemanticInputState.PolicyBlocked or SemanticInputState.Unknown))
                continue;

            if (input.Fingerprint is null
                || solution.FilePath is null
                || !RepositoryTrustContext.IsExplicitlyTrustedFor(solution.FilePath))
            {
                MergeObservation(outcomes, new SemanticInputOutcome(
                    input.Id,
                    SemanticInputState.Failed,
                    Detail: "Workspace input membership can no longer be verified against a trusted repository identity."), diagnostics);
                continue;
            }

            try
            {
                var identity = RepositoryTrustContext.ResolveWorkspaceIdentity(solution.FilePath);
                var sdkIdentity = recordedProvenance?.SdkIdentity ?? "Unknown";
                var current = await WorkspaceInputFingerprint.CreateForAcquisition(sdkIdentity)
                    .CaptureAsync(identity, solution, cancellationToken);
                MergeObservation(outcomes, new SemanticInputOutcome(
                    input.Id,
                    current.Reusable && current.SnapshotConsistent
                        ? SemanticInputState.Resolved
                        : SemanticInputState.Failed,
                    current.Value,
                    current.Reusable && current.SnapshotConsistent
                        ? null
                        : "Workspace input membership or loaded-snapshot consistency is no longer proven."), diagnostics);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException)
            {
                MergeObservation(outcomes, new SemanticInputOutcome(
                    input.Id,
                    SemanticInputState.Failed,
                    Detail: exception.Message), diagnostics);
            }
        }
        foreach (var input in manifest.Inputs.Where(input => outcomes.All(outcome => outcome.InputId != input.Id)))
            outcomes.Add(new SemanticInputOutcome(input.Id, SemanticInputState.Unknown,
                Detail: "The acquisition provider has not supplied an authoritative outcome for this required input or stage."));

        if (manifest.DiscoveryState == ManifestDiscoveryState.Closed)
            diagnostics.AddRange(await WorkspaceObservedInputVerifier.VerifyAsync(solution, manifest, outcomes, cancellationToken));

        var snapshotMatches = receipt is not null && ReferenceEquals(receipt.Solution, solution)
            && receipt.Provenance.SourceFingerprint is not null
            && string.Equals(receipt.Provenance.SourceFingerprint,
                await SolutionRevision.ComputeAsync(solution, cancellationToken), StringComparison.Ordinal);
        // Snapshot only after deferred compilations and fingerprint reads: late acquisition events remain visible.
        diagnostics.AddRange(WorkspaceDiagnosticRegistry.GetStructured(solution.Workspace));
        var hasUsableScope = requested.Length > 0
            ? requested.Any(input => usableProjects.Contains(input.ProjectPath ?? input.Path ?? input.Id))
            : declaredManifest is null && usableProjects.Count > 0;
        var decision = WorkspaceCompletenessPolicy.Evaluate(manifest, outcomes, diagnostics, hasUsableScope, snapshotMatches);
        var provenance = recordedProvenance ?? new WorkspaceEvaluatorProvenance(
            null, solution.FilePath, null,
            "Unknown", [], "Unknown", GetCompilerVersion(), "Unverified legacy workspace", "NotEstablished", "Unknown");
        var allDiagnostics = diagnostics.Concat(decision.Reasons.Select((reason, index) =>
            new WorkspaceAcquisitionDiagnostic($"acquisition.evidence.{index + 1}", DiagnosticSeverity.Warning,
                CompletenessDiagnosticImpact.RequiredInputIncomplete, "acquisition", reason))).ToImmutableArray();
        var acquisition = new WorkspaceAcquisitionResult(decision.State, manifest, outcomes.ToImmutableArray(),
            allDiagnostics, provenance, decision.Reasons);
        var expectedInputs = manifest.Inputs.Select(input =>
        {
            var outcome = outcomes.First(item => item.InputId == input.Id);
            return new RtWorkspaceInput(input.Id, input.Kind.ToString(), outcome.State.ToString(), input.Required,
                input.ProjectPath, outcome.Detail, input.RequiredForMinimumScope, input.Context, input.Path,
                input.Fingerprint, outcome.ObservedFingerprint, outcome.ExclusionValidated);
        });
        return new WorkspaceCompleteness(decision.State,
            allDiagnostics.Select(diagnostic => $"{diagnostic.ProjectPath ?? diagnostic.Origin}: {diagnostic.Id}: {diagnostic.Message}").ToImmutableArray(),
            expectedInputs.Concat(observed).ToImmutableArray(),
            requested.Select(input => input.ProjectPath ?? input.Path ?? input.Id).Order(comparer).ToImmutableArray(),
            failedProjects.Order(comparer).ToImmutableArray(), GetCompilerVersion(),
            $"{provenance.EvaluatorMode}; {provenance.AnalyzerGeneratorPolicy}")
        {
            Acquisition = acquisition
        };
    }

    internal static string ProjectIdentity(Project project)
        => project.FilePath is null ? $"roslyn-project:{project.Id.Id:D}" : Path.GetFullPath(project.FilePath);

    // Snapshot-local identity only: this is not authoritative production TFM/configuration evidence.
    internal static string CompilationContextIdentity(Project project) => $"roslyn-context:{project.Id.Id:D}";

    internal static string AnalyzerReferenceIdentity(AnalyzerReference reference, int index)
        => $"analyzer:{index}:{reference.FullPath ?? "in-memory"}";

    internal static string MetadataReferenceIdentity(MetadataReference reference, int index)
        => $"metadata:{index}:{reference.Display ?? "in-memory"}";

    private static void MergeObservation(List<SemanticInputOutcome> outcomes, SemanticInputOutcome observation,
        List<WorkspaceAcquisitionDiagnostic> diagnostics)
    {
        var previous = outcomes.Where(outcome => outcome.InputId == observation.InputId).ToArray();
        if (previous.Length == 0)
        {
            outcomes.Add(observation);
            return;
        }
        // A loaded object or readable file cannot upgrade missing, blocked, failed or contradictory collector evidence.
        if (previous.Length != 1
            || previous[0].State is SemanticInputState.Unknown or SemanticInputState.Failed or SemanticInputState.PolicyBlocked)
            return;
        if (observation.State == SemanticInputState.Failed)
        {
            outcomes.Remove(previous[0]);
            outcomes.Add(observation);
            return;
        }
        if (observation.ObservedFingerprint is not null && previous[0].ObservedFingerprint is not null
            && !string.Equals(observation.ObservedFingerprint, previous[0].ObservedFingerprint, StringComparison.Ordinal))
            diagnostics.Add(new WorkspaceAcquisitionDiagnostic("acquisition.contradictory-fingerprint", DiagnosticSeverity.Warning,
                CompletenessDiagnosticImpact.RequiredInputIncomplete, "acquisition",
                "The observed input fingerprint contradicts its collector receipt.", observation.InputId));
    }

    private static void AddDocuments(List<RtWorkspaceInput> inputs, IEnumerable<TextDocument> documents, string kind, string project)
    {
        foreach (var document in documents)
            inputs.Add(new RtWorkspaceInput($"{kind}:{document.Id}", kind, "Loaded", true, project, document.FilePath ?? document.Name));
    }

    private static string GetCompilerVersion() => typeof(Compilation).Assembly.GetName().Version?.ToString() ?? "Unknown";
}
