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

public sealed class StaticDependencyMappingCoverageTests
{
    private const string SourceId = "M:System.IO.File.ReadAllText(System.String)";
    private const string TargetId = "M:Fixture.IReader.ReadAllText(System.String)";
    private static readonly string ProjectPath = Path.GetFullPath("/virtual/rt153-coverage/Selected.csproj");
    private static readonly string DocumentPath = Path.GetFullPath("/virtual/rt153-coverage/Shared.cs");

    [Theory]
    [InlineData("File /* retain callee comment */.ReadAllText")]
    [InlineData("File.\nReadAllText")]
    public async Task callee_interior_trivia_blocks_the_whole_selection_without_losing_text(string callee)
    {
        using var fixture = await CreateAsync(callee);
        var project = fixture.Solution.Projects.Single();
        var document = project.Documents.Single();
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var call = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Read")
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        call.Expression.ToString().ShouldBe(callee);
        call.Expression.DescendantTrivia().ShouldContain(trivia => call.Expression.Span.Contains(trivia.Span) && trivia.Span.Length > 0);
        model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod.GetDocumentationCommentId().ShouldBe(SourceId);
        (await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Complete);

        var plan = await AssertBlockedAsync(fixture, "mapping.unsupported");

        plan.Sites.ShouldContain(site => site.SpanStart == call.SpanStart && site.SpanLength == call.Span.Length
            && site.Message.Contains("trivia", StringComparison.OrdinalIgnoreCase));
        plan.Sites.Count(site => site.Disposition == StaticDependencySiteDisposition.Blocked).ShouldBe(2);
    }

    [Fact]
    public async Task a_shared_physical_document_across_complete_project_contexts_blocks_a_single_context_rewrite()
    {
        using var fixture = await CreateAsync("File.ReadAllText", sharedDocument: true);
        var projects = fixture.Solution.Projects.ToArray();
        projects.Length.ShouldBe(2);
        projects.Select(project => project.FilePath).Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
        projects.SelectMany(project => project.Documents).Select(document => document.FilePath)
            .ShouldBe([DocumentPath, DocumentPath]);
        (await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Complete);

        var plan = await AssertBlockedAsync(fixture, "mapping.unsupported");

        plan.Sites.ShouldContain(site => site.ProjectPath == ProjectPath && site.Path == DocumentPath
            && site.Message.Contains("shared", StringComparison.OrdinalIgnoreCase));
        plan.Sites.ShouldContain(site => site.ProjectPath != ProjectPath
            && site.Disposition == StaticDependencySiteDisposition.Skipped);
    }

    [Fact]
    public async Task a_bound_but_invalid_suppressed_callee_cannot_establish_complete_acquisition()
    {
        using var fixture = await CreateAsync("File.ReadAllText!", expectedCompilerError: "CS8598");
        var project = fixture.Solution.Projects.Single();
        var document = project.Documents.Single();
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var call = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.Expression.IsKind(SyntaxKind.SuppressNullableWarningExpression));
        model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod.GetDocumentationCommentId().ShouldBe(SourceId);
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        completeness.State.ShouldBe(WorkspaceCompletenessState.Partial);
        WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence.ShouldBeFalse();
        completeness.Diagnostics.ShouldContain(message => message.Contains("CS8598", StringComparison.Ordinal));

