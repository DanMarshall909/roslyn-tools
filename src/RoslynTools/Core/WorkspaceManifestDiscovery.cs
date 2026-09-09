using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

internal sealed record WorkspaceManifestSeed(
    SemanticInputManifest Manifest,
    ImmutableArray<WorkspaceAcquisitionDiagnostic> Diagnostics);

/// <summary>
/// Seeds requested projects before Roslyn acquisition. It deliberately does not claim evaluated MSBuild closure.
/// Imports, conditions, transitive references, TFMs and generator results need a subsequent authoritative collector.
/// </summary>
internal static class WorkspaceManifestDiscovery
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static async Task<WorkspaceManifestSeed> SeedAsync(string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var inputs = ImmutableArray.CreateBuilder<ExpectedSemanticInput>();
        var diagnostics = ImmutableArray.CreateBuilder<WorkspaceAcquisitionDiagnostic>();
        var selectedPath = Path.GetFullPath(solutionPath);
        var bytes = await File.ReadAllBytesAsync(selectedPath, cancellationToken);
        inputs.Add(new ExpectedSemanticInput("selected-solution", SemanticInputKind.BuildInput, true,
            Path: selectedPath, Fingerprint: Convert.ToHexString(SHA256.HashData(bytes))));

        var projectPaths = new HashSet<string>(PathComparer);
        try
        {
            if (selectedPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = new MemoryStream(bytes);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                var document = XDocument.Load(reader);
                if (document.Root?.Name.LocalName != "Solution")
                    throw new InvalidDataException("The selected .slnx has no Solution root element.");
                var projectIndex = 0;
                foreach (var project in document.Descendants().Where(element => element.Name.LocalName == "Project"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    projectIndex++;
                    try
                    {
                        var path = project.Attribute("Path")?.Value;
                        if (string.IsNullOrWhiteSpace(path))
                            throw new InvalidDataException("A .slnx Project entry has no Path.");
                        projectPaths.Add(ResolveProjectPath(selectedPath, path));
                    }
                    catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
                    {
                        RecordFailure(exception, $"slnx-project:{projectIndex}");
                    }
                }
            }
            else if (selectedPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
                var lineNumber = 0;
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    lineNumber++;
                    if (!line.TrimStart().StartsWith("Project(", StringComparison.Ordinal))
                        continue;
                    try
                    {
                        var match = Regex.Match(line,
                            "^\\s*Project\\(\"(?<type>[^\"]+)\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"(?<path>[^\"]+)\",\\s*\"[^\"]+\"\\s*$",
                            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                        if (!match.Success)
                            throw new InvalidDataException("A solution Project entry could not be accounted for.");
                        // A solution folder is presentation-only, not an evaluated project.
                        if (string.Equals(match.Groups["type"].Value, "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(match.Groups["type"].Value, "{2150E333-8FDC-42A3-9474-1A3956D46DE8}", StringComparison.OrdinalIgnoreCase))
                            continue;
                        projectPaths.Add(ResolveProjectPath(selectedPath, match.Groups["path"].Value));
                    }
                    catch (Exception exception) when (exception is InvalidDataException or ArgumentException or RegexMatchTimeoutException)
                    {
                        RecordFailure(exception, $"sln-project-line:{lineNumber}");
                    }
                }
            }
            else
            {
                throw new InvalidDataException("The selected entry point has no supported pre-acquisition project inventory format.");
            }
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or ArgumentException or RegexMatchTimeoutException)
        {
            RecordFailure(exception);
        }

        foreach (var path in projectPaths.Order(PathComparer))
            inputs.Add(new ExpectedSemanticInput(ProjectInputId(path), SemanticInputKind.Project, true,
                ProjectPath: path, Path: path));
        inputs.AddRange(UnresolvedScopeInputs());
        return new WorkspaceManifestSeed(new SemanticInputManifest(selectedPath, inputs), diagnostics.ToImmutable());

        void RecordFailure(Exception exception, string? inputId = null) =>
            diagnostics.Add(new WorkspaceAcquisitionDiagnostic("manifest.project-discovery", DiagnosticSeverity.Warning,
                CompletenessDiagnosticImpact.RequiredInputIncomplete, "manifest-discovery", exception.Message, inputId));
    }

    internal static string ProjectInputId(string projectPath) => "project:" + Path.GetFullPath(projectPath);

    internal static IEnumerable<ExpectedSemanticInput> UnresolvedScopeInputs()
    {
        yield return new ExpectedSemanticInput("scope:closure", SemanticInputKind.ManifestClosure, true);
        yield return new ExpectedSemanticInput("scope:contexts", SemanticInputKind.CompilationContext, true);
        yield return new ExpectedSemanticInput("scope:references", SemanticInputKind.ProjectReference, true);
        yield return new ExpectedSemanticInput("scope:documents", SemanticInputKind.SourceDocument, true);
        yield return new ExpectedSemanticInput("scope:build-inputs", SemanticInputKind.BuildInput, true);
        yield return new ExpectedSemanticInput("scope:sdk", SemanticInputKind.Sdk, true);
        yield return new ExpectedSemanticInput("scope:packages", SemanticInputKind.Package, true);
        yield return new ExpectedSemanticInput("scope:framework-references", SemanticInputKind.FrameworkReference, true);
        yield return new ExpectedSemanticInput("scope:evaluation", SemanticInputKind.EvaluationStage, true);
        yield return new ExpectedSemanticInput("scope:generators", SemanticInputKind.GeneratorStage, true);
        yield return new ExpectedSemanticInput("scope:analyzers", SemanticInputKind.AnalyzerStage, true);
        yield return new ExpectedSemanticInput("scope:snapshot", SemanticInputKind.SourceSnapshot, true);
    }

    private static string ResolveProjectPath(string solutionPath, string projectPath)
        => Path.GetFullPath(projectPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar),
            Path.GetDirectoryName(solutionPath)!);
}
