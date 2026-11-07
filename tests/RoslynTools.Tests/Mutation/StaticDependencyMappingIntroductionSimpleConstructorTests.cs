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

public sealed class StaticDependencyMappingIntroductionSimpleConstructorTests
{
    private const string ProjectName = "SimpleIntroduction";
    private const string ConsumerId = "T:Fixture.Consumer";
    private const string FieldId = "F:Fixture.Consumer._reader";
    private const string SourceId = "M:System.IO.File.ReadAllText(System.String)";
    private const string TargetId = "M:Fixture.IReader.ReadAllText(System.String)";
    private const string OriginalConstructorId = "M:Fixture.Consumer.#ctor(System.Int32,System.String)";
    private const string CandidateConstructorId = "M:Fixture.Consumer.#ctor(System.Int32,System.String,Fixture.IReader)";

    private const string Contracts = """
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

    private const string Consumer = """
        using System.IO;
        using System.Text;
        using FileAlias = System.IO.File;
        namespace Fixture;
        internal sealed class Consumer
        {
            private readonly int _code;
            private readonly string _initial;
            internal Consumer(int code, string path)
            {
                _code = code;
                _initial = FileAlias.ReadAllText(path: path + "-ctor");
            }
            internal string Read(string path)
            {
                var exact = File.ReadAllText(path: path + "-body");
                var encoded = File.ReadAllText(path, Encoding.Unicode);
                return _initial + _code + exact + encoded;
            }
        }
        """;

    private const string Callers = """
        namespace Fixture;
        internal static class Callers
        {
            private static int _order;
            internal static string First(int code, IReader input, string path)
            {
                var consumer = new Consumer(Stamp(code), path: Trace(path));
                return consumer.Read(path);
            }
            internal static string Second(IReader alternate, string path)
            {
                var consumer = new Consumer(path: Trace(path), code: Stamp(2));
                return consumer.Read(path);
            }
            private static int Stamp(int value) => value + ++_order;
            private static string Trace(string value) => value + ++_order;
        }
        """;

    [Fact]
    public async Task prepares_single_constructor_and_explicit_callers_without_writing()
    {
        using var fixture = await CreateAsync();
        var original = fixture.Solution;
        var request = fixture.Request;
        var receipt = WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldNotBeNull();
        var compilation = await AssertCompilesAsync(original);
        var type = compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        type.DeclaredAccessibility.ShouldBe(Accessibility.Internal);
        type.IsSealed.ShouldBeTrue();
        type.Arity.ShouldBe(0);
        var constructor = type.InstanceConstructors.ShouldHaveSingleItem();
        constructor.IsImplicitlyDeclared.ShouldBeFalse();
        constructor.GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        var (originalRoot, originalModel) = await InspectAsync(original, "Consumer.cs");
        var originalCalls = CallsBoundTo(originalRoot, originalModel, SourceId);
        originalCalls.Length.ShouldBe(2);
        var service = new StaticDependencyIntroductionService();
        StaticDependencyIntroductionPlan? prepared = null;

        var exception = await Record.ExceptionAsync(async () =>
        {
            prepared = await service.PrepareAsync(original, request);
        });

        exception.ShouldBeNull("simple constructor introduction must return a coherent no-write plan");
        var plan = prepared.ShouldNotBeNull();
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        plan.SchemaVersion.ShouldNotBeNullOrWhiteSpace();
        plan.Request.ShouldBe(request);
        plan.SourceFingerprint.ShouldBe(request.ExpectedSourceFingerprint);
        plan.MappingFingerprint.ShouldNotBeNullOrWhiteSpace();
        plan.Resolution.ShouldBe(new StaticDependencyIntroductionResolution(false, FieldId, "_reader", "reader"));
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        plan.Changes.HasChanges.ShouldBeTrue();
        AssertNoFileEffects(plan.Changes);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var candidateCompilation = await AssertCompilesAsync(candidate);

        // Independently specified complete syntax; formatting is not part of wiring semantics.
        var expectedConsumer = Consumer.Replace("private readonly int _code;",
                "private readonly global::Fixture.IReader _reader;\n    private readonly int _code;", StringComparison.Ordinal)
            .Replace("Consumer(int code, string path)",
                "Consumer(int code, string path, global::Fixture.IReader reader)", StringComparison.Ordinal)
            .Replace("_code = code;", "this._reader = reader;\n        _code = code;", StringComparison.Ordinal)
            .Replace("FileAlias.ReadAllText", "this._reader.ReadAllText", StringComparison.Ordinal)
            .Replace("File.ReadAllText(path: path + \"-body\")",
                "this._reader.ReadAllText(path: path + \"-body\")", StringComparison.Ordinal);
        var expectedCallers = Callers.Replace("path: Trace(path));",
                "path: Trace(path), reader: input);", StringComparison.Ordinal)
            .Replace("code: Stamp(2));", "code: Stamp(2), reader: alternate);", StringComparison.Ordinal);
        await AssertDocumentChangesAsync(fixture, plan, expectedConsumer, expectedCallers);

        var field = candidateCompilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull()
            .GetMembers("_reader").OfType<IFieldSymbol>().ShouldHaveSingleItem();
        field.IsReadOnly.ShouldBeTrue();
        field.IsStatic.ShouldBeFalse();
        field.DeclaredAccessibility.ShouldBe(Accessibility.Private);
        field.Type.GetDocumentationCommentId().ShouldBe("T:Fixture.IReader");
        var (candidateRoot, candidateModel) = await InspectAsync(candidate, "Consumer.cs");
        var candidateConstructor = candidateRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single();
        var assignment = candidateModel.GetOperation(candidateConstructor.Body!.Statements[0])
            .ShouldBeAssignableTo<IExpressionStatementOperation>()!.Operation
            .ShouldBeAssignableTo<ISimpleAssignmentOperation>()!;
        assignment.Target.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.GetDocumentationCommentId().ShouldBe(FieldId);
        var storedParameter = assignment.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter;
        storedParameter.Ordinal.ShouldBe(2);
        storedParameter.Name.ShouldBe("reader");
        storedParameter.ContainingSymbol.GetDocumentationCommentId().ShouldBe(CandidateConstructorId);
        SymbolEqualityComparer.Default.Equals(storedParameter.Type, field.Type).ShouldBeTrue();

        var mapped = CallsBoundTo(candidateRoot, candidateModel, TargetId);
        mapped.Length.ShouldBe(2);
        var sites = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate).ToArray();
        plan.Sites.Count.ShouldBe(3, "inventory includes both selected calls and the unselected adapter call");
        plan.Sites.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(3);
        foreach (var site in sites)
        {
            site.ProjectPath.ShouldBe(request.Selection.ProjectPath);
            site.TypeDocumentationId.ShouldBe(ConsumerId);
        }
        sites.Select(site => (site.Path, site.SpanStart, site.SpanLength)).OrderBy(site => site.SpanStart)
            .ShouldBe(originalCalls.Select(call => (fixture.PathFor("Consumer.cs"), call.SpanStart, call.Span.Length)));
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
        foreach (var (before, after) in originalCalls.Zip(mapped))
        {
            var oldCall = originalModel.GetOperation(before).ShouldBeAssignableTo<IInvocationOperation>()!;
            var newCall = candidateModel.GetOperation(after).ShouldBeAssignableTo<IInvocationOperation>()!;
            oldCall.TargetMethod.Locations.All(location => location.IsInMetadata).ShouldBeTrue();
            newCall.TargetMethod.ContainingAssembly.Identity.ToString().ShouldBe(request.Mapping.Target.AssemblyIdentity);
            after.ArgumentList.ToString().ShouldBe(before.ArgumentList.ToString());
            newCall.Arguments.Select(ArgumentBinding).ShouldBe(oldCall.Arguments.Select(ArgumentBinding));
            var receiver = newCall.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
            receiver.Field.GetDocumentationCommentId().ShouldBe(FieldId);
            receiver.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
        }
        // The other BCL overload and the unselected adapter must keep their original binding.
        candidateModel.GetOperation(candidateRoot.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(call => call.Expression.ToString() == "File.ReadAllText"))
            .ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod.GetDocumentationCommentId()
            .ShouldBe("M:System.IO.File.ReadAllText(System.String,System.Text.Encoding)");
        var (adapterRoot, adapterModel) = await InspectAsync(candidate, "Contracts.cs");
        CallsBoundTo(adapterRoot, adapterModel, SourceId).ShouldHaveSingleItem();
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Skipped
            && site.TypeDocumentationId == "T:Fixture.BclReader" && site.Path == fixture.PathFor("Contracts.cs"));
        await AssertCallerBindingsAsync(original, candidate, request);
        await AssertClosureAsync(fixture, plan, originalCalls, candidateCompilation);

        var repeated = await service.PrepareAsync(original, request);
        repeated.Status.ShouldBe(plan.Status);
        repeated.SchemaVersion.ShouldBe(plan.SchemaVersion);
        repeated.SourceFingerprint.ShouldBe(plan.SourceFingerprint);
        repeated.MappingFingerprint.ShouldBe(plan.MappingFingerprint);
        repeated.Documents.ShouldBe(plan.Documents);
        repeated.Sites.ShouldBe(plan.Sites);
        repeated.Closure.ShouldBe(plan.Closure);
        repeated.Resolution.ShouldBe(plan.Resolution);
        foreach (var document in plan.Documents)
            (await FindDocument(repeated.Changes.ChangedSolution.ShouldNotBeNull(), Path.GetFileName(document.Path))
                .GetTextAsync()).ToString().ShouldBe(document.After);

        // Engine-owned candidates are not acquisitions. Keep the original receipt intact.
        WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldBeSameAs(receipt);
        (await WorkspaceCompleteness.AssessSemanticAsync(candidate, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Partial);
        var candidateSources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in candidate.Projects.Single().Documents)
            candidateSources.Add(document.Name, (await document.GetTextAsync()).ToString());
        using var freshWorkspace = CreateWorkspace(fixture.Root, candidateSources);
        var reacquired = freshWorkspace.CurrentSolution;
        await ExplicitWorkspaceAcquisition.AttachAsync(reacquired, [ProjectName]);
        await AssertCompleteAsync(reacquired);
        await AssertCompilesAsync(reacquired);
        var candidateFingerprint = await SolutionRevision.ComputeAsync(reacquired);
        candidateFingerprint.ShouldBe(await SolutionRevision.ComputeAsync(candidate));
        candidateFingerprint.ShouldNotBe(plan.SourceFingerprint);
        var noOp = await service.PrepareAsync(reacquired, request with { ExpectedSourceFingerprint = candidateFingerprint });
        noOp.Status.ShouldBe(StaticDependencyPlanStatus.NoOp);
        noOp.SourceFingerprint.ShouldBe(candidateFingerprint);
        noOp.Documents.ShouldBeEmpty();
        noOp.Closure.ShouldBeEmpty();
        noOp.Changes.HasChanges.ShouldBeFalse();
        noOp.Changes.AffectedPaths.ShouldBeEmpty();
        AssertNoFileEffects(noOp.Changes);
        noOp.Sites.ShouldNotContain(site => site.Disposition != StaticDependencySiteDisposition.Skipped);
        freshWorkspace.CurrentSolution.ShouldBeSameAs(reacquired);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task refuses_atomic_selection_on_public_consumer()
    {
        using var fixture = await CreateAsync("public-consumer");
        var original = fixture.Solution;
        await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var (root, model) = await InspectAsync(original, "Consumer.cs");
        var selectedCalls = CallsBoundTo(root, model, SourceId);
        selectedCalls.Length.ShouldBe(2);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(original, fixture.Request);

        AssertBlockedWithoutCandidate(plan, original);
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task refuses_explicit_field_capture_in_deferred_consumer()
    {
        using var fixture = await CreateAsync(deferredForm: "lambda", deferredExpression: "this._initial");
        var (model, escape, selectedCalls) = await AssertDeferredConsumerAsync(fixture);
        var lambda = escape.DescendantNodes().OfType<LambdaExpressionSyntax>().ShouldHaveSingleItem();
        var capture = model.GetOperation(lambda.Body).ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        capture.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._initial");
        var receiver = capture.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        receiver.Type!.GetDocumentationCommentId().ShouldBe(ConsumerId);
        receiver.IsImplicit.ShouldBeFalse("the lambda must capture the selected consumer through an explicit field receiver");

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "explicit field capture must block dependency introduction");
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "lambda");
    }

    [Fact]
    public async Task refuses_inherited_method_group_capture_in_consumer()
    {
        using var fixture = await CreateAsync(deferredForm: "inherited method group");
        var (model, escape, selectedCalls) = await AssertDeferredConsumerAsync(fixture);
        var operation = model.GetOperation(escape).ShouldNotBeNull();
        var creation = operation.DescendantsAndSelf().OfType<IDelegateCreationOperation>().ShouldHaveSingleItem();
        var capture = creation.Target.ShouldBeAssignableTo<IMethodReferenceOperation>()!;
        capture.Method.GetDocumentationCommentId().ShouldBe("M:System.Object.MemberwiseClone");
        capture.Method.IsStatic.ShouldBeFalse();
        capture.Method.ContainingType.SpecialType.ShouldBe(SpecialType.System_Object);
        var receiver = capture.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        receiver.Type!.GetDocumentationCommentId().ShouldBe(ConsumerId,
            "an inherited method group must retain the selected consumer, not a separate object receiver");

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "method group");
    }

    [Fact]
    public async Task refuses_async_state_machine_retaining_consumer_state()
    {
        using var fixture = await CreateAsync(deferredForm: "async");
        var (model, escape, selectedCalls) = await AssertDeferredConsumerAsync(fixture);
        model.GetDeclaredSymbol(escape).ShouldNotBeNull().IsAsync.ShouldBeTrue();
        var body = model.GetOperation(escape.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        body.Operations.Length.ShouldBe(2);
        var suspension = body.Operations[0].ShouldBeAssignableTo<IExpressionStatementOperation>()!.Operation
            .ShouldBeAssignableTo<IAwaitOperation>()!;
        suspension.Operation.ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod
            .GetDocumentationCommentId().ShouldBe("M:System.Threading.Tasks.Task.Yield");
        var capture = body.Operations[1].ShouldBeAssignableTo<IReturnOperation>()!.ReturnedValue
            .ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        capture.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._initial");
        var receiver = capture.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        receiver.Type!.GetDocumentationCommentId().ShouldBe(ConsumerId,
            "the async method reads the selected consumer's state after its await");

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "async");
    }

    [Fact]
    public async Task refuses_iterator_state_machine_retaining_consumer_state()
    {
        using var fixture = await CreateAsync(deferredForm: "iterator");
        var (model, escape, selectedCalls) = await AssertDeferredConsumerAsync(fixture);
        var body = model.GetOperation(escape.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        var yielded = body.Operations.ShouldHaveSingleItem().ShouldBeAssignableTo<IReturnOperation>()!;
        yielded.Kind.ShouldBe(OperationKind.YieldReturn);
        var capture = yielded.ReturnedValue.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        capture.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._initial");
        var receiver = capture.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        receiver.Type!.GetDocumentationCommentId().ShouldBe(ConsumerId,
            "the iterator must retain the selected consumer to evaluate its yielded field value");

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "iterator");
    }

    [Fact]
    public async Task prepares_noncapturing_lambda_without_changing_original_acquisition()
    {
        using var fixture = await CreateAsync(deferredForm: "lambda", deferredExpression: "string.Empty");
        var (model, escape, selectedCalls) = await AssertDeferredConsumerAsync(fixture);
        var lambda = escape.DescendantNodes().OfType<LambdaExpressionSyntax>().ShouldHaveSingleItem();
        var operation = model.GetOperation(lambda).ShouldBeAssignableTo<IAnonymousFunctionOperation>()!;
        operation.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldBeEmpty();
        var field = model.GetOperation(lambda.Body).ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        field.Field.GetDocumentationCommentId().ShouldBe("F:System.String.Empty");
        field.Field.IsStatic.ShouldBeTrue();
        field.Instance.ShouldBeNull();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        plan.Changes.OriginalSolution.ShouldBeSameAs(fixture.Solution);
        plan.Changes.HasChanges.ShouldBeTrue();
        AssertNoFileEffects(plan.Changes);
        plan.Sites.Count.ShouldBe(3);
        plan.Sites.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(3);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
        var converted = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate).ToArray();
        converted.Length.ShouldBe(2);
        converted.ShouldAllBe(site => site.ProjectPath == fixture.Request.Selection.ProjectPath
            && site.TypeDocumentationId == ConsumerId);
        converted.Select(site => (site.Path, site.SpanStart, site.SpanLength)).OrderBy(site => site.SpanStart)
            .ShouldBe(selectedCalls.Select(call => (fixture.PathFor("Consumer.cs"), call.SpanStart, call.Span.Length)));
        var (adapterRoot, adapterModel) = await InspectAsync(fixture.Solution, "Contracts.cs");
        var adapterCall = CallsBoundTo(adapterRoot, adapterModel, SourceId).ShouldHaveSingleItem();
        var skipped = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.Skipped).ShouldHaveSingleItem();
        skipped.TypeDocumentationId.ShouldBe("T:Fixture.BclReader");
        (skipped.Path, skipped.SpanStart, skipped.SpanLength)
            .ShouldBe((fixture.PathFor("Contracts.cs"), adapterCall.SpanStart, adapterCall.Span.Length));
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var compilation = await AssertCompilesAsync(candidate);
        await AssertCallerBindingsAsync(fixture.Solution, candidate, fixture.Request);
        await AssertClosureAsync(fixture, plan, selectedCalls, compilation);
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("lambda", "_initial")]
    [InlineData("lambda", "Initial")]
    [InlineData("lambda", "Read(path)")]
    [InlineData("local function", "_initial")]
    [InlineData("local function", "Initial")]
    [InlineData("local function", "Read(path)")]
    public async Task refuses_implicit_instance_capture_in_deferred_consumer(string form, string expression)
    {
        using var fixture = await CreateAsync(deferredForm: form, deferredExpression: expression);
        var original = fixture.Solution;
        await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var (root, model) = await InspectAsync(original, "Consumer.cs");
        var selectedCalls = CallsBoundTo(root, model, SourceId);
        selectedCalls.Length.ShouldBe(2);
        selectedCalls.All(call => !call.Ancestors().Any(node =>
            node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)).ShouldBeTrue(
            "selected source calls must remain in ordinary constructor and method contexts");
        var escape = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Escape");
        SyntaxNode deferredBody = form == "lambda"
            ? escape.DescendantNodes().OfType<LambdaExpressionSyntax>().ShouldHaveSingleItem().Body
            : escape.DescendantNodes().OfType<LocalFunctionStatementSyntax>().ShouldHaveSingleItem().ExpressionBody!.Expression;
        var capture = model.GetOperation(deferredBody).ShouldNotBeNull();
        var receiver = capture switch
        {
            IFieldReferenceOperation field => field.Instance,
            IPropertyReferenceOperation property => property.Instance,
            IInvocationOperation call => call.Instance,
            _ => null
        };
        receiver.ShouldBeAssignableTo<IInstanceReferenceOperation>()!.IsImplicit.ShouldBeTrue(
            "the deferred expression must bind to an implicit instance receiver, not merely contain deferred syntax");
        var (callerRoot, callerModel) = await InspectAsync(original, "Callers.cs");
        CallsBoundTo(callerRoot, callerModel, "M:Fixture.Consumer.Escape(System.String)")
            .ShouldHaveSingleItem().Parent.ShouldBeAssignableTo<ReturnStatementSyntax>();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(original, fixture.Request);

        AssertBlockedWithoutCandidate(plan, original);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && !string.IsNullOrWhiteSpace(site.Code)
            && site.Message.Contains("implicit", StringComparison.OrdinalIgnoreCase)
            && site.Message.Contains("instance", StringComparison.OrdinalIgnoreCase)
            && site.Message.Contains("captur", StringComparison.OrdinalIgnoreCase)
            && site.Message.Contains(form, StringComparison.OrdinalIgnoreCase),
            "refusal must identify implicit instance capture in the lambda or local function");
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("relative-document-path", "introduction.document")]
    [InlineData("reflection-activation", "introduction.reflection:")]
    [InlineData("no-construction-sites", "introduction.callers")]
    [InlineData("nonlocal-construction", "introduction.consumer:")]
    [InlineData("explicit-self-escape", "introduction.self-escape:")]
    [InlineData("instance-method-group", "introduction.method-escape:")]
    [InlineData("inherited-instance-call", "introduction.inherited-instance:")]
    [InlineData("nested-selected-calls", "introduction.context")]
    [InlineData("selected-call-in-lambda", "introduction.context")]
    [InlineData("nonmatching-target-parameter", null)]
    public async Task preserves_atomic_refusal_for_unproved_closure_and_binding(string refusal, string? diagnosticPrefix)
    {
        using var fixture = await CreateAsync(refusal);
        var original = fixture.Solution;
        await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var (root, model) = await InspectAsync(original, "Consumer.cs");
        var selectedCalls = CallsBoundTo(root, model, SourceId);
        selectedCalls.Length.ShouldBe(refusal is "nested-selected-calls" or "selected-call-in-lambda" ? 3 : 2);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(original, fixture.Request);

        AssertBlockedWithoutCandidate(plan, original);
        if (diagnosticPrefix is not null)
            plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
                && site.Code.StartsWith(diagnosticPrefix, StringComparison.Ordinal));
        if (refusal == "nonmatching-target-parameter")
            plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
                && site.Message.Contains("path", StringComparison.Ordinal)
                && site.Message.Contains("CS1739", StringComparison.Ordinal),
                "a named argument that cannot bind must be reported after candidate verification");
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task refuses_unknown_site_selection_without_inventing_selected_calls()
    {
        using var fixture = await CreateAsync();
        var request = fixture.Request with
        {
            Selection = fixture.Request.Selection with { SiteIds = ["unknown-original-site"] }
        };

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, request);

        AssertBlockedWithoutCandidate(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Code == "introduction.sites"
            && site.Disposition == StaticDependencySiteDisposition.Blocked);
        var sourceCalls = plan.Sites.Where(site => site.Path.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
        sourceCalls.Length.ShouldBe(3);
        sourceCalls.ShouldAllBe(site => site.Disposition == StaticDependencySiteDisposition.Skipped);
        plan.Sites.ShouldNotContain(site => site.Id == "unknown-original-site");
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("missing-caller-binding")]
    [InlineData("multiple-constructors")]
    [InlineData("public-consumer")]
    [InlineData("escaping-consumer")]
    [InlineData("missing-acquisition")]
    [InlineData("stale-acquisition")]
    [InlineData("stale-fingerprint")]
    [InlineData("partial-consumer")]
    [InlineData("optional-constructor")]
    [InlineData("implicit-constructor")]
    [InlineData("field-collision")]
    [InlineData("parameter-collision")]
    [InlineData("invalid-field-name")]
    [InlineData("duplicate-caller")]
    [InlineData("unknown-caller")]
    [InlineData("invalid-caller-ordinal")]
    [InlineData("wrong-caller-type")]
    [InlineData("outside-caller-project")]
    [InlineData("wrong-source-assembly")]
    [InlineData("unknown-target")]
    [InlineData("wrong-receiver-type")]
    [InlineData("missing-precondition")]
    [InlineData("unknown-schema")]
    [InlineData("existing-receiver")]
    public async Task refuses_unproven_wiring_without_a_partial_candidate(string refusal)
    {
        using var fixture = await CreateAsync(refusal);
        var original = fixture.Solution;
        var input = refusal == "stale-acquisition"
            ? original.WithDocumentText(FindDocument(original, "Consumer.cs").Id, SourceText.From(Consumer + "\n// later snapshot"))
            : original;
        await AssertCompilesAsync(input);
        var request = fixture.Request with { ExpectedSourceFingerprint = await SolutionRevision.ComputeAsync(input) };
        if (refusal == "missing-caller-binding")
            request = request with { Callers = request.Callers.Take(1).ToArray() };
        if (refusal == "stale-fingerprint")
            request = request with { ExpectedSourceFingerprint = new string('0', 64) };
        request = refusal switch
        {
            "field-collision" => request with { PreferredFieldName = "_code" },
            "parameter-collision" => request with { PreferredParameterName = "path" },
            "invalid-field-name" => request with { PreferredFieldName = "class" },
            "duplicate-caller" => request with { Callers = request.Callers.Append(request.Callers[0]).ToArray() },
            "unknown-caller" => request with
            {
                Callers = [request.Callers[0] with { MethodDocumentationId = "M:Fixture.Callers.Missing" }, request.Callers[1]]
            },
            "invalid-caller-ordinal" => request with
            {
                Callers = [request.Callers[0] with { ParameterOrdinal = 99 }, request.Callers[1]]
            },
            "wrong-caller-type" => request with
            {
                Callers = [request.Callers[0] with { ParameterOrdinal = 0 }, request.Callers[1]]
            },
            "outside-caller-project" => request with
            {
                Callers = [request.Callers[0] with { ProjectPath = fixture.PathFor("Unloaded.csproj") }, request.Callers[1]]
            },
            "wrong-source-assembly" => request with
            {
                Mapping = request.Mapping with
                {
                    Source = request.Mapping.Source with { AssemblyIdentity = request.Mapping.Target.AssemblyIdentity }
                }
            },
            "unknown-target" => request with
            {
                Mapping = request.Mapping with
                {
                    Target = request.Mapping.Target with { DocumentationId = "M:Fixture.IReader.Missing(System.String)" }
                }
            },
            "wrong-receiver-type" => request with
            {
                Mapping = request.Mapping with
                {
                    ReceiverType = request.Mapping.ReceiverType with { DocumentationId = ConsumerId }
                }
            },
            "missing-precondition" => request with
            {
                Mapping = request.Mapping with
                {
                    Preconditions = request.Mapping.Preconditions with { ReceiverAvailability = "" }
                }
            },
            "unknown-schema" => request with { Mapping = request.Mapping with { SchemaVersion = "unsupported" } },
            "existing-receiver" => request with { ExistingReceiverFieldDocumentationId = FieldId },
            _ => request
        };
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(input, CancellationToken.None);
        completeness.State.ShouldBe(refusal is "missing-acquisition" or "stale-acquisition"
            ? WorkspaceCompletenessState.Partial : WorkspaceCompletenessState.Complete);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(input, request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked, refusal);
        plan.Documents.ShouldBeEmpty();
        plan.Closure.ShouldBeEmpty();
        plan.Changes.HasChanges.ShouldBeFalse();
        plan.Changes.AffectedPaths.ShouldBeEmpty();
        plan.Changes.OriginalSolution.ShouldBeSameAs(input);
        plan.Changes.ChangedSolution.ShouldBeSameAs(input);
        AssertNoFileEffects(plan.Changes);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate);
        var blockers = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.Blocked).ToArray();
        blockers.ShouldNotBeEmpty();
        foreach (var blocker in blockers)
        {
            blocker.Code.ShouldNotBeNullOrWhiteSpace();
            blocker.Message.ShouldNotBeNullOrWhiteSpace();
        }
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("assembly")]
    [InlineData("references")]
    [InlineData("document-membership")]
    [InlineData("additional-input")]
    [InlineData("analyzer-input")]
    [InlineData("compilation")]
    public async Task refuses_candidate_changes_outside_the_acquired_source_contract(string change)
    {
        using var fixture = await CreateAsync("non-source-inputs");
        var original = fixture.Solution;
        var project = original.Projects.Single();
        var acquired = await StaticDependencyPreparation.AcquireAsync(original, fixture.Request.Selection,
            fixture.Request.ExpectedSourceFingerprint, CancellationToken.None);
        acquired.Blockers.ShouldBeEmpty();
        var preparation = acquired.Preparation.ShouldNotBeNull();
        using var otherWorkspace = CreateWorkspace(fixture.Root, fixture.Sources);
        var candidate = change switch
        {
            "workspace" => otherWorkspace.CurrentSolution,
            "assembly" => original.WithProjectAssemblyName(project.Id, "DifferentAssembly"),
            "references" => original.WithProjectMetadataReferences(project.Id, project.MetadataReferences.Take(1)),
            "document-membership" => original.RemoveDocument(FindDocument(original, "Contracts.cs").Id),
            "additional-input" => original.WithAdditionalDocumentText(project.AdditionalDocumentIds.Single(), SourceText.From("changed")),
            "analyzer-input" => original.WithAnalyzerConfigDocumentText(project.AnalyzerConfigDocuments.Single().Id,
                SourceText.From("root = true\n[*]\nindent_size = 8\n")),
            "compilation" => original.WithDocumentText(FindDocument(original, "Consumer.cs").Id,
                SourceText.From(Consumer.Replace("_code = code;", "_code = missing;", StringComparison.Ordinal))),
            _ => original
        };
        var blockers = await preparation.VerifyAsync(candidate, CancellationToken.None);
        blockers.ShouldNotBeEmpty(change);
        blockers.ShouldAllBe(blocker => blocker.Disposition == StaticDependencySiteDisposition.Blocked);
        blockers.ShouldAllBe(blocker => !string.IsNullOrWhiteSpace(blocker.Code)
            && !string.IsNullOrWhiteSpace(blocker.Message));
        if (change == "compilation")
            blockers.ShouldContain(blocker => blocker.Message.Contains("CS0103", StringComparison.Ordinal));
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("field-readonly")]
    [InlineData("field-visibility")]
    [InlineData("assignment-value")]
    [InlineData("caller-value")]
    [InlineData("original-argument")]
    public async Task rejects_compiling_candidate_corruption_at_the_wiring_boundary(string corruption)
    {
        using var fixture = await CreateAsync();
        var original = fixture.Solution;
        var acquired = await StaticDependencyPreparation.AcquireAsync(original, fixture.Request.Selection,
            fixture.Request.ExpectedSourceFingerprint, CancellationToken.None);
        acquired.Blockers.ShouldBeEmpty();
        var compilation = await AssertCompilesAsync(original);
        var receiver = compilation.GetTypeByMetadataName("Fixture.IReader").ShouldNotBeNull();
        var callers = fixture.Request.Callers.Select(binding =>
            DocumentationCommentId.GetSymbolsForDeclarationId(binding.MethodDocumentationId, compilation)
                .OfType<IMethodSymbol>().Single().Parameters[binding.ParameterOrdinal]).ToArray();
        var closure = new StaticDependencyIntroductionClosure(acquired.Preparation.ShouldNotBeNull(), fixture.Request, receiver);
        (await closure.AnalyzeAsync(callers, CancellationToken.None)).ShouldBeEmpty();
        var introduced = await closure.IntroduceAsync(original, CancellationToken.None);
        await AssertCompilesAsync(introduced);
        var valid = await closure.VerifyAsync(introduced, CancellationToken.None);
        valid.Blockers.ShouldBeEmpty();
        valid.Changes.ShouldNotBeEmpty();
        var document = FindDocument(introduced, corruption is "caller-value" or "original-argument"
            ? "Callers.cs" : "Consumer.cs");
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        SyntaxNode changed;
        if (corruption is "field-readonly" or "field-visibility")
        {
            var field = root.DescendantNodes().OfType<FieldDeclarationSyntax>()
                .Single(declaration => declaration.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "_reader"));
            var modifiers = corruption == "field-readonly"
                ? SyntaxFactory.TokenList(field.Modifiers.Where(token => !token.IsKind(SyntaxKind.ReadOnlyKeyword)))
                : field.Modifiers.Replace(field.Modifiers.Single(token => token.IsKind(SyntaxKind.PrivateKeyword)),
                    SyntaxFactory.Token(SyntaxKind.InternalKeyword).WithTrailingTrivia(SyntaxFactory.Space));
            changed = root.ReplaceNode(field, field.WithModifiers(modifiers));
        }
        else if (corruption == "assignment-value")
        {
            var assignment = root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Single(expression => expression.Left.ToString() == "this._reader");
            changed = root.ReplaceNode(assignment, assignment.WithRight(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
        }
        else
        {
            var argument = corruption == "caller-value"
                ? root.DescendantNodes().OfType<ArgumentSyntax>().First(node => node.NameColon?.Name.Identifier.ValueText == "reader")
                : root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().First().ArgumentList!.Arguments[0];
            var value = corruption == "caller-value"
                ? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                : SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0));
            changed = root.ReplaceNode(argument, argument.WithExpression(value));
        }
        var corrupted = introduced.WithDocumentSyntaxRoot(document.Id, changed);
        await AssertCompilesAsync(corrupted);

        var refused = await closure.VerifyAsync(corrupted, CancellationToken.None);

        refused.Blockers.ShouldNotBeEmpty(corruption);
        refused.Changes.ShouldBeEmpty("no subset of corrupted wiring may be admitted");
        refused.Blockers.ShouldAllBe(blocker => blocker.Disposition == StaticDependencySiteDisposition.Blocked);
        await fixture.AssertUntouchedAsync();
    }

    private static async Task<(SemanticModel Model, MethodDeclarationSyntax Escape, InvocationExpressionSyntax[] SelectedCalls)>
        AssertDeferredConsumerAsync(Fixture fixture)
    {
        await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var selectedCalls = CallsBoundTo(root, model, SourceId);
        selectedCalls.Length.ShouldBe(2);
        selectedCalls.All(call => !call.Ancestors().Any(node =>
            node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)).ShouldBeTrue(
            "selected source calls must remain in ordinary constructor and method contexts");
        selectedCalls.Select(call => model.GetEnclosingSymbol(call.SpanStart)!.GetDocumentationCommentId())
            .ShouldBe([OriginalConstructorId, "M:Fixture.Consumer.Read(System.String)"]);
        var escape = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Escape");
        var (callerRoot, callerModel) = await InspectAsync(fixture.Solution, "Callers.cs");
        var returnedEscape = CallsBoundTo(callerRoot, callerModel, "M:Fixture.Consumer.Escape(System.String)")
            .ShouldHaveSingleItem();
        returnedEscape.Parent.ShouldBeAssignableTo<ReturnStatementSyntax>();
        var caller = callerModel.GetDeclaredSymbol(returnedEscape.Ancestors().OfType<MethodDeclarationSyntax>().First())
            .ShouldNotBeNull();
        caller.Name.ShouldBe("First");
        var invocation = callerModel.GetOperation(returnedEscape).ShouldBeAssignableTo<IInvocationOperation>()!;
        SymbolEqualityComparer.Default.Equals(caller.ReturnType, invocation.TargetMethod.ReturnType).ShouldBeTrue(
            "the caller must return the exact deferred result type without an adapter or conversion");
        var ordinaryRead = CallsBoundTo(callerRoot, callerModel, "M:Fixture.Consumer.Read(System.String)")
            .ShouldHaveSingleItem();
        ordinaryRead.Parent.ShouldBeAssignableTo<ReturnStatementSyntax>();
        callerModel.GetDeclaredSymbol(ordinaryRead.Ancestors().OfType<MethodDeclarationSyntax>().First())
            .ShouldNotBeNull().Name.ShouldBe("Second");
        var creations = callerRoot.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToArray();
        creations.Length.ShouldBe(2);
        foreach (var creation in creations)
            callerModel.GetOperation(creation).ShouldBeAssignableTo<IObjectCreationOperation>()!.Constructor!
                .GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        return (model, escape, selectedCalls);
    }

    private static async Task AssertDeferredRefusalAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        InvocationExpressionSyntax[] selectedCalls, string context)
    {
        AssertBlockedWithoutCandidate(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && !string.IsNullOrWhiteSpace(site.Code)
            && site.Message.Contains(context, StringComparison.OrdinalIgnoreCase)
            && (site.Message.Contains("instance", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("consumer", StringComparison.OrdinalIgnoreCase))
            && (site.Message.Contains("captur", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("escap", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("retain", StringComparison.OrdinalIgnoreCase)),
            "refusal must identify how the " + context + " can retain the selected consumer instance");
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        await fixture.AssertUntouchedAsync();
    }

    private static void AssertBlockedWithoutCandidate(StaticDependencyIntroductionPlan plan, Solution original)
    {
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        plan.Documents.ShouldBeEmpty();
        plan.Closure.ShouldBeEmpty();
        plan.Resolution.ShouldBeNull();
        plan.Changes.HasChanges.ShouldBeFalse();
        plan.Changes.AffectedPaths.ShouldBeEmpty();
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        plan.Changes.ChangedSolution.ShouldBeSameAs(original);
        AssertNoFileEffects(plan.Changes);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate);
    }

    private static async Task AssertAtomicSiteDispositionsAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        InvocationExpressionSyntax[] selectedCalls)
    {
        foreach (var call in selectedCalls)
        {
            var site = plan.Sites.Where(site => site.Path == FindDocument(fixture.Solution, "Consumer.cs").FilePath
                && site.SpanStart == call.SpanStart && site.SpanLength == call.Span.Length).ShouldHaveSingleItem();
            site.Disposition.ShouldBe(StaticDependencySiteDisposition.Blocked,
                "atomic refusal must block every selected source call");
            site.ProjectPath.ShouldBe(fixture.Request.Selection.ProjectPath);
            site.TypeDocumentationId.ShouldBe(ConsumerId);
            site.Code.ShouldNotBeNullOrWhiteSpace();
            site.Code.ShouldNotBe("selection.outside");
            site.Message.ShouldNotBeNullOrWhiteSpace();
        }
        var (adapterRoot, adapterModel) = await InspectAsync(fixture.Solution, "Contracts.cs");
        var adapterCall = CallsBoundTo(adapterRoot, adapterModel, SourceId).ShouldHaveSingleItem();
        var adapterSite = plan.Sites.Where(site => site.Path == fixture.PathFor("Contracts.cs")
            && site.SpanStart == adapterCall.SpanStart && site.SpanLength == adapterCall.Span.Length).ShouldHaveSingleItem();
        adapterSite.TypeDocumentationId.ShouldBe("T:Fixture.BclReader");
        adapterSite.Disposition.ShouldBe(StaticDependencySiteDisposition.Skipped,
            "the unselected adapter call must retain its unselected disposition");
    }

    private static async Task AssertDocumentChangesAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        string expectedConsumer, string expectedCallers)
    {
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var expectedPaths = new[] { fixture.PathFor("Callers.cs"), fixture.PathFor("Consumer.cs") };
        plan.Documents.Select(document => document.Path).ShouldBe(expectedPaths);
        plan.Changes.AffectedPaths.ShouldBe(expectedPaths);
        foreach (var change in plan.Documents)
        {
            var name = Path.GetFileName(change.Path);
            change.Before.ShouldBe(fixture.Sources[name]);
            var actual = (await FindDocument(candidate, name).GetTextAsync()).ToString();
            change.After.ShouldBe(actual, "the diff must contain the entire final document");
            SyntaxFactory.ParseCompilationUnit(actual).NormalizeWhitespace().ToFullString().ShouldBe(
                SyntaxFactory.ParseCompilationUnit(name == "Consumer.cs" ? expectedConsumer : expectedCallers)
                    .NormalizeWhitespace().ToFullString());
        }
        (await FindDocument(candidate, "Contracts.cs").GetTextAsync()).ToString().ShouldBe(Contracts);
        var delta = candidate.GetChanges(fixture.Solution);
        delta.GetAddedProjects().ShouldBeEmpty();
        delta.GetRemovedProjects().ShouldBeEmpty();
        var projectChange = delta.GetProjectChanges().ShouldHaveSingleItem();
        projectChange.GetChangedDocuments().Select(id => candidate.GetDocument(id)!.FilePath)
            .Order(StringComparer.Ordinal).ShouldBe(expectedPaths);
        projectChange.GetAddedDocuments().ShouldBeEmpty();
        projectChange.GetRemovedDocuments().ShouldBeEmpty();
    }

    private static async Task AssertCallerBindingsAsync(Solution original, Solution candidate,
        StaticDependencyIntroductionRequest request)
    {
        var (oldRoot, oldModel) = await InspectAsync(original, "Callers.cs");
        var (newRoot, newModel) = await InspectAsync(candidate, "Callers.cs");
        var before = oldRoot.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToArray();
        var after = newRoot.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToArray();
        before.Length.ShouldBe(2);
        after.Length.ShouldBe(before.Length);
        foreach (var (oldCreation, newCreation) in before.Zip(after))
        {
            var oldOperation = oldModel.GetOperation(oldCreation).ShouldBeAssignableTo<IObjectCreationOperation>()!;
            var newOperation = newModel.GetOperation(newCreation).ShouldBeAssignableTo<IObjectCreationOperation>()!;
            oldOperation.Constructor!.GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
            newOperation.Constructor!.GetDocumentationCommentId().ShouldBe(CandidateConstructorId);
            newOperation.Arguments.Length.ShouldBe(3);
            newOperation.Arguments.Take(2).Select(ArgumentBinding).ShouldBe(oldOperation.Arguments.Select(ArgumentBinding),
                "original named-argument order, conversions and nested calls must retain their bindings");
            var caller = newModel.GetDeclaredSymbol(newCreation.Ancestors().OfType<MethodDeclarationSyntax>().First())!;
            var binding = request.Callers.Single(value => value.MethodDocumentationId == caller.GetDocumentationCommentId());
            var argument = newOperation.Arguments[2];
            argument.Parameter!.Ordinal.ShouldBe(2);
            argument.InConversion.IsIdentity.ShouldBeTrue();
            var passed = argument.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter;
            passed.Ordinal.ShouldBe(binding.ParameterOrdinal);
            SymbolEqualityComparer.Default.Equals(passed, caller.Parameters[binding.ParameterOrdinal]).ShouldBeTrue();
        }
    }

    private static async Task AssertClosureAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        InvocationExpressionSyntax[] originalCalls, Compilation candidateCompilation)
    {
        plan.Closure.Count.ShouldBe(7);
        plan.Closure.Select(change => change.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(7);
        plan.Closure.Select(change => change.Kind).Order().ShouldBe(new[]
        {
            StaticDependencyClosureChangeKind.Field,
            StaticDependencyClosureChangeKind.ConstructorParameter,
            StaticDependencyClosureChangeKind.ConstructorAssignment,
            StaticDependencyClosureChangeKind.ConstructionArgument,
            StaticDependencyClosureChangeKind.ConstructionArgument,
            StaticDependencyClosureChangeKind.MappedCall,
            StaticDependencyClosureChangeKind.MappedCall
        }.Order());
        var originalCompilation = await AssertCompilesAsync(fixture.Solution);
        var (consumerRoot, _) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var originalType = consumerRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var originalConstructor = originalType.Members.OfType<ConstructorDeclarationSyntax>().Single();
        foreach (var change in plan.Closure)
        {
            change.Id.ShouldNotBeNullOrWhiteSpace();
            change.Detail.ShouldNotBeNullOrWhiteSpace();
            change.ProjectPath.ShouldBe(fixture.Request.Selection.ProjectPath);
            var source = fixture.Sources[Path.GetFileName(change.Path)];
            change.Path.ShouldBe(fixture.PathFor(Path.GetFileName(change.Path)));
            change.SpanStart.ShouldBeInRange(0, source.Length);
            change.SpanLength.ShouldBeInRange(0, source.Length - change.SpanStart);
            DocumentationCommentId.GetSymbolsForDeclarationId(change.CandidateSymbolDocumentationId, candidateCompilation)
                .ShouldHaveSingleItem();
            if (change.OriginalSymbolDocumentationId is { } originalId)
                DocumentationCommentId.GetSymbolsForDeclarationId(originalId, originalCompilation).ShouldHaveSingleItem();
            if (change.Kind is StaticDependencyClosureChangeKind.Field
                or StaticDependencyClosureChangeKind.ConstructorParameter or StaticDependencyClosureChangeKind.ConstructorAssignment)
            {
                change.Path.ShouldBe(fixture.PathFor("Consumer.cs"));
                var anchor = new TextSpan(change.SpanStart, change.SpanLength);
                (change.Kind == StaticDependencyClosureChangeKind.Field ? originalType.FullSpan : originalConstructor.FullSpan)
                    .Contains(anchor).ShouldBeTrue("introduced declarations must be anchored in the acquired original type/constructor");
            }
        }
        plan.Closure.Single(change => change.Kind == StaticDependencyClosureChangeKind.Field)
            .CandidateSymbolDocumentationId.ShouldBe(FieldId);
        plan.Closure.Single(change => change.Kind == StaticDependencyClosureChangeKind.ConstructorParameter)
            .CandidateSymbolDocumentationId.ShouldBe(CandidateConstructorId);
        plan.Closure.Where(change => change.Kind == StaticDependencyClosureChangeKind.MappedCall)
            .Select(change => (change.Path, change.SpanStart, change.SpanLength)).OrderBy(change => change.SpanStart)
            .ShouldBe(originalCalls.Select(call => (fixture.PathFor("Consumer.cs"), call.SpanStart, call.Span.Length)));
        var (root, _) = await InspectAsync(fixture.Solution, "Callers.cs");
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            plan.Closure.ShouldContain(change => change.Kind == StaticDependencyClosureChangeKind.ConstructionArgument
                && change.Path == fixture.PathFor("Callers.cs")
                && creation.Span.Contains(new TextSpan(change.SpanStart, change.SpanLength)));
    }

    private static (string Text, int Ordinal, string Type, ArgumentKind Kind, bool InIdentity, bool OutIdentity, string Calls)
        ArgumentBinding(IArgumentOperation argument) => (
            argument.Syntax.ToString(), argument.Parameter!.Ordinal, argument.Parameter.Type.ToDisplayString(),
            argument.ArgumentKind, argument.InConversion.IsIdentity, argument.OutConversion.IsIdentity,
            string.Join(";", argument.Value.DescendantsAndSelf().OfType<IInvocationOperation>()
                .Select(call => call.TargetMethod.GetDocumentationCommentId())));

    private static InvocationExpressionSyntax[] CallsBoundTo(SyntaxNode root, SemanticModel model, string id) =>
        root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => model.GetOperation(call) is IInvocationOperation operation
                && operation.TargetMethod.GetDocumentationCommentId() == id).ToArray();

    private static Document FindDocument(Solution solution, string name) =>
        solution.Projects.Single().Documents.Single(document => document.Name == name);

    private static async Task<(SyntaxNode Root, SemanticModel Model)> InspectAsync(Solution solution, string name)
    {
        var document = FindDocument(solution, name);
        return ((await document.GetSyntaxRootAsync()).ShouldNotBeNull(),
            (await document.GetSemanticModelAsync()).ShouldNotBeNull());
    }

    private static async Task<Compilation> AssertCompilesAsync(Solution solution)
    {
        var compilation = (await solution.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty("the independent fixture must compile, independently of planner behavior");
        return compilation;
    }

    private static async Task AssertCompleteAsync(Solution solution)
    {
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
        completeness.State.ShouldBe(WorkspaceCompletenessState.Complete, string.Join("; ", completeness.Diagnostics));
        WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence.ShouldBeTrue();
        completeness.RequestedProjects.ShouldBe([solution.Projects.Single().FilePath!]);
    }

    private static void AssertNoFileEffects(PreparedChangeSet changes)
    {
        changes.NewFiles.ShouldBeEmpty();
        changes.DeletedFiles.ShouldBeEmpty();
        changes.MovedFiles.ShouldBeEmpty();
        changes.ProjectFileEdits.ShouldBeEmpty();
        changes.TextEdits.ShouldBeEmpty();
    }

    private static AdhocWorkspace CreateWorkspace(string root, IReadOnlyDictionary<string, string> sources)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var references = new[]
        {
            typeof(object).Assembly.Location, typeof(File).Assembly.Location, typeof(Encoding).Assembly.Location,
            Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")
        }.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        workspace.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), ProjectName, ProjectName,
            LanguageNames.CSharp, filePath: Path.Combine(root, "Fixture.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12), metadataReferences: references));
        foreach (var (name, source) in sources)
            workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(projectId), name,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())),
                filePath: Path.Combine(root, name)));
        return workspace;
    }

    private static async Task<Fixture> CreateAsync(string? refusal = null, string? deferredForm = null,
        string? deferredExpression = null)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Contracts.cs"] = Contracts,
            ["Consumer.cs"] = Consumer,
            ["Callers.cs"] = Callers
        };
        if (refusal == "multiple-constructors")
            sources["Consumer.cs"] = Consumer.Replace("internal Consumer(int code, string path)",
                "internal Consumer(string path) : this(0, path) { }\n    internal Consumer(int code, string path)", StringComparison.Ordinal);
        if (refusal == "public-consumer")
            sources["Consumer.cs"] = Consumer.Replace("internal sealed class Consumer", "public sealed class Consumer", StringComparison.Ordinal);
        if (refusal == "escaping-consumer")
            sources["Callers.cs"] = Callers.Replace("static string First", "static Consumer First", StringComparison.Ordinal)
                .Replace("path: Trace(path));\n        return consumer.Read(path);",
                    "path: Trace(path));\n        return consumer;", StringComparison.Ordinal);
        if (refusal == "partial-consumer")
            sources["Consumer.cs"] = Consumer.Replace("internal sealed class Consumer", "internal sealed partial class Consumer", StringComparison.Ordinal);
        if (refusal == "optional-constructor")
            sources["Consumer.cs"] = Consumer.Replace("int code, string path)", "int code = 0, string path = \"default\")", StringComparison.Ordinal);
        if (refusal == "implicit-constructor")
        {
            sources["Consumer.cs"] = Consumer.Replace("""
                    internal Consumer(int code, string path)
                    {
                        _code = code;
                        _initial = FileAlias.ReadAllText(path: path + "-ctor");
                    }
                """, "", StringComparison.Ordinal);
            sources["Callers.cs"] = Callers.Replace("new Consumer(Stamp(code), path: Trace(path))", "new Consumer()", StringComparison.Ordinal)
                .Replace("new Consumer(path: Trace(path), code: Stamp(2))", "new Consumer()", StringComparison.Ordinal);
        }
        if (refusal == "reflection-activation")
            sources["Callers.cs"] = Callers.Replace("private static int _order;",
                "private static int _order;\n    private static object Activate(System.Type type) => System.Activator.CreateInstance(type);", StringComparison.Ordinal);
        if (refusal == "no-construction-sites")
            sources["Callers.cs"] = "namespace Fixture;";
        if (refusal == "nonlocal-construction")
            sources["Callers.cs"] = Callers.Replace("static string First", "static object First", StringComparison.Ordinal)
                .Replace("var consumer = new Consumer(Stamp(code), path: Trace(path));\n        return consumer.Read(path);",
                    "return new Consumer(Stamp(code), path: Trace(path));", StringComparison.Ordinal);
        var escapeMember = refusal switch
        {
            "explicit-self-escape" => "internal object Escape() => this;",
            "instance-method-group" => "internal System.Func<string, string> Escape() => Read;",
            "inherited-instance-call" => "internal object Clone() => MemberwiseClone();",
            "selected-call-in-lambda" => "internal System.Func<string> Deferred(string path) => () => System.IO.File.ReadAllText(path);",
            _ => null
        };
        if (escapeMember is not null)
            sources["Consumer.cs"] = Consumer.Replace("internal string Read(string path)",
                escapeMember + "\n    internal string Read(string path)", StringComparison.Ordinal);
        if (refusal == "nested-selected-calls")
            sources["Consumer.cs"] = Consumer.Replace("File.ReadAllText(path: path + \"-body\")",
                "File.ReadAllText(path: File.ReadAllText(path))", StringComparison.Ordinal);
        if (refusal == "nonmatching-target-parameter")
            sources["Contracts.cs"] = Contracts.Replace("string ReadAllText(string path);",
                "string ReadAllText(string fileName);", StringComparison.Ordinal);
        if (deferredForm is not null)
        {
            var returnType = deferredForm switch
            {
                "inherited method group" => "System.Func<object>",
                "async" => "System.Threading.Tasks.Task<string>",
                "iterator" => "System.Collections.Generic.IEnumerable<string>",
                _ => "System.Func<string>"
            };
            var escape = deferredForm switch
            {
                "lambda" => $"internal System.Func<string> Escape(string path) => () => {deferredExpression};",
                "inherited method group" => "internal System.Func<object> Escape(string path) => MemberwiseClone;",
                "async" => """
                    internal async System.Threading.Tasks.Task<string> Escape(string path)
                    {
                        await System.Threading.Tasks.Task.Yield();
                        return _initial;
                    }
                    """,
                "iterator" => """
                    internal System.Collections.Generic.IEnumerable<string> Escape(string path)
                    {
                        yield return _initial;
                    }
                    """,
                _ => $$"""
                    internal System.Func<string> Escape(string path)
                    {
                        string Later() => {{deferredExpression}};
                        return Later;
                    }
                    """
            };
            var property = deferredExpression == "Initial" ? "internal string Initial => _initial;\n    " : "";
            sources["Consumer.cs"] = Consumer.Replace("internal string Read(string path)",
                property + escape + "\n    internal string Read(string path)", StringComparison.Ordinal);
            sources["Callers.cs"] = Callers.Replace("static string First", "static " + returnType + " First", StringComparison.Ordinal)
                .Replace("path: Trace(path));\n        return consumer.Read(path);",
                    "path: Trace(path));\n        return consumer.Escape(path);", StringComparison.Ordinal);
        }
        var root = Path.Combine(Path.GetTempPath(), "rt153-simple-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (name, source) in sources)
            await File.WriteAllTextAsync(Path.Combine(root, name), source);
        await File.WriteAllTextAsync(Path.Combine(root, "Fixture.csproj"), "<Project />");
        var workspace = CreateWorkspace(root, sources);
        var solution = workspace.CurrentSolution;
        if (refusal == "relative-document-path")
        {
            solution = solution.WithDocumentFilePath(FindDocument(solution, "Consumer.cs").Id, "Consumer.cs");
            workspace.TryApplyChanges(solution).ShouldBeTrue();
            solution = workspace.CurrentSolution;
        }
        if (refusal == "non-source-inputs")
        {
            solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(solution.ProjectIds.Single()),
                    "settings.txt", SourceText.From("original"), filePath: Path.Combine(root, "settings.txt"))
                .AddAnalyzerConfigDocument(DocumentId.CreateNewId(solution.ProjectIds.Single()), ".editorconfig",
                    SourceText.From("root = true\n[*]\nindent_size = 4\n"), filePath: Path.Combine(root, ".editorconfig"));
            workspace.TryApplyChanges(solution).ShouldBeTrue();
            solution = workspace.CurrentSolution;
        }
        var compilation = await AssertCompilesAsync(solution);
        if (refusal != "missing-acquisition")
        {
            await ExplicitWorkspaceAcquisition.AttachAsync(solution, [ProjectName]);
            await AssertCompleteAsync(solution);
        }
        var sourceMethod = DocumentationCommentId.GetSymbolsForDeclarationId(SourceId, compilation).ShouldHaveSingleItem();
        var targetMethod = DocumentationCommentId.GetSymbolsForDeclarationId(TargetId, compilation).ShouldHaveSingleItem();
        var mapping = new StaticDependencyIntroductionMapping("rt.static-dependency.mapping.v1",
            new StaticDependencySymbol(SourceId, sourceMethod.ContainingAssembly.Identity.ToString()),
            new StaticDependencySymbol("T:Fixture.IReader", targetMethod.ContainingAssembly.Identity.ToString()),
            new StaticDependencySymbol(TargetId, targetMethod.ContainingAssembly.Identity.ToString()),
            new StaticDependencyPreconditions(
                "Assign the caller-supplied reader before any original constructor body statement.",
                "Callers guarantee reader lifetime covers the constructed instance.",
                "Callers guarantee their declared reader parameters are non-null.",
                "BclReader forwards the exact BCL overload without catching exceptions.",
                "BclReader preserves the one-string overload's encoding behavior.",
                "Both synchronous contracts have no cancellation parameter.",
                "BclReader retains no resources and returns the BCL result directly.",
                ["Controlled BclReader source forwards path directly; lifetime/non-null are caller declarations."]));
        var projectPath = solution.Projects.Single().FilePath!;
        StaticDependencyCallerParameter[] callers = refusal == "no-construction-sites" ? [] :
        [
            new StaticDependencyCallerParameter(projectPath, "M:Fixture.Callers.First(System.Int32,Fixture.IReader,System.String)", 1),
            new StaticDependencyCallerParameter(projectPath, "M:Fixture.Callers.Second(Fixture.IReader,System.String)", 0)
        ];
        foreach (var caller in callers)
        {
            var method = DocumentationCommentId.GetSymbolsForDeclarationId(caller.MethodDocumentationId, compilation)
                .ShouldHaveSingleItem().ShouldBeAssignableTo<IMethodSymbol>()!;
            method.Parameters[caller.ParameterOrdinal].Type.GetDocumentationCommentId().ShouldBe("T:Fixture.IReader");
        }
        var request = new StaticDependencyIntroductionRequest(mapping, new StaticDependencySelection(projectPath, ConsumerId),
            await SolutionRevision.ComputeAsync(solution), "_reader", "reader", callers);
        return new Fixture(workspace, solution, root, sources, request,
            WorkspaceAcquisitionRegistry.Get(workspace).Receipt);
    }

    private sealed record Fixture(AdhocWorkspace Workspace, Solution Solution, string Root,
        IReadOnlyDictionary<string, string> Sources, StaticDependencyIntroductionRequest Request,
        WorkspaceAcquisitionReceipt? Receipt) : IDisposable
    {
        public string PathFor(string name) => Path.Combine(Root, name);

        public async Task AssertUntouchedAsync()
        {
            Workspace.CurrentSolution.ShouldBeSameAs(Solution);
            WorkspaceAcquisitionRegistry.Get(Workspace).Receipt.ShouldBeSameAs(Receipt);
            (await SolutionRevision.ComputeAsync(Solution)).ShouldBe(Request.ExpectedSourceFingerprint);
            Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .ShouldBe(Sources.Keys.Append("Fixture.csproj").Select(PathFor).Order(StringComparer.Ordinal));
            (await File.ReadAllTextAsync(PathFor("Fixture.csproj"))).ShouldBe("<Project />");
            foreach (var (name, source) in Sources)
            {
                (await FindDocument(Solution, name).GetTextAsync()).ToString().ShouldBe(source);
                (await File.ReadAllTextAsync(PathFor(name))).ShouldBe(source);
            }
        }

        public void Dispose()
        {
            Workspace.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