        await AssertBlockedAsync(fixture, "selection.acquisition");
    }

    [Fact]
    public async Task equal_parameter_types_do_not_authorize_rebinding_named_arguments_to_different_ordinals()
    {
        using var fixture = await CreateAsync("Source.ReadAllText", swappedNames: true);
        var document = fixture.Solution.Projects.Single().Documents.Single();
        var originalRoot = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var originalModel = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var originalCall = originalRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Read").DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var originalOperation = originalModel.GetOperation(originalCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        originalOperation.Arguments.Select(argument => argument.Parameter!.Ordinal).ShouldBe([0, 1, 2]);
        originalOperation.Arguments.Select(argument => argument.Parameter!.Type.SpecialType)
            .ShouldAllBe(type => type == SpecialType.System_String);
        var proposed = fixture.Solution.WithDocumentText(document.Id, SourceText.From(
            fixture.Source.Replace("Source.ReadAllText(", "this._reader.ReadAllText(", StringComparison.Ordinal)));
        var compilation = (await proposed.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var candidateDocument = proposed.GetDocument(document.Id)!;
        var candidateRoot = (await candidateDocument.GetSyntaxRootAsync()).ShouldNotBeNull();
        var candidateModel = (await candidateDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        var candidateCall = candidateRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Read").DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        candidateCall.ArgumentList.ToFullString().ShouldBe(originalCall.ArgumentList.ToFullString());
        var candidateOperation = candidateModel.GetOperation(candidateCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        candidateOperation.TargetMethod.GetDocumentationCommentId().ShouldBe(fixture.Request.Mapping.Target.DocumentationId);
        candidateOperation.Arguments.Select(argument => argument.Parameter!.Ordinal).ShouldBe([1, 0, 2]);
        candidateOperation.Arguments.Select(argument => argument.Parameter!.Type.SpecialType)
            .ShouldAllBe(type => type == SpecialType.System_String);

        await AssertBlockedAsync(fixture, "mapping.binding");
    }

    [Fact]
    public async Task a_conditional_extension_invocation_is_an_explicit_unsupported_callee_context()
    {
        using var fixture = await CreateAsync("path?.ReadAllText", conditionalExtension: true);
        var document = fixture.Solution.Projects.Single().Documents.Single();
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var call = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.Expression is MemberBindingExpressionSyntax);
        var operation = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
        operation.TargetMethod.GetDocumentationCommentId().ShouldBe(fixture.Request.Mapping.Source.DocumentationId);
        operation.TargetMethod.IsExtensionMethod.ShouldBeTrue();
        operation.TargetMethod.IsStatic.ShouldBeTrue();
        (await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Complete);

        var plan = await AssertBlockedAsync(fixture, "mapping.unsupported");

        plan.Sites.ShouldContain(site => site.SpanStart == call.SpanStart && site.SpanLength == call.Span.Length
            && site.Message.Contains("callee", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("params")]
    [InlineData("optional")]
    [InlineData("ref")]
    public async Task target_only_parameter_shapes_are_refused_before_argument_adaptation(string shape)
    {
        using var fixture = await CreateAsync(shape == "params" ? "Source.ReadAllText" : "File.ReadAllText",
            targetParameterShape: shape);
        var compilation = (await fixture.Solution.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        var source = DocumentationCommentId.GetSymbolsForDeclarationId(fixture.Request.Mapping.Source.DocumentationId, compilation)
            .OfType<IMethodSymbol>().ShouldHaveSingleItem();
        var target = DocumentationCommentId.GetSymbolsForDeclarationId(fixture.Request.Mapping.Target.DocumentationId, compilation)
            .OfType<IMethodSymbol>().ShouldHaveSingleItem();
        source.Parameters.ShouldHaveSingleItem().RefKind.ShouldBe(RefKind.None);
        source.Parameters[0].IsParams.ShouldBeFalse();
        source.Parameters[0].IsOptional.ShouldBeFalse();
        var parameter = target.Parameters.ShouldHaveSingleItem();
        if (shape == "params")
            parameter.IsParams.ShouldBeTrue();
        else if (shape == "optional")
            parameter.IsOptional.ShouldBeTrue();
        else
            parameter.RefKind.ShouldBe(RefKind.Ref);

        var plan = await AssertBlockedAsync(fixture, "selection.mapping");

        plan.Sites.ShouldContain(site => site.Code == "selection.mapping"
            && site.Message.Contains(shape, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("reserved", "event", "class")]
    [InlineData("contextual", "await", "record")]
    public async Task keyword_named_fields_and_targets_resolve_to_the_supplied_symbols(string names, string field, string member)
    {
        using var fixture = await CreateAsync("File.ReadAllText", escapedNames: names);
        var plan = await new StaticDependencyMappingService().PrepareAsync(fixture.Solution, fixture.Request);
        plan.SchemaVersion.ShouldBe("rt.static-dependency.plan.v1");
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var compilation = (await candidate.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var document = candidate.Projects.Single().Documents.Single();
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        calls.Length.ShouldBe(2);
        foreach (var call in calls)
        {
            var operation = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
            operation.TargetMethod.Name.ShouldBe(member);
            operation.TargetMethod.GetDocumentationCommentId().ShouldBe(fixture.Request.Mapping.Target.DocumentationId);
            var receiver = operation.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
            receiver.Field.Name.ShouldBe(field);
            receiver.Field.GetDocumentationCommentId().ShouldBe(fixture.Request.Mapping.ReceiverFieldDocumentationId);
            receiver.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
            call.ArgumentList.ToString().ShouldBe("(path)");
        }
        plan.Documents.ShouldHaveSingleItem().Before.ShouldBe(fixture.Source);
        (await fixture.Solution.Projects.Single().Documents.Single().GetTextAsync()).ToString().ShouldBe(fixture.Source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task bound_candidate_compilation_errors_block_even_when_the_target_and_arguments_bind(bool nullable)
    {
        using var fixture = await CreateAsync("File.ReadAllText", nullableReceiver: nullable, nullableErrors: true);
        var original = fixture.Solution.Projects.Single().Documents.Single();
        var proposed = fixture.Solution.WithDocumentText(original.Id, SourceText.From(
            fixture.Source.Replace("File.ReadAllText(", "this._reader.ReadAllText(", StringComparison.Ordinal)));
        var compilation = (await proposed.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (nullable)
            errors.ShouldAllBe(error => error.Id == "CS8602");
        else
            errors.ShouldBeEmpty();
        if (nullable)
            errors.ShouldNotBeEmpty();
        var candidateDocument = proposed.GetDocument(original.Id)!;
        var root = (await candidateDocument.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await candidateDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var operation = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
            operation.TargetMethod.GetDocumentationCommentId().ShouldBe(fixture.Request.Mapping.Target.DocumentationId);
            operation.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.GetDocumentationCommentId()
                .ShouldBe(fixture.Request.Mapping.ReceiverFieldDocumentationId);
            operation.Arguments.ShouldHaveSingleItem().Parameter!.Ordinal.ShouldBe(0);
        }
        (await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Complete);
        if (nullable)
        {
            var plan = await AssertBlockedAsync(fixture, "selection.compilation:" + ProjectPath);
            var selected = plan.Sites.Where(site => site.Path == original.FilePath && site.SpanLength > 0).ToArray();
            selected.Length.ShouldBe(2);
            selected.ShouldAllBe(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
            selected.ShouldAllBe(site => site.Code == "selection.blocked");
            plan.Sites.ShouldContain(site => site.Code == "selection.compilation:" + ProjectPath
                && site.Message.Contains("CS8602", StringComparison.Ordinal)
                && site.Message.Contains(ProjectPath, StringComparison.Ordinal));
        }
        else
        {
            var plan = await new StaticDependencyMappingService().PrepareAsync(fixture.Solution, fixture.Request);
            plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
            plan.Documents.ShouldHaveSingleItem();
            plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
        }
    }

    private static async Task<StaticDependencyPlan> AssertBlockedAsync(Fixture fixture, string code)
    {
        var snapshot = fixture.Workspace.CurrentSolution;
        var plan = await new StaticDependencyMappingService().PrepareAsync(fixture.Solution, fixture.Request);
        plan.SchemaVersion.ShouldBe("rt.static-dependency.plan.v1");
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        plan.SourceFingerprint.ShouldBe(fixture.Request.ExpectedSourceFingerprint);
        plan.Documents.ShouldBeEmpty();
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked && site.Code == code);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate);
        plan.Changes.HasChanges.ShouldBeFalse();
        plan.Changes.AffectedPaths.ShouldBeEmpty();
        plan.Changes.OriginalSolution.ShouldBeSameAs(fixture.Solution);
        plan.Changes.ChangedSolution.ShouldBeSameAs(fixture.Solution);
        plan.Changes.NewFiles.ShouldBeEmpty();
        plan.Changes.DeletedFiles.ShouldBeEmpty();
        plan.Changes.MovedFiles.ShouldBeEmpty();
        plan.Changes.ProjectFileEdits.ShouldBeEmpty();
        plan.Changes.TextEdits.ShouldBeEmpty();
        var repeat = await new StaticDependencyMappingService().PrepareAsync(fixture.Solution, fixture.Request);
        repeat.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        repeat.Sites.ShouldBe(plan.Sites);
        repeat.Documents.ShouldBeEmpty();
        fixture.Workspace.CurrentSolution.ShouldBeSameAs(snapshot);
        foreach (var document in fixture.Solution.Projects.SelectMany(project => project.Documents))
            (await document.GetTextAsync()).ToString().ShouldBe(fixture.Source);
        (await SolutionRevision.ComputeAsync(fixture.Solution)).ShouldBe(fixture.Request.ExpectedSourceFingerprint);
        return plan;
    }

    private static async Task<Fixture> CreateAsync(string callee, bool sharedDocument = false, string? expectedCompilerError = null,
        bool swappedNames = false, bool conditionalExtension = false, string? targetParameterShape = null,
        string? escapedNames = null, bool nullableReceiver = false, bool nullableErrors = false)
    {
        var memberName = escapedNames == "reserved" ? "class" : escapedNames == "contextual" ? "record" : "ReadAllText";
        var fieldName = escapedNames == "reserved" ? "event" : escapedNames == "contextual" ? "await" : "_reader";
        var declaredMember = escapedNames is null ? memberName : "@" + memberName;
        var declaredField = escapedNames is null ? fieldName : "@" + fieldName;
        var reader = swappedNames ? "string ReadAllText(string middle, string left, string right);"
            : targetParameterShape switch
            {
                "params" => "string ReadAllText(params string[] path);",
                "optional" => "string ReadAllText(string path = \"fallback\");",
                "ref" => "string ReadAllText(ref string path);",
                _ => $"string {declaredMember}(string path);"
            };
        var sourceMethod = conditionalExtension
            ? "public static class Source { public static string ReadAllText(this string path) => path; }"
            : targetParameterShape == "params"
            ? "public static class Source { public static string ReadAllText(string[] path) => string.Concat(path); }"
            : swappedNames
            ? "public static class Source { public static string ReadAllText(string left, string middle, string right) => left + middle + right; }"
            : string.Empty;
        var good = conditionalExtension ? "Source.ReadAllText(path)"
            : targetParameterShape == "params" ? "Source.ReadAllText(new[] { path })"
            : swappedNames ? "Source.ReadAllText(path, path, path)" : "File.ReadAllText(path)";
        var arguments = conditionalExtension ? string.Empty
            : targetParameterShape == "params" ? "new[] { path }"
            : swappedNames ? "left: path, middle: path, right: path" : "path";
        var sourceId = conditionalExtension ? "M:Fixture.Source.ReadAllText(System.String)"
            : targetParameterShape == "params" ? "M:Fixture.Source.ReadAllText(System.String[])"
            : swappedNames ? "M:Fixture.Source.ReadAllText(System.String,System.String,System.String)" : SourceId;
        var targetId = swappedNames ? "M:Fixture.IReader.ReadAllText(System.String,System.String,System.String)"
            : targetParameterShape == "params" ? "M:Fixture.IReader.ReadAllText(System.String[])"
            : targetParameterShape == "ref" ? "M:Fixture.IReader.ReadAllText(System.String@)"
            : $"M:Fixture.IReader.{memberName}(System.String)";
        var receiverType = nullableReceiver ? "IReader?" : "IReader";
        var source = $$"""
            {{(nullableReceiver || nullableErrors ? "#nullable enable" : string.Empty)}}
            using System.IO;
            namespace Fixture;
            public interface IReader { {{reader}} }
            {{sourceMethod}}
            public sealed class Consumer
            {
                private readonly {{receiverType}} {{declaredField}};
                public Consumer({{receiverType}} reader) { {{declaredField}} = reader; }
                public string Good(string path) => {{good}};
                public string Read(string path) => {{callee}}({{arguments}});
            }
            """;
        var workspace = new AdhocWorkspace();
        var names = sharedDocument ? new[] { "Selected", "Peer" } : ["Selected"];
        foreach (var name in names)
        {
            var id = ProjectId.CreateNewId();
            var references = new[] { typeof(object).Assembly.Location, typeof(File).Assembly.Location,
                    Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll") }
                .Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            if (nullableErrors)
                compilationOptions = compilationOptions.WithSpecificDiagnosticOptions(
                    new Dictionary<string, ReportDiagnostic> { ["CS8602"] = ReportDiagnostic.Error });
            workspace.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: Path.GetFullPath("/virtual/rt153-coverage/" + name + ".csproj"),
                compilationOptions: compilationOptions,
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12), metadataReferences: references));
            workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(id), "Shared.cs",
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())), filePath: DocumentPath));
        }
        var solution = workspace.CurrentSolution;
        foreach (var project in solution.Projects)
        {
            var compilation = (await project.GetCompilationAsync()).ShouldNotBeNull();
            var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (expectedCompilerError is null)
                errors.ShouldBeEmpty("the independently acquired shared/trivia fixture must compile");
            else
                errors.ShouldHaveSingleItem().Id.ShouldBe(expectedCompilerError);
        }
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, names);
        var selected = solution.Projects.Single(project => project.FilePath == ProjectPath);
        var selectedCompilation = (await selected.GetCompilationAsync()).ShouldNotBeNull();
        var from = DocumentationCommentId.GetSymbolsForDeclarationId(sourceId, selectedCompilation).OfType<IMethodSymbol>().ShouldHaveSingleItem();
        var target = DocumentationCommentId.GetSymbolsForDeclarationId(targetId, selectedCompilation).OfType<IMethodSymbol>().ShouldHaveSingleItem();
        from.IsStatic.ShouldBeTrue();
        if (!swappedNames && !conditionalExtension && targetParameterShape != "params")
            from.Locations.ShouldAllBe(location => location.IsInMetadata);
        target.IsStatic.ShouldBeFalse();
        var mapping = new StaticDependencyMethodMapping("rt.static-dependency.mapping.v1",
            new StaticDependencySymbol(sourceId, from.ContainingAssembly.Identity.ToString()), $"F:Fixture.Consumer.{fieldName}",
            new StaticDependencySymbol(targetId, target.ContainingAssembly.Identity.ToString()),
            new StaticDependencyPreconditions("Caller declares availability.", "Caller declares lifetime.", "Caller declares non-null.",
                "Caller declares exceptions.", "Caller declares encoding.", "Caller declares cancellation.", "Caller declares ownership.",
                ["Controlled semantic characterization; no claim of arbitrary adapter equivalence."]));
        return new Fixture(workspace, solution, source, new StaticDependencyMappingRequest(mapping,
            new StaticDependencySelection(ProjectPath, "T:Fixture.Consumer"), await SolutionRevision.ComputeAsync(solution)));
    }

    private sealed record Fixture(AdhocWorkspace Workspace, Solution Solution, string Source, StaticDependencyMappingRequest Request) : IDisposable
    {
        public void Dispose() => Workspace.Dispose();
    }
}
