using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

// Deliberately independent of the constructor-introduction fixture and planner output.
internal sealed record StaticDependencyReuseFixture(
    AdhocWorkspace Workspace,
    Solution Solution,
    string Root,
    IReadOnlyDictionary<string, string> Sources,
    StaticDependencyIntroductionRequest Request,
    WorkspaceAcquisitionReceipt? Receipt) : IDisposable
{
    internal const string ProjectName = "ExplicitReuse";
    internal const string ConsumerId = "T:Fixture.Consumer";
    internal const string FieldId = "F:Fixture.Consumer._existing";
    internal const string ReceiverId = "T:Fixture.IReader";
    internal const string SourceId = "M:System.IO.File.ReadAllText(System.String)";
    internal const string TargetId = "M:Fixture.IReader.ReadAllText(System.String)";
    internal const string ConstructorId = "M:Fixture.Consumer.#ctor(Fixture.IReader,System.String)";
    internal const string ConstructorCall = "FileAlias.ReadAllText(path: Trace(path, \"-ctor\"))";
    internal const string MethodCall = "System.IO.File.ReadAllText(Trace(path, \"-body\"))";
    internal const string Assignment = "this._existing = supplied;";

    internal const string Contracts = """
        namespace Fixture;
        internal interface IReader
        {
            string ReadAllText(string path);
        }
        internal sealed class BclReader : IReader
        {
            public string ReadAllText(string path) => System.IO.File.ReadAllText(path);
        }
        """;

    internal const string Consumer = """
        using FileAlias = System.IO.File;
        namespace Fixture;
        internal sealed class Consumer
        {
            private readonly IReader _existing;
            private readonly string _initial;
            private static int _ticks;
            internal Consumer(IReader supplied, string path)
            {
                this._existing = supplied;
                _initial = FileAlias.ReadAllText(path: Trace(path, "-ctor"));
            }
            internal string Read(string path) => _initial + System.IO.File.ReadAllText(Trace(path, "-body"));
            private static string Trace(string value, string suffix) => value + suffix + ++_ticks;
        }
        """;

    internal const string Callers = """
        namespace Fixture;
        internal static class Callers
        {
            internal static string First(IReader input, string path)
            {
                var consumer = new Consumer(input, path);
                return consumer.Read(path);
            }
            internal static string Second(IReader alternate, string path)
            {
                Consumer consumer = new(supplied: alternate, path: path);
                return consumer.Read(path);
            }
        }
        """;

    internal static string MethodOnlyConsumer => Consumer.Replace(ConstructorCall, "path", StringComparison.Ordinal);

    internal string PathFor(string name) => Path.Combine(Root, name);

    internal static Document Document(Solution solution, string name) =>
        solution.Projects.Single(project => project.Name == ProjectName).Documents.Single(document => document.Name == name);

    internal static async Task<Compilation> AssertCompilesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = (await project.GetCompilationAsync()).ShouldNotBeNull();
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty("fixture compilation must succeed before testing planner behavior");
        }
        return (await solution.Projects.Single(project => project.Name == ProjectName).GetCompilationAsync()).ShouldNotBeNull();
    }

    internal static async Task AssertCompleteAsync(Solution solution)
    {
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
        completeness.State.ShouldBe(WorkspaceCompletenessState.Complete, string.Join("; ", completeness.Diagnostics));
        WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence.ShouldBeTrue();
        completeness.RequestedProjects.Order(StringComparer.Ordinal)
            .ShouldBe(solution.Projects.Select(project => project.FilePath!).Order(StringComparer.Ordinal));
    }

    internal static AdhocWorkspace CreateWorkspace(string root, IReadOnlyDictionary<string, string> sources,
        bool linked = false, string? selectedPath = null)
    {
        var workspace = new AdhocWorkspace();
        var references = new[]
        {
            typeof(object).Assembly.Location, typeof(File).Assembly.Location, typeof(Encoding).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")
        }.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        foreach (var name in linked ? new[] { ProjectName, "LinkedContext" } : [ProjectName])
        {
            var project = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(project, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: Path.Combine(root, name + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12), metadataReferences: references));
            foreach (var (file, source) in sources)
                workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(project), file,
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())),
                    filePath: file == "Consumer.cs" && selectedPath is not null ? selectedPath : Path.Combine(root, file)));
        }
        return workspace;
    }

    internal static async Task<StaticDependencyReuseFixture> CreateAsync(string? consumer = null,
        string? contracts = null, string? callers = null, bool linked = false, bool acquire = true,
        string? selectedPath = null)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Contracts.cs"] = contracts ?? Contracts,
            ["Consumer.cs"] = consumer ?? Consumer,
            ["Callers.cs"] = callers ?? Callers
        };
        var root = Path.Combine(Path.GetTempPath(), "rt153-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (name, source) in sources)
            await File.WriteAllTextAsync(Path.Combine(root, name), source);
        foreach (var name in linked ? new[] { ProjectName, "LinkedContext" } : [ProjectName])
            await File.WriteAllTextAsync(Path.Combine(root, name + ".csproj"), "<Project />");
        var workspace = CreateWorkspace(root, sources, linked, selectedPath);
        try
        {
            var solution = workspace.CurrentSolution;
            var compilation = await AssertCompilesAsync(solution);
            if (acquire)
            {
                await ExplicitWorkspaceAcquisition.AttachAsync(solution, linked ? [ProjectName, "LinkedContext"] : [ProjectName]);
                await AssertCompleteAsync(solution);
            }
            var source = DocumentationCommentId.GetSymbolsForDeclarationId(SourceId, compilation).ShouldHaveSingleItem();
            var target = DocumentationCommentId.GetSymbolsForDeclarationId(TargetId, compilation).ShouldHaveSingleItem();
            var mapping = new StaticDependencyIntroductionMapping("rt.static-dependency.mapping.v1",
                new StaticDependencySymbol(SourceId, source.ContainingAssembly.Identity.ToString()),
                new StaticDependencySymbol(ReceiverId, target.ContainingAssembly.Identity.ToString()),
                new StaticDependencySymbol(TargetId, target.ContainingAssembly.Identity.ToString()),
                new StaticDependencyPreconditions(
                    "The chosen existing field is available at each selected call; no field is introduced.",
                    "The owner declares the existing reader lifetime covers each selected call.",
                    "The owner declares the existing reader is non-null at every selected call.",
                    "BclReader forwards the exact BCL overload without catching exceptions.",
                    "BclReader preserves the one-string overload's encoding behavior.",
                    "Both synchronous contracts have no cancellation parameter.",
                    "BclReader retains no resources and returns the BCL result directly.",
                    ["Controlled adapter source; availability, lifetime and non-null remain declared preconditions."]));
            var request = new StaticDependencyIntroductionRequest(mapping,
                new StaticDependencySelection(Path.Combine(root, ProjectName + ".csproj"), ConsumerId),
                await SolutionRevision.ComputeAsync(solution), "_newReader", "newReader", [], FieldId);
            return new StaticDependencyReuseFixture(workspace, solution, root, sources, request,
                WorkspaceAcquisitionRegistry.Get(workspace).Receipt);
        }
        catch
        {
            workspace.Dispose();
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    internal async Task AssertUntouchedAsync()
    {
        Workspace.CurrentSolution.ShouldBeSameAs(Solution);
        WorkspaceAcquisitionRegistry.Get(Workspace).Receipt.ShouldBeSameAs(Receipt);
        (await SolutionRevision.ComputeAsync(Solution)).ShouldBe(Request.ExpectedSourceFingerprint);
        var projectFiles = Solution.Projects.Select(project => project.Name + ".csproj").ToArray();
        Directory.GetFiles(Root).Order(StringComparer.Ordinal)
            .ShouldBe(Sources.Keys.Concat(projectFiles).Select(PathFor).Order(StringComparer.Ordinal));
        Directory.GetDirectories(Root).ShouldBeEmpty();
        foreach (var file in projectFiles)
            (await File.ReadAllTextAsync(PathFor(file))).ShouldBe("<Project />");
        foreach (var (name, source) in Sources)
        {
            (await File.ReadAllTextAsync(PathFor(name))).ShouldBe(source);
            foreach (var document in Solution.Projects.SelectMany(project => project.Documents).Where(document => document.Name == name))
                (await document.GetTextAsync()).ToString().ShouldBe(source);
        }
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
