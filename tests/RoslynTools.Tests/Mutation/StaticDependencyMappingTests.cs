using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class StaticDependencyMappingTests
{
    private static readonly string[] MappedCallNames = ["alias", "qualified", "ordinary", "imported"];

    private const string Before = """
        using System.IO;
        using System.Text;
        using FileAlias = System.IO.File;
        using static System.IO.File;

        namespace Fixture;

        public interface IReader
        {
            string ReadAllText(string path);
        }

        public sealed class BclReader : IReader
        {
            public string ReadAllText(string path) => File.ReadAllText(path);
        }

        public static class UserFiles
        {
            public static string ReadAllText(string path) => path;
        }

        public sealed class Consumer
        {
            private readonly IReader _reader;

            public Consumer(IReader reader)
            {
                _reader = reader ?? throw new System.ArgumentNullException(nameof(reader));
            }

            public string Read(string path)
            {
                var _reader = path + "-shadow";
                var alias = FileAlias.ReadAllText(path: path + "-alias");
                var qualified = global::System.IO.File.ReadAllText(path + "-qualified");
                var ordinary = File.ReadAllText(path: path + "-ordinary");
                var imported = ReadAllText(path + "-imported");
                var user = UserFiles.ReadAllText(path + "-user");
                var encoded = File.ReadAllText(path + "-encoded", Encoding.Unicode);
                return alias + qualified + ordinary + imported + user + encoded + _reader;
            }
        }
        """;

    private const string After = """
        using System.IO;
        using System.Text;
        using FileAlias = System.IO.File;
        using static System.IO.File;

        namespace Fixture;

        public interface IReader
        {
            string ReadAllText(string path);
        }

        public sealed class BclReader : IReader
        {
            public string ReadAllText(string path) => File.ReadAllText(path);
        }

        public static class UserFiles
        {
            public static string ReadAllText(string path) => path;
        }

        public sealed class Consumer
        {
            private readonly IReader _reader;

            public Consumer(IReader reader)
            {
                _reader = reader ?? throw new System.ArgumentNullException(nameof(reader));
            }

            public string Read(string path)
            {
                var _reader = path + "-shadow";
                var alias = this._reader.ReadAllText(path: path + "-alias");
                var qualified = this._reader.ReadAllText(path + "-qualified");
                var ordinary = this._reader.ReadAllText(path: path + "-ordinary");
                var imported = this._reader.ReadAllText(path + "-imported");
                var user = UserFiles.ReadAllText(path + "-user");
                var encoded = File.ReadAllText(path + "-encoded", Encoding.Unicode);
                return alias + qualified + ordinary + imported + user + encoded + _reader;
            }
        }
        """;

    [Fact]
    public async Task prepares_exact_static_calls_on_an_existing_reader_without_writing()
    {
        using var workspace = new AdhocWorkspace();
        // Stable virtual paths describe this fixture; no project or source files are created.
        var projectPath = Path.GetFullPath("/virtual/rt022/ReaderFixture.csproj");
        var documentPath = Path.GetFullPath("/virtual/rt022/Consumer.cs");
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var references = new[]
        {
            typeof(object).Assembly.Location,
            typeof(File).Assembly.Location,
            typeof(Encoding).Assembly.Location,
            Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")
        }.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        workspace.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "ReaderFixture",
            "ReaderFixture",
            LanguageNames.CSharp,
            filePath: projectPath,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12),
            metadataReferences: references));
        workspace.AddDocument(DocumentInfo.Create(
            documentId,
            "Consumer.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(Before), VersionStamp.Create())),
            filePath: documentPath));
        var original = workspace.CurrentSolution;
        await ExplicitWorkspaceAcquisition.AttachAsync(original, ["ReaderFixture"]);
        var fingerprint = await SolutionRevision.ComputeAsync(original);

        // Setup failures remain outside the success assertion for the unwired planner.
        var compilation = (await original.GetProject(projectId)!.GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty("the independent fixture must compile before preparation");
        var sourceMethod = compilation.GetTypeByMetadataName("System.IO.File").ShouldNotBeNull()
            .GetMembers("ReadAllText").OfType<IMethodSymbol>()
            .Single(method => method.Parameters.Length == 1
                && method.Parameters[0].Type.SpecialType == SpecialType.System_String);
        sourceMethod.GetDocumentationCommentId().ShouldBe("M:System.IO.File.ReadAllText(System.String)");
        sourceMethod.Locations.All(location => location.IsInMetadata).ShouldBeTrue();
        var targetMethod = compilation.GetTypeByMetadataName("Fixture.IReader").ShouldNotBeNull()
            .GetMembers("ReadAllText").OfType<IMethodSymbol>().Single();
        var receiver = compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull()
            .GetMembers("_reader").OfType<IFieldSymbol>().Single();
        receiver.IsStatic.ShouldBeFalse();
        receiver.IsReadOnly.ShouldBeTrue();
        SymbolEqualityComparer.Default.Equals(receiver.Type, targetMethod.ContainingType).ShouldBeTrue();

        var originalDocument = original.GetDocument(documentId).ShouldNotBeNull();
        var originalRoot = (await originalDocument.GetSyntaxRootAsync()).ShouldNotBeNull();
        var originalModel = (await originalDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        var calls = MappedCallNames.Select(name => FindInitializer(originalRoot, name)).ToArray();
        foreach (var call in calls)
        {
            var operation = originalModel.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
            SymbolEqualityComparer.Default.Equals(operation.TargetMethod, sourceMethod).ShouldBeTrue();
            operation.Arguments.ShouldHaveSingleItem().Parameter!.Ordinal.ShouldBe(0);
        }
        var forwardingCall = originalRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "BclReader")
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var forwardingOperation = originalModel.GetOperation(forwardingCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        SymbolEqualityComparer.Default.Equals(forwardingOperation.TargetMethod, sourceMethod).ShouldBeTrue();
        forwardingOperation.Arguments.ShouldHaveSingleItem().Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!
            .Parameter.Name.ShouldBe("path");

        var mapping = new StaticDependencyMethodMapping(
            SchemaVersion: "rt.static-dependency.mapping.v1",
            Source: new StaticDependencySymbol(sourceMethod.GetDocumentationCommentId()!,
                sourceMethod.ContainingAssembly.Identity.ToString()),
            ReceiverFieldDocumentationId: "F:Fixture.Consumer._reader",
            Target: new StaticDependencySymbol("M:Fixture.IReader.ReadAllText(System.String)",
                targetMethod.ContainingAssembly.Identity.ToString()),
            Preconditions: new StaticDependencyPreconditions(
                ReceiverAvailability: "Consumer's constructor receives and stores IReader before Read is invoked.",
                ReceiverLifetime: "The readonly reader remains available for the Consumer instance lifetime.",
                ReceiverNonNull: "Consumer's constructor rejects null readers.",
                AdapterExceptions: "BclReader directly forwards the BCL overload without catching or wrapping exceptions.",
                AdapterEncoding: "BclReader uses the same one-string BCL overload and its default encoding behavior.",
                AdapterCancellation: "Both synchronous methods have no cancellation parameter or additional cancellation.",
                AdapterResourceOwnership: "BclReader directly returns the BCL result and retains no resources.",
                ContractEvidence:
                [
                    "StaticDependencyMappingTests: compiled Consumer constructor guards and stores a readonly IReader.",
                    "StaticDependencyMappingTests: BclReader's expression body semantically forwards its path to the exact BCL overload."
                ]));
        var request = new StaticDependencyMappingRequest(mapping,
            new StaticDependencySelection(projectPath, "T:Fixture.Consumer"), fingerprint);
        var service = new StaticDependencyMappingService();
        StaticDependencyPlan? prepared = null;

        var exception = await Record.ExceptionAsync(async () =>
        {
            prepared = await service.PrepareAsync(original, request);
        });

        exception.ShouldBeNull("existing-reader planning must succeed");
        var plan = prepared.ShouldNotBeNull();
        plan.SchemaVersion.ShouldBe("rt.static-dependency.plan.v1");
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        plan.Request.ShouldBe(request);
        plan.SourceFingerprint.ShouldBe(fingerprint);
        plan.MappingFingerprint.ShouldNotBeNullOrWhiteSpace();
        plan.Documents.ShouldBe([new StaticDependencyDocumentChange(documentPath, Before, After)]);
        plan.Changes.HasChanges.ShouldBeTrue();
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        plan.Changes.AffectedPaths.ShouldBe([documentPath]);
        AssertNoFileEffects(plan.Changes);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var candidateDocument = candidate.GetDocument(documentId).ShouldNotBeNull();
        (await candidateDocument.GetTextAsync()).ToString().ShouldBe(After);
        var delta = candidate.GetChanges(original);
        delta.GetAddedProjects().ShouldBeEmpty();
        delta.GetRemovedProjects().ShouldBeEmpty();
        var projectChange = delta.GetProjectChanges().ShouldHaveSingleItem();
        projectChange.GetChangedDocuments().ShouldBe([documentId]);
        projectChange.GetAddedDocuments().ShouldBeEmpty();
        projectChange.GetRemovedDocuments().ShouldBeEmpty();
        var candidateCompilation = (await candidate.GetProject(projectId)!.GetCompilationAsync()).ShouldNotBeNull();
        candidateCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty("the prepared candidate must introduce no compilation errors");

        var candidateRoot = (await candidateDocument.GetSyntaxRootAsync()).ShouldNotBeNull();
        var candidateModel = (await candidateDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        var converted = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate)
            .OrderBy(site => site.SpanStart).ToArray();
        converted.Length.ShouldBe(4);
        converted.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(4);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
        for (var index = 0; index < calls.Length; index++)
        {
            var site = converted[index];
            site.Id.ShouldNotBeNullOrWhiteSpace();
            site.ProjectPath.ShouldBe(projectPath);
            site.TypeDocumentationId.ShouldBe("T:Fixture.Consumer");
            site.Path.ShouldBe(documentPath);
            site.SpanStart.ShouldBe(calls[index].SpanStart);
            site.SpanLength.ShouldBe(calls[index].Span.Length);
            var name = ((VariableDeclaratorSyntax)calls[index].Parent!.Parent!).Identifier.ValueText;
            var replacement = FindInitializer(candidateRoot, name);
            replacement.ArgumentList.ToString().ShouldBe(calls[index].ArgumentList.ToString());
            var operation = candidateModel.GetOperation(replacement).ShouldBeAssignableTo<IInvocationOperation>()!;
            operation.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.IReader.ReadAllText(System.String)");
            operation.TargetMethod.ContainingAssembly.Identity.ToString()
                .ShouldBe(targetMethod.ContainingAssembly.Identity.ToString());
            var argument = operation.Arguments.ShouldHaveSingleItem();
            argument.Parameter!.Ordinal.ShouldBe(0);
            argument.Parameter.Name.ShouldBe("path");
            argument.Parameter.Type.SpecialType.ShouldBe(SpecialType.System_String);
            var field = operation.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
            field.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._reader");
            field.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
        }

        var userCall = candidateModel.GetOperation(FindInitializer(candidateRoot, "user"))
            .ShouldBeAssignableTo<IInvocationOperation>()!;
        userCall.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.UserFiles.ReadAllText(System.String)");
        var encodedCall = candidateModel.GetOperation(FindInitializer(candidateRoot, "encoded"))
            .ShouldBeAssignableTo<IInvocationOperation>()!;
        encodedCall.TargetMethod.GetDocumentationCommentId()
            .ShouldBe("M:System.IO.File.ReadAllText(System.String,System.Text.Encoding)");
        encodedCall.TargetMethod.ContainingAssembly.Identity.ToString()
            .ShouldBe(sourceMethod.ContainingAssembly.Identity.ToString());

        var repeated = await service.PrepareAsync(original, request);
        repeated.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        repeated.SourceFingerprint.ShouldBe(plan.SourceFingerprint);
        repeated.MappingFingerprint.ShouldBe(plan.MappingFingerprint);
        repeated.Documents.ShouldBe(plan.Documents);
        repeated.Sites.Select(site => (site.Id, site.ProjectPath, site.TypeDocumentationId, site.Path,
                site.SpanStart, site.SpanLength, site.Disposition))
            .ShouldBe(plan.Sites.Select(site => (site.Id, site.ProjectPath, site.TypeDocumentationId, site.Path,
                site.SpanStart, site.SpanLength, site.Disposition)));
        (await repeated.Changes.ChangedSolution.ShouldNotBeNull().GetDocument(documentId)!.GetTextAsync())
            .ToString().ShouldBe(After);

        // Authoritative acquisition evidence and the fingerprint must describe the new snapshot.
        using var reacquiredWorkspace = new AdhocWorkspace();
        var candidateProject = candidate.GetProject(projectId).ShouldNotBeNull();
        reacquiredWorkspace.AddProject(ProjectInfo.Create(
            projectId, VersionStamp.Create(), candidateProject.Name, candidateProject.AssemblyName!,
            candidateProject.Language, filePath: candidateProject.FilePath,
            compilationOptions: candidateProject.CompilationOptions,
            parseOptions: candidateProject.ParseOptions,
            metadataReferences: candidateProject.MetadataReferences));
        reacquiredWorkspace.AddDocument(DocumentInfo.Create(
            documentId, "Consumer.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(After), VersionStamp.Create())),
            filePath: documentPath));
        var reacquired = reacquiredWorkspace.CurrentSolution;
        await ExplicitWorkspaceAcquisition.AttachAsync(reacquired, ["ReaderFixture"]);
        var candidateFingerprint = await SolutionRevision.ComputeAsync(reacquired);
        candidateFingerprint.ShouldBe(await SolutionRevision.ComputeAsync(candidate));
        candidateFingerprint.ShouldNotBe(fingerprint);
        var noOp = await service.PrepareAsync(reacquired,
            request with { ExpectedSourceFingerprint = candidateFingerprint });
        noOp.Status.ShouldBe(StaticDependencyPlanStatus.NoOp);
        noOp.SourceFingerprint.ShouldBe(candidateFingerprint);
        noOp.Documents.ShouldBeEmpty();
        noOp.Changes.HasChanges.ShouldBeFalse();
        noOp.Changes.AffectedPaths.ShouldBeEmpty();
        AssertNoFileEffects(noOp.Changes);
        noOp.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate
            || site.Disposition == StaticDependencySiteDisposition.Blocked);

        workspace.CurrentSolution.ShouldBeSameAs(original);
        (await originalDocument.GetTextAsync()).ToString().ShouldBe(Before);
        (await SolutionRevision.ComputeAsync(original)).ShouldBe(fingerprint);
    }

    private static InvocationExpressionSyntax FindInitializer(SyntaxNode root, string variableName) =>
        (InvocationExpressionSyntax)root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == variableName).Initializer!.Value;

    private static void AssertNoFileEffects(PreparedChangeSet changes)
    {
        changes.NewFiles.ShouldBeEmpty();
        changes.DeletedFiles.ShouldBeEmpty();
        changes.MovedFiles.ShouldBeEmpty();
        changes.ProjectFileEdits.ShouldBeEmpty();
        changes.TextEdits.ShouldBeEmpty();
    }
}
