using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTools.Tests;

/// <summary>
///     In-memory replacement for <see cref="TestProjectBuilder" />. Builds an
///     <see cref="AdhocWorkspace" /> directly with BCL metadata references from the
///     test host's TRUSTED_PLATFORM_ASSEMBLIES — skips <c>dotnet build</c> and
///     MSBuildWorkspace.OpenSolutionAsync entirely.
///     Still writes source files to disk so commands that write through
///     <see cref="RoslynTools.Core.SolutionWriter" /> (which uses File.WriteAllText on
///     each document's FilePath) round-trip correctly and tests can verify via
///     <see cref="ReadSourceAsync" />.
/// </summary>
public sealed class AdhocProjectBuilder : IDisposable
{
    private static readonly string BaseOutputDir =
        Path.Combine(Path.GetDirectoryName(typeof(AdhocProjectBuilder).Assembly.Location)!, "TestOutput");

    private static readonly ImmutableArray<MetadataReference> BclReferences = BuildBclReferences();
    private readonly ProjectDef _default;
    private readonly LanguageVersion _languageVersion;

    private readonly List<ProjectDef> _projects = new();

    /// <summary>
    ///     Starts a fixture holding a single default project.
    /// </summary>
    /// <param name="projectName">Name given to the default project.</param>
    /// <param name="languageVersion">
    ///     Language version every project in the fixture parses with. A repository under analysis
    ///     picks its own <c>LangVersion</c>, so a fixture has to be able to say which one it models:
    ///     preview syntax such as a <c>union</c> declaration parses as a type declaration under
    ///     <see cref="LanguageVersion.Preview" /> and mis-parses into an unrelated member under
    ///     <see cref="LanguageVersion.Latest" />.
    /// </param>
    public AdhocProjectBuilder(string? projectName = null, LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        Directory = Path.Combine(BaseOutputDir, Guid.NewGuid().ToString("N"));
        _languageVersion = languageVersion;
        _default = new ProjectDef(projectName ?? "TestProject");
        _projects.Add(_default);
    }

    public string Directory { get; }

    public string ProjectDir => Path.Combine(Directory, _default.Name);
    public string SolutionPath => Path.Combine(Directory, _default.Name + ".sln");

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    /// <summary>
    ///     Add a source file to the default project. Path is relative to the project directory.
    /// </summary>
    public AdhocProjectBuilder AddSource(string relativePath, string content)
    {
        _default.Files.Add((relativePath, content));
        return this;
    }

    /// <summary>
    ///     Add a non-source file to the default project — an ILLink descriptor, say. These land in
    ///     <see cref="Project.AdditionalDocuments" />, not <see cref="Project.Documents" />.
    /// </summary>
    public AdhocProjectBuilder AddAdditionalDocument(string relativePath, string content)
    {
        _default.AdditionalFiles.Add((relativePath, content));
        return this;
    }

    /// <summary>
    ///     Create (or get) a named project in the solution. Use the returned handle to add
    ///     sources and reference other projects.
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
    ///     Write sources to disk and materialize an AdhocWorkspace. Returns the same tuple
    ///     shape <see cref="RoslynTools.Core.WorkspaceLoader.LoadAsync" /> returns.
    /// </summary>
    public (Workspace Workspace, Solution Solution) Build()
    {
        System.IO.Directory.CreateDirectory(Directory);

        // Pre-assign IDs so ProjectReferences can be wired in a second pass.
        var projectIds = _projects.ToDictionary(p => p.Name, _ => ProjectId.CreateNewId());

        var projectInfos = new List<ProjectInfo>();
        foreach (var p in _projects)
        {
            var projectDir = Path.Combine(Directory, p.Name);
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
                    Path.GetFileName(rel),
                    filePath: fullPath,
                    loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create(), fullPath))));
            }

            var additionalDocs = new List<DocumentInfo>();
            foreach (var (rel, content) in p.AdditionalFiles)
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, rel));
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content, Encoding.UTF8);

                var text = SourceText.From(content, Encoding.UTF8);
                additionalDocs.Add(DocumentInfo.Create(
                    DocumentId.CreateNewId(projectIds[p.Name]),
                    Path.GetFileName(rel),
                    filePath: fullPath,
                    loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create(), fullPath))));
            }

            var projectRefs = p.ProjectReferenceNames
                .Select(n => new ProjectReference(projectIds[n]))
                .ToList();

            projectInfos.Add(ProjectInfo.Create(
                projectIds[p.Name],
                VersionStamp.Create(),
                p.Name,
                p.Name,
                LanguageNames.CSharp,
                Path.Combine(projectDir, p.Name + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable),
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(_languageVersion),
                documents: docs,
                projectReferences: projectRefs,
                metadataReferences: BclReferences)
                .WithAdditionalDocuments(additionalDocs));
        }

        var workspace = new AdhocWorkspace();
        foreach (var info in projectInfos)
            workspace.AddProject(info);

        return (workspace, workspace.CurrentSolution);
    }

    /// <summary>
    ///     Read the current on-disk content of a source file in the default project.
    /// </summary>
    public Task<string> ReadSourceAsync(string relativePath)
    {
        return File.ReadAllTextAsync(Path.Combine(Directory, _default.Name, relativePath));
    }

    /// <summary>
    ///     Check if a source file exists on disk in the default project.
    /// </summary>
    public bool SourceExists(string relativePath)
    {
        return File.Exists(Path.Combine(Directory, _default.Name, relativePath));
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
        internal ProjectDef(string name)
        {
            Name = name;
        }

        public string Name { get; }
        internal List<(string RelativePath, string Content)> Files { get; } = new();
        internal List<(string RelativePath, string Content)> AdditionalFiles { get; } = new();
        internal List<string> ProjectReferenceNames { get; } = new();

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
