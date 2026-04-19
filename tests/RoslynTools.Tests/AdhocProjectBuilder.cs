using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTools.Tests;

/// <summary>
/// In-memory replacement for <see cref="TestProjectBuilder"/>. Builds an
/// <see cref="AdhocWorkspace"/> directly with BCL metadata references from the
/// test host's TRUSTED_PLATFORM_ASSEMBLIES — skips <c>dotnet build</c> and
/// MSBuildWorkspace.OpenSolutionAsync entirely.
///
/// Still writes source files to disk so commands that write through
/// <see cref="RoslynTools.Core.SolutionWriter"/> (which uses File.WriteAllText on
/// each document's FilePath) round-trip correctly and tests can verify via
/// <see cref="ReadSourceAsync"/>.
/// </summary>
public sealed class AdhocProjectBuilder : IDisposable
{
    private static readonly string BaseOutputDir =
        Path.Combine(Path.GetDirectoryName(typeof(AdhocProjectBuilder).Assembly.Location)!, "TestOutput");

    private static readonly ImmutableArray<MetadataReference> BclReferences = BuildBclReferences();

    private readonly string _dir;
    private readonly List<ProjectDef> _projects = new();
    private readonly ProjectDef _default;

    public string Directory => _dir;
    public string ProjectDir => Path.Combine(_dir, _default.Name);
    public string SolutionPath => Path.Combine(_dir, _default.Name + ".sln");

    public AdhocProjectBuilder(string? projectName = null)
    {
        _dir = Path.Combine(BaseOutputDir, Guid.NewGuid().ToString("N"));
        _default = new ProjectDef(projectName ?? "TestProject");
        _projects.Add(_default);
    }

    /// <summary>
    /// Add a source file to the default project. Path is relative to the project directory.
    /// </summary>
    public AdhocProjectBuilder AddSource(string relativePath, string content)
    {
        _default.Files.Add((relativePath, content));
        return this;
    }

    /// <summary>
    /// Create (or get) a named project in the solution. Use the returned handle to add
    /// sources and reference other projects.
    /// </summary>
    public ProjectDef AddProject(string name)
    {
        var existing = _projects.FirstOrDefault(p => p.Name == name);
        if (existing is not null)
            return existing;
        var proj = new ProjectDef(name);
        _projects.Add(proj);
        return proj;
    }

    /// <summary>
    /// Write sources to disk and materialize an AdhocWorkspace. Returns the same tuple
    /// shape <see cref="RoslynTools.Core.WorkspaceLoader.LoadAsync"/> returns.
    /// </summary>
    public (Workspace Workspace, Solution Solution) Build()
    {
        System.IO.Directory.CreateDirectory(_dir);

        // Pre-assign IDs so ProjectReferences can be wired in a second pass.
        var projectIds = _projects.ToDictionary(p => p.Name, _ => ProjectId.CreateNewId());

        var projectInfos = new List<ProjectInfo>();
        foreach (var p in _projects)
        {
            var projectDir = Path.Combine(_dir, p.Name);
            System.IO.Directory.CreateDirectory(projectDir);

            var docs = new List<DocumentInfo>();
            foreach (var (rel, content) in p.Files)
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, rel));
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content, Encoding.UTF8);

                var text = SourceText.From(content, Encoding.UTF8);
                docs.Add(DocumentInfo.Create(
                    DocumentId.CreateNewId(projectIds[p.Name]),
                    name: Path.GetFileName(rel),
                    filePath: fullPath,
                    loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create(), fullPath))));
            }

            var projectRefs = p.ProjectReferenceNames
                .Select(n => new ProjectReference(projectIds[n]))
                .ToList();

            projectInfos.Add(ProjectInfo.Create(
                projectIds[p.Name],
                VersionStamp.Create(),
                name: p.Name,
                assemblyName: p.Name,
                language: LanguageNames.CSharp,
                filePath: Path.Combine(projectDir, p.Name + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable),
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                documents: docs,
                projectReferences: projectRefs,
                metadataReferences: BclReferences));
        }

        var workspace = new AdhocWorkspace();
        foreach (var info in projectInfos)
            workspace.AddProject(info);

        return (workspace, workspace.CurrentSolution);
    }

    /// <summary>
    /// Read the current on-disk content of a source file in the default project.
    /// </summary>
    public Task<string> ReadSourceAsync(string relativePath) =>
        File.ReadAllTextAsync(Path.Combine(_dir, _default.Name, relativePath));

    /// <summary>
    /// Check if a source file exists on disk in the default project.
    /// </summary>
    public bool SourceExists(string relativePath) =>
        File.Exists(Path.Combine(_dir, _default.Name, relativePath));

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(_dir))
                System.IO.Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static ImmutableArray<MetadataReference> BuildBclReferences()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not set.");

        return tpa.Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray();
    }

    public sealed class ProjectDef
    {
        public string Name { get; }
        internal List<(string RelativePath, string Content)> Files { get; } = new();
        internal List<string> ProjectReferenceNames { get; } = new();

        internal ProjectDef(string name) => Name = name;

        public ProjectDef AddSource(string relativePath, string content)
        {
            Files.Add((relativePath, content));
            return this;
        }

        public ProjectDef ReferenceProject(string otherProjectName)
        {
            if (!ProjectReferenceNames.Contains(otherProjectName))
                ProjectReferenceNames.Add(otherProjectName);
            return this;
        }
    }
}
