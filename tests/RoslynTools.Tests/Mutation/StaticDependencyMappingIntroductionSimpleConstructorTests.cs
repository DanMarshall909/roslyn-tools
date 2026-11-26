using System.Globalization;
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
    private const string ReadPart = "Consumer.Read.cs";
    private const string UnselectedPart = "Consumer.Probe.cs";
    private const string GeneratedPart = "Consumer.Probe.g.cs";
    private const string LinkedProject = "LinkedProbe";
    private const string Probe = "private const int PartialProbe = 153;";

    private static readonly int[] FirstCallerArgumentOrdinals = [0, 1];
    private static readonly int[] SecondCallerArgumentOrdinals = [1, 0];

    private static readonly string[] OrdinarySelectedCallOwners =
    [
        OriginalConstructorId,
        "M:Fixture.Consumer.Read(System.String)"
    ];

    private static readonly string[] SelectedRewriteOriginalCallTexts =
    [
        "FileAlias.ReadAllText(path: path + \"-ctor\")",
        "System.IO.File.ReadAllText(path)",
        "File.ReadAllText(path: path + \"-body\")"
    ];

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
        await AssertPreparesSingleConstructorAsync(fixture);
    }

    [Fact]
    public async Task prepares_one_constructor_across_authored_partial_declarations()
    {
        using var fixture = await CreatePartialAsync();
        var original = fixture.Solution;
        var request = fixture.Request;
        var compilation = await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var receipt = WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldNotBeNull();
        receipt.ShouldBeSameAs(fixture.Receipt);
        receipt.Solution.ShouldBeSameAs(original);
        request.ExpectedSourceFingerprint.ShouldBe(await SolutionRevision.ComputeAsync(original));

        var type = compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        type.GetDocumentationCommentId().ShouldBe(ConsumerId);
        type.DeclaredAccessibility.ShouldBe(Accessibility.Internal);
        type.IsSealed.ShouldBeTrue();
        type.Arity.ShouldBe(0);
        type.ContainingType.ShouldBeNull();
        var declarations = type.DeclaringSyntaxReferences.Select(reference =>
            reference.GetSyntax().ShouldBeAssignableTo<ClassDeclarationSyntax>()!).ToArray();
        declarations.Length.ShouldBe(3);
        foreach (var declaration in declarations)
        {
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword).ShouldBeTrue();
            declaration.ParameterList.ShouldBeNull();
        }
        var owners = declarations.Select(part => original.GetDocument(part.SyntaxTree).ShouldNotBeNull()).ToArray();
        owners.Select(document => document.Id).Distinct().Count().ShouldBe(2);
        owners.Select(document => document.Name).Order(StringComparer.Ordinal)
            .ShouldBe([ReadPart, ReadPart, "Consumer.cs"]);
        foreach (var owner in owners.DistinctBy(document => document.Id))
        {
            (owner is SourceGeneratedDocument).ShouldBeFalse();
            owner.Project.FilePath.ShouldBe(request.Selection.ProjectPath);
            owner.FilePath.ShouldBe(fixture.PathFor(owner.Name));
            Path.IsPathFullyQualified(owner.FilePath!).ShouldBeTrue();
            original.Projects.SelectMany(project => project.Documents).Count(document => document.FilePath == owner.FilePath)
                .ShouldBe(1);
            var input = receipt.Manifest.Inputs.Where(input => input.Kind == SemanticInputKind.SourceDocument
                && input.ProjectPath == owner.Project.FilePath && input.Path == owner.FilePath).ShouldHaveSingleItem();
            receipt.Outcomes.Where(outcome => outcome.InputId == input.Id).ShouldHaveSingleItem()
                .State.ShouldBe(SemanticInputState.Resolved);
        }
        var constructor = type.InstanceConstructors.ShouldHaveSingleItem();
        constructor.IsImplicitlyDeclared.ShouldBeFalse();
        constructor.GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        var constructorSyntax = constructor.DeclaringSyntaxReferences.ShouldHaveSingleItem().GetSyntax()
            .ShouldBeAssignableTo<ConstructorDeclarationSyntax>()!;
        original.GetDocument(constructorSyntax.SyntaxTree).ShouldNotBeNull().Name.ShouldBe("Consumer.cs");
        constructorSyntax.Initializer.ShouldBeNull();
        constructorSyntax.Body.ShouldNotBeNull();

        var (readRoot, readModel) = await InspectAsync(original, ReadPart);
        var (constructorRoot, constructorModel) = await InspectAsync(original, "Consumer.cs");
        var readCall = CallsBoundTo(readRoot, readModel, SourceId).ShouldHaveSingleItem();
        var constructorCall = CallsBoundTo(constructorRoot, constructorModel, SourceId).ShouldHaveSingleItem();
        readCall.ToString().ShouldBe(SelectedRewriteOriginalCallTexts[2]);
        constructorCall.ToString().ShouldBe(SelectedRewriteOriginalCallTexts[0]);
        readModel.GetEnclosingSymbol(readCall.SpanStart).ShouldNotBeNull().GetDocumentationCommentId()
            .ShouldBe(OrdinarySelectedCallOwners[1]);
        constructorModel.GetEnclosingSymbol(constructorCall.SpanStart).ShouldNotBeNull().GetDocumentationCommentId()
            .ShouldBe(OriginalConstructorId);
        var probe = readRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(part => part.Members.OfType<FieldDeclarationSyntax>().Any()).ShouldHaveSingleItem();
        probe.Members.ShouldHaveSingleItem().ToString().ShouldBe(Probe);
        CallsBoundTo(probe, readModel, SourceId).ShouldBeEmpty();
        var originals = new[]
        {
            (Name: ReadPart, Call: readCall, Model: readModel),
            (Name: "Consumer.cs", Call: constructorCall, Model: constructorModel)
        };
        foreach (var (_, call, model) in originals)
        {
            var source = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod;
            AssertSymbolIdentity(source, request.Mapping.Source, SourceId);
            source.Locations.ShouldAllBe(location => location.IsInMetadata);
        }
        var callers = await AssertOriginalPartialCallersAsync(original, request, constructor);
        var service = new StaticDependencyIntroductionService();

        var plan = await service.PrepareAsync(original, request);

        var siteCodes = string.Join(", ", plan.Sites.Select(site => site.Code));
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared,
            $"authored partial declarations must share one constructor wiring plan. Observed site codes [{siteCodes}]");
        plan.SchemaVersion.ShouldBe("rt.static-dependency.introduction-plan.v1");
        plan.Request.ShouldBe(request);
        plan.SourceFingerprint.ShouldBe(request.ExpectedSourceFingerprint);
        plan.MappingFingerprint.ShouldNotBeNullOrWhiteSpace();
        plan.Resolution.ShouldBe(new StaticDependencyIntroductionResolution(false, FieldId, "_reader", "reader"));
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        plan.Changes.HasChanges.ShouldBeTrue();
        AssertNoFileEffects(plan.Changes);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var candidateCompilation = await AssertCompilesAsync(candidate);
        var originalDiagnostics = compilation.GetDiagnostics().Select(DiagnosticIdentity);
        candidateCompilation.GetDiagnostics().Select(DiagnosticIdentity).Except(originalDiagnostics).ShouldBeEmpty();
        await AssertDocumentChangesAsync(fixture, plan, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Callers.cs"] = ExpectedWiredCallers(),
            [ReadPart] = fixture.Sources[ReadPart].Replace("File.ReadAllText(path: path + \"-body\")",
                "this._reader.ReadAllText(path: path + \"-body\")", StringComparison.Ordinal),
            ["Consumer.cs"] = ExpectedWiredConstructor(fixture.Sources["Consumer.cs"])
        });
        var field = await AssertPartialConstructorWiringAsync(candidate, candidateCompilation, request, constructorSyntax);
        foreach (var (name, before, oldModel) in originals)
        {
            var (root, model) = await InspectAsync(candidate, name);
            var after = CallsBoundTo(root, model, TargetId).ShouldHaveSingleItem();
            var bound = model.GetOperation(after).ShouldBeAssignableTo<IInvocationOperation>()!;
            AssertSymbolIdentity(bound.TargetMethod, request.Mapping.Target, TargetId);
            SymbolEqualityComparer.Default.Equals(bound.TargetMethod.ContainingType, field.Type).ShouldBeTrue();
            var receiver = bound.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
            SymbolEqualityComparer.Default.Equals(receiver.Field, field).ShouldBeTrue();
            receiver.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
            after.ArgumentList.ToString().ShouldBe(before.ArgumentList.ToString());
            bound.Arguments.Select(ArgumentBinding).ShouldBe(
                oldModel.GetOperation(before).ShouldBeAssignableTo<IInvocationOperation>()!.Arguments.Select(ArgumentBinding));
        }
        var (candidateReadRoot, candidateReadModel) = await InspectAsync(candidate, ReadPart);
        var candidateProbe = candidateReadRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(part => part.Members.OfType<FieldDeclarationSyntax>().Any()).ShouldHaveSingleItem();
        candidateProbe.ToFullString().ShouldBe(probe.ToFullString());
        var encoded = candidateReadRoot.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression.ToString() == "File.ReadAllText");
        var encodedMethod = candidateReadModel.GetOperation(encoded).ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod;
        encodedMethod.GetDocumentationCommentId().ShouldBe("M:System.IO.File.ReadAllText(System.String,System.Text.Encoding)");
        encodedMethod.ContainingAssembly.Identity.ToString().ShouldBe(request.Mapping.Source.AssemblyIdentity);
        var (adapterRoot, adapterModel) = await InspectAsync(candidate, "Contracts.cs");
        var adapter = CallsBoundTo(adapterRoot, adapterModel, SourceId).ShouldHaveSingleItem();
        AssertSymbolIdentity(adapterModel.GetOperation(adapter).ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod,
            request.Mapping.Source, SourceId);
        var (originalAdapterRoot, originalAdapterModel) = await InspectAsync(original, "Contracts.cs");
        var originalAdapter = CallsBoundTo(originalAdapterRoot, originalAdapterModel, SourceId).ShouldHaveSingleItem();
        await AssertCallerBindingsAsync(original, candidate, request);
        var selectedCalls = originals.Select(item => item.Call).ToArray();
        await AssertSiteDispositionsAsync(fixture, plan, selectedCalls, StaticDependencySiteDisposition.ConvertedInCandidate);
        plan.Sites.Select(site => (site.Path, site.SpanStart, site.SpanLength)).ShouldBe(
        [
            (fixture.PathFor("Contracts.cs"), originalAdapter.SpanStart, originalAdapter.Span.Length),
            (fixture.PathFor(ReadPart), readCall.SpanStart, readCall.Span.Length),
            (fixture.PathFor("Consumer.cs"), constructorCall.SpanStart, constructorCall.Span.Length)
        ]);
        AssertPartialClosure(fixture, plan, callers, constructorSyntax, readCall, constructorCall);
        await AssertRepeatAndFreshNoOpAsync(fixture, plan, service, receipt);
    }

    private static void AssertSymbolIdentity(ISymbol symbol, StaticDependencySymbol identity, string expectedId)
    {
        identity.DocumentationId.ShouldBe(expectedId);
        symbol.GetDocumentationCommentId().ShouldBe(expectedId);
        symbol.ContainingAssembly.Identity.ToString().ShouldBe(identity.AssemblyIdentity);
    }

    private static (string Id, DiagnosticSeverity Severity, string Message) DiagnosticIdentity(Diagnostic diagnostic) =>
        (diagnostic.Id, diagnostic.Severity, diagnostic.GetMessage(CultureInfo.InvariantCulture));

    private static async Task<ObjectCreationExpressionSyntax[]> AssertOriginalPartialCallersAsync(Solution original,
        StaticDependencyIntroductionRequest request, IMethodSymbol constructor)
    {
        var (root, model) = await InspectAsync(original, "Callers.cs");
        var creations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToArray();
        creations.Select(creation => creation.ToString()).ShouldBe([
            "new Consumer(Stamp(code), path: Trace(path))",
            "new Consumer(path: Trace(path), code: Stamp(2))"
        ]);
        request.Callers.Select(binding => binding.ParameterOrdinal).ShouldBe([1, 0]);
        for (var index = 0; index < creations.Length; index++)
        {
            var creation = creations[index];
            var method = creation.Ancestors().OfType<MethodDeclarationSyntax>().First();
            var caller = model.GetDeclaredSymbol(method).ShouldNotBeNull();
            caller.MethodKind.ShouldBe(MethodKind.Ordinary);
            caller.IsAsync.ShouldBeFalse();
            var binding = request.Callers[index];
            caller.GetDocumentationCommentId().ShouldBe(binding.MethodDocumentationId);
            binding.ProjectPath.ShouldBe(request.Selection.ProjectPath);
            var parameter = caller.Parameters[binding.ParameterOrdinal];
            parameter.Name.ShouldBe(index == 0 ? "input" : "alternate");
            parameter.RefKind.ShouldBe(RefKind.None);
            AssertSymbolIdentity(parameter.Type, request.Mapping.ReceiverType, "T:Fixture.IReader");
            var body = model.GetOperation(method.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
            body.Operations.Length.ShouldBe(2);
            var local = body.Operations[0].DescendantsAndSelf().OfType<IVariableDeclaratorOperation>().ShouldHaveSingleItem();
            var operation = local.Initializer.ShouldNotBeNull().Value.ShouldBeAssignableTo<IObjectCreationOperation>()!;
            SymbolEqualityComparer.Default.Equals(operation.Constructor, constructor).ShouldBeTrue();
            operation.Arguments.Select(argument => argument.Parameter!.Ordinal)
                .ShouldBe(index == 0 ? FirstCallerArgumentOrdinals : SecondCallerArgumentOrdinals);
            var stamp = "M:Fixture.Callers.Stamp(System.Int32)";
            var trace = "M:Fixture.Callers.Trace(System.String)";
            operation.Arguments.Select(argument => argument.Value.ShouldBeAssignableTo<IInvocationOperation>()!
                .TargetMethod.GetDocumentationCommentId()).ShouldBe(index == 0 ? new[] { stamp, trace } : new[] { trace, stamp });
            var read = body.Operations[1].ShouldBeAssignableTo<IReturnOperation>()!.ReturnedValue
                .ShouldBeAssignableTo<IInvocationOperation>()!;
            read.TargetMethod.GetDocumentationCommentId().ShouldBe(OrdinarySelectedCallOwners[1]);
            var instance = read.Instance.ShouldBeAssignableTo<ILocalReferenceOperation>()!;
            SymbolEqualityComparer.Default.Equals(instance.Local, local.Symbol).ShouldBeTrue();
        }
        return creations;
    }

    private static async Task<IFieldSymbol> AssertPartialConstructorWiringAsync(Solution candidate, Compilation compilation,
        StaticDependencyIntroductionRequest request, ConstructorDeclarationSyntax originalConstructor)
    {
        var type = compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        type.DeclaringSyntaxReferences.Length.ShouldBe(3);
        var field = type.GetMembers("_reader").OfType<IFieldSymbol>().ShouldHaveSingleItem();
        field.GetDocumentationCommentId().ShouldBe(FieldId);
        field.IsReadOnly.ShouldBeTrue();
        field.IsStatic.ShouldBeFalse();
        field.DeclaredAccessibility.ShouldBe(Accessibility.Private);
        AssertSymbolIdentity(field.Type, request.Mapping.ReceiverType, "T:Fixture.IReader");
        var (root, model) = await InspectAsync(candidate, "Consumer.cs");
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ShouldHaveSingleItem();
        var fieldDeclaration = field.DeclaringSyntaxReferences.ShouldHaveSingleItem().GetSyntax();
        fieldDeclaration.SyntaxTree.ShouldBeSameAs(constructor.SyntaxTree);
        fieldDeclaration.Ancestors().OfType<ClassDeclarationSyntax>().First().Span.ShouldBe(constructor.Parent!.Span);
        var constructorSymbol = type.InstanceConstructors.ShouldHaveSingleItem();
        constructorSymbol.GetDocumentationCommentId().ShouldBe(CandidateConstructorId);
        constructorSymbol.ContainingAssembly.Identity.ToString().ShouldBe(request.Mapping.ReceiverType.AssemblyIdentity);
        constructor.Initializer.ShouldBeNull();
        var parameters = constructor.ParameterList.Parameters;
        parameters.Count.ShouldBe(originalConstructor.ParameterList.Parameters.Count + 1);
        parameters.Take(2).Select(parameter => parameter.ToString())
            .ShouldBe(originalConstructor.ParameterList.Parameters.Select(parameter => parameter.ToString()));
        var reader = constructorSymbol.Parameters[2];
        reader.Name.ShouldBe("reader");
        reader.RefKind.ShouldBe(RefKind.None);
        SymbolEqualityComparer.Default.Equals(reader.Type, field.Type).ShouldBeTrue();
        var statements = constructor.Body.ShouldNotBeNull().Statements;
        statements.Count.ShouldBe(originalConstructor.Body!.Statements.Count + 1);
        var assignment = model.GetOperation(statements[0]).ShouldBeAssignableTo<IExpressionStatementOperation>()!
            .Operation.ShouldBeAssignableTo<ISimpleAssignmentOperation>()!;
        var target = assignment.Target.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        target.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
        SymbolEqualityComparer.Default.Equals(target.Field, field).ShouldBeTrue();
        var value = assignment.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!;
        SymbolEqualityComparer.Default.Equals(value.Parameter, reader).ShouldBeTrue();
        return field;
    }

    private static void AssertPartialClosure(Fixture fixture, StaticDependencyIntroductionPlan plan,
        ObjectCreationExpressionSyntax[] callers, ConstructorDeclarationSyntax constructor,
        InvocationExpressionSyntax readCall, InvocationExpressionSyntax constructorCall)
    {
        var expected = new (StaticDependencyClosureChangeKind Kind, SyntaxNode Anchor, string? BeforeId, string AfterId)[]
        {
            (StaticDependencyClosureChangeKind.ConstructionArgument, callers[0], OriginalConstructorId, CandidateConstructorId),
            (StaticDependencyClosureChangeKind.ConstructionArgument, callers[1], OriginalConstructorId, CandidateConstructorId),
            (StaticDependencyClosureChangeKind.MappedCall, readCall, SourceId, TargetId),
            (StaticDependencyClosureChangeKind.Field, constructor.Parent!, null, FieldId),
            (StaticDependencyClosureChangeKind.ConstructorParameter, constructor, OriginalConstructorId, CandidateConstructorId),
            (StaticDependencyClosureChangeKind.ConstructorAssignment, constructor, OriginalConstructorId, CandidateConstructorId),
            (StaticDependencyClosureChangeKind.MappedCall, constructorCall, SourceId, TargetId)
        };
        plan.Closure.Select(change => change.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(expected.Length);
        plan.Closure.ShouldAllBe(change => change.ProjectPath == fixture.Request.Selection.ProjectPath
            && !string.IsNullOrWhiteSpace(change.Id) && !string.IsNullOrWhiteSpace(change.Detail));
        plan.Closure.Select(change => (change.Kind, change.Path, change.SpanStart, change.SpanLength,
                change.OriginalSymbolDocumentationId, change.CandidateSymbolDocumentationId))
            .ShouldBe(expected.Select(change => (change.Kind, change.Anchor.SyntaxTree.FilePath,
                change.Anchor.SpanStart, change.Anchor.Span.Length, change.BeforeId, change.AfterId)));
    }

    [Theory]
    [InlineData("local-collision", "introduction.names")]
    [InlineData("self-escape", "introduction.self-escape:")]
    [InlineData("deferred", "introduction.explicit-instance-capture:")]
    [InlineData("async", "introduction.state-machine-capture:")]
    [InlineData("iterator", "introduction.state-machine-capture:")]
    [InlineData("finalizer", "introduction.finalizer")]
    [InlineData("generated", "introduction.document")]
    [InlineData("relative", "introduction.document")]
    [InlineData("shared", "introduction.document")]
    [InlineData("primary", "introduction.declaration")]
    [InlineData("implicit", "introduction.constructors")]
    [InlineData("chained", "introduction.constructor-shape")]
    [InlineData("multiple", "introduction.constructors")]
    public async Task refuses_unsafe_partial_introduction_atomically(string form, string code)
    {
        using var fixture = await CreatePartialAsync(form);
        var original = fixture.Solution;
        var compilation = await AssertCompilesAsync(original);
        var linkedProjectPath = fixture.PathFor("LinkedProbe.csproj");
        if (form == "shared")
            await AssertCompleteAsync(original, linkedProjectPath);
        else
            await AssertCompleteAsync(original);
        WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldNotBeNull().Solution.ShouldBeSameAs(original);
        var type = compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        var (constructorRoot, constructorModel) = await InspectAsync(original, "Consumer.cs");
        var (readRoot, readModel) = await InspectAsync(original, ReadPart);
        var selectedCalls = CallsBoundTo(constructorRoot, constructorModel, SourceId)
            .Concat(CallsBoundTo(readRoot, readModel, SourceId)).ToArray();
        selectedCalls.Length.ShouldBe(2);
        var declarations = type.DeclaringSyntaxReferences.Select(reference =>
            reference.GetSyntax().ShouldBeAssignableTo<ClassDeclarationSyntax>()!).ToArray();
        declarations.Length.ShouldBe(3);
        var probe = declarations.Where(part => part.SyntaxTree != constructorRoot.SyntaxTree
            && !part.Members.OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == "Read"))
            .ShouldHaveSingleItem();
        var probeModel = compilation.GetSemanticModel(probe.SyntaxTree);
        SymbolEqualityComparer.Default.Equals(probeModel.GetDeclaredSymbol(probe), type).ShouldBeTrue();
        CallsBoundTo(probe, probeModel, SourceId).ShouldBeEmpty();

        switch (form)
        {
            case "local-collision":
                probe.SyntaxTree.ShouldBeSameAs(readRoot.SyntaxTree);
                type.GetMembers("_reader").ShouldBeEmpty();
                var local = probe.DescendantNodes().OfType<VariableDeclaratorSyntax>().ShouldHaveSingleItem();
                probeModel.GetDeclaredSymbol(local).ShouldBeAssignableTo<ILocalSymbol>()!.Name.ShouldBe("_reader");
                break;
            case "self-escape":
                original.GetDocument(probe.SyntaxTree).ShouldNotBeNull().Name.ShouldBe(UnselectedPart);
                var self = probe.DescendantNodes().OfType<ThisExpressionSyntax>().ShouldHaveSingleItem();
                self.Parent.ShouldBeAssignableTo<ArrowExpressionClauseSyntax>();
                var receiver = probeModel.GetOperation(self).ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
                SymbolEqualityComparer.Default.Equals(receiver.Type, type).ShouldBeTrue();
                break;
            case "deferred":
                probe.SyntaxTree.ShouldBeSameAs(readRoot.SyntaxTree);
                var lambda = probe.DescendantNodes().OfType<LambdaExpressionSyntax>().ShouldHaveSingleItem();
                probeModel.GetOperation(lambda).ShouldBeAssignableTo<IAnonymousFunctionOperation>();
                var field = probeModel.GetOperation(lambda.Body).ShouldBeAssignableTo<IFieldReferenceOperation>()!;
                field.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._initial");
                field.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!.IsImplicit.ShouldBeFalse();
                break;
            case "async":
            case "iterator":
                original.GetDocument(probe.SyntaxTree).ShouldNotBeNull().Name.ShouldBe(UnselectedPart);
                var method = probe.Members.OfType<MethodDeclarationSyntax>().ShouldHaveSingleItem();
                probeModel.GetDeclaredSymbol(method).ShouldNotBeNull().IsAsync.ShouldBe(form == "async");
                method.DescendantNodes().OfType<AwaitExpressionSyntax>().Count().ShouldBe(form == "async" ? 1 : 0);
                method.DescendantNodes().OfType<YieldStatementSyntax>().Count().ShouldBe(form == "iterator" ? 1 : 0);
                var captured = method.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Single(identifier => identifier.Identifier.ValueText == "_initial");
                var access = probeModel.GetOperation(captured).ShouldBeAssignableTo<IFieldReferenceOperation>()!;
                access.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._initial");
                access.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
                break;
            case "finalizer":
                original.GetDocument(probe.SyntaxTree).ShouldNotBeNull().Name.ShouldBe(UnselectedPart);
                var finalizer = probe.Members.OfType<DestructorDeclarationSyntax>().ShouldHaveSingleItem();
                probeModel.GetDeclaredSymbol(finalizer).ShouldNotBeNull().MethodKind.ShouldBe(MethodKind.Destructor);
                break;
            case "generated":
                var selectedProject = FindDocument(original, ReadPart).Project;
                var generated = (await selectedProject.GetSourceGeneratedDocumentsAsync()).ShouldHaveSingleItem();
                generated.Name.ShouldBe(GeneratedPart);
                (await generated.GetSyntaxTreeAsync()).ShouldBeSameAs(probe.SyntaxTree);
                selectedProject.DocumentIds.ShouldNotContain(generated.Id);
                fixture.Receipt.ShouldNotBeNull().Manifest.Inputs.ShouldContain(input =>
                    input.Kind == SemanticInputKind.GeneratedDocument && input.Path == generated.FilePath);
                break;
            case "relative":
                var relative = original.GetDocument(probe.SyntaxTree).ShouldNotBeNull();
                relative.FilePath.ShouldBe(UnselectedPart);
                Path.IsPathFullyQualified(relative.FilePath!).ShouldBeFalse();
                break;
            case "shared":
                var owned = original.GetDocument(probe.SyntaxTree).ShouldNotBeNull();
                owned.FilePath.ShouldBe(fixture.PathFor(UnselectedPart));
                var contexts = original.Projects.SelectMany(project => project.Documents)
                    .Where(document => document.FilePath == owned.FilePath).ToArray();
                contexts.Select(document => document.Id).Distinct().Count().ShouldBe(2);
                contexts.Select(document => document.Project.FilePath).Order(StringComparer.Ordinal)
                    .ShouldBe(new[] { fixture.Request.Selection.ProjectPath, linkedProjectPath }.Order(StringComparer.Ordinal));
                var peer = contexts.Single(document => document.Id != owned.Id);
                (await peer.GetTextAsync()).ToString().ShouldBe(fixture.Sources[UnselectedPart]);
                break;
            case "primary":
                declarations.Where(part => part.ParameterList is not null).ShouldHaveSingleItem().SyntaxTree
                    .ShouldBeSameAs(constructorRoot.SyntaxTree);
                type.InstanceConstructors.ShouldHaveSingleItem().IsImplicitlyDeclared.ShouldBeFalse();
                constructorRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ShouldBeEmpty();
                break;
            case "implicit":
                declarations.ShouldAllBe(part => part.ParameterList == null);
                type.InstanceConstructors.ShouldHaveSingleItem().IsImplicitlyDeclared.ShouldBeTrue();
                break;
            case "chained":
                type.InstanceConstructors.ShouldHaveSingleItem();
                constructorRoot.DescendantNodes().OfType<ConstructorInitializerSyntax>().ShouldHaveSingleItem()
                    .IsKind(SyntaxKind.BaseConstructorInitializer).ShouldBeTrue();
                break;
            case "multiple":
                type.InstanceConstructors.Length.ShouldBe(2);
                constructorRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ShouldHaveSingleItem();
                var overload = probe.Members.OfType<ConstructorDeclarationSyntax>().ShouldHaveSingleItem();
                overload.Initializer.ShouldNotBeNull().IsKind(SyntaxKind.ThisConstructorInitializer).ShouldBeTrue();
                original.GetDocument(overload.SyntaxTree).ShouldNotBeNull().FilePath.ShouldBe(fixture.PathFor(UnselectedPart));
                break;
        }

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(original, fixture.Request);

        AssertBlockedWithoutCandidate(plan, original);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && (code.EndsWith(':') ? site.Code.StartsWith(code, StringComparison.Ordinal) : site.Code == code),
            $"the {form} declaration must block the whole plan with {code}");
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        await fixture.AssertUntouchedAsync();
    }

    private static async Task AssertPreparesSingleConstructorAsync(Fixture fixture)
    {
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
        var resolution = plan.Resolution.ShouldNotBeNull();
        resolution.ReusedExistingReceiver.ShouldBeFalse();
        resolution.FieldDocumentationId.ShouldBe(FieldId);
        resolution.FieldName.ShouldBe("_reader");
        resolution.ParameterName.ShouldBe("reader");
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        plan.Changes.HasChanges.ShouldBeTrue();
        AssertNoFileEffects(plan.Changes);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var candidateCompilation = await AssertCompilesAsync(candidate);

        // Independently specified complete syntax; formatting is not part of wiring semantics.
        var expectedConsumer = ExpectedWiredConstructor(fixture.Sources["Consumer.cs"])
            .Replace("File.ReadAllText(path: path + \"-body\")",
                "this._reader.ReadAllText(path: path + \"-body\")", StringComparison.Ordinal);
        await AssertDocumentChangesAsync(fixture, plan, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Callers.cs"] = ExpectedWiredCallers(),
            ["Consumer.cs"] = expectedConsumer
        });

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
        await AssertClosureAsync(fixture, plan, originalCalls, candidateCompilation, expectedSelectedCalls: 2);
        await AssertRepeatAndFreshNoOpAsync(fixture, plan, service, receipt);
    }

    private static async Task AssertRepeatAndFreshNoOpAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        StaticDependencyIntroductionService service, WorkspaceAcquisitionReceipt receipt)
    {
        var original = fixture.Solution;
        var request = fixture.Request;
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
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
        freshWorkspace.ShouldNotBeSameAs(fixture.Workspace);
        var reacquired = freshWorkspace.CurrentSolution;
        await ExplicitWorkspaceAcquisition.AttachAsync(reacquired, [ProjectName]);
        var freshReceipt = WorkspaceAcquisitionRegistry.Get(freshWorkspace).Receipt.ShouldNotBeNull();
        freshReceipt.ShouldNotBeSameAs(receipt);
        freshReceipt.Solution.ShouldBeSameAs(reacquired);
        await AssertCompleteAsync(reacquired);
        await AssertCompilesAsync(reacquired);
        var candidateFingerprint = await SolutionRevision.ComputeAsync(reacquired);
        candidateFingerprint.ShouldBe(await SolutionRevision.ComputeAsync(candidate));
        candidateFingerprint.ShouldNotBe(plan.SourceFingerprint);
        var noOp = await service.PrepareAsync(reacquired, request with { ExpectedSourceFingerprint = candidateFingerprint });
        noOp.Status.ShouldBe(StaticDependencyPlanStatus.NoOp);
        noOp.SourceFingerprint.ShouldBe(candidateFingerprint);
        noOp.MappingFingerprint.ShouldBe(plan.MappingFingerprint);
        noOp.Documents.ShouldBeEmpty();
        noOp.Closure.ShouldBeEmpty();
        noOp.Resolution.ShouldBeNull();
        noOp.Changes.HasChanges.ShouldBeFalse();
        noOp.Changes.AffectedPaths.ShouldBeEmpty();
        noOp.Changes.OriginalSolution.ShouldBeSameAs(reacquired);
        noOp.Changes.ChangedSolution.ShouldBeSameAs(reacquired);
        AssertNoFileEffects(noOp.Changes);
        var remainingSite = noOp.Sites.ShouldHaveSingleItem();
        remainingSite.Disposition.ShouldBe(StaticDependencySiteDisposition.Skipped);
        remainingSite.TypeDocumentationId.ShouldBe("T:Fixture.BclReader");
        freshWorkspace.CurrentSolution.ShouldBeSameAs(reacquired);
        WorkspaceAcquisitionRegistry.Get(freshWorkspace).Receipt.ShouldBeSameAs(freshReceipt);
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
    public async Task refuses_explicit_base_clone_escape()
    {
        using var fixture = await CreateAsync(deferredForm: "base clone");
        var (model, escape, selectedCalls) = await AssertBaseEscapeAsync(fixture);
        var invocation = model.GetOperation(escape.ExpressionBody!.Expression).ShouldBeAssignableTo<IInvocationOperation>()!;
        AssertObjectMethod(model, invocation.TargetMethod, "M:System.Object.MemberwiseClone");
        invocation.TargetMethod.IsVirtual.ShouldBeFalse();
        invocation.Arguments.ShouldBeEmpty();
        AssertBaseReceiver(model, invocation.Instance, model.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull());
        model.GetDeclaredSymbol(escape).ShouldNotBeNull().ReturnType.SpecialType.ShouldBe(SpecialType.System_Object);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "explicit base clone must not escape the introduced dependency");
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && !string.IsNullOrWhiteSpace(site.Code)
            && (site.Message.Contains("base", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("inherited", StringComparison.OrdinalIgnoreCase))
            && site.Message.Contains("consumer", StringComparison.OrdinalIgnoreCase)
            && (site.Message.Contains("escap", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("expos", StringComparison.OrdinalIgnoreCase)),
            "the refusal must explain how the inherited/base call can escape the selected consumer");
        AssertBlockedWithoutCandidate(plan, fixture.Solution);
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task refuses_explicit_base_method_group_escape()
    {
        using var fixture = await CreateAsync(deferredForm: "base method group");
        var (model, escape, selectedCalls) = await AssertBaseEscapeAsync(fixture);
        var creation = model.GetOperation(escape).ShouldNotBeNull().DescendantsAndSelf()
            .OfType<IDelegateCreationOperation>().ShouldHaveSingleItem();
        creation.Type!.OriginalDefinition.GetDocumentationCommentId().ShouldBe("T:System.Func`1");
        creation.Type.ShouldBeAssignableTo<INamedTypeSymbol>()!.TypeArguments.ShouldHaveSingleItem()
            .SpecialType.ShouldBe(SpecialType.System_Object);
        var target = creation.Target.ShouldBeAssignableTo<IMethodReferenceOperation>()!;
        AssertObjectMethod(model, target.Method, "M:System.Object.MemberwiseClone");
        target.Method.ReturnType.SpecialType.ShouldBe(SpecialType.System_Object);
        AssertBaseReceiver(model, target.Instance, model.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull());

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "an escaping explicit base method group must not retain the introduced dependency");
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "method group");
    }

    [Fact]
    public async Task refuses_explicit_base_capture_in_deferred_lambda()
    {
        using var fixture = await CreateAsync(deferredForm: "lambda", deferredExpression: "base.ToString()");
        var (model, escape, selectedCalls) = await AssertBaseEscapeAsync(fixture);
        var lambda = escape.DescendantNodes().OfType<LambdaExpressionSyntax>().ShouldHaveSingleItem();
        var function = model.GetOperation(lambda).ShouldBeAssignableTo<IAnonymousFunctionOperation>()!;
        var invocation = function.Body.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldHaveSingleItem();
        AssertObjectMethod(model, invocation.TargetMethod, "M:System.Object.ToString");
        var consumer = model.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        SymbolEqualityComparer.Default.Equals(function.Symbol.ContainingType, consumer).ShouldBeTrue();
        AssertBaseReceiver(model, invocation.Instance, consumer);
        function.Body.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldHaveSingleItem()
            .ShouldBeSameAs(invocation.Instance);
        OperationAncestors(invocation).OfType<IAnonymousFunctionOperation>().ShouldHaveSingleItem().ShouldBeSameAs(function);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "a deferred lambda's explicit base receiver must retain selected-consumer ownership");
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "lambda");
    }

    [Theory]
    [InlineData("async")]
    [InlineData("iterator")]
    public async Task refuses_explicit_base_capture_in_state_machine(string form)
    {
        using var fixture = await CreateAsync(deferredForm: form, deferredExpression: "base.ToString()");
        var (model, escape, selectedCalls) = await AssertBaseEscapeAsync(fixture);
        model.GetDeclaredSymbol(escape).ShouldNotBeNull().IsAsync.ShouldBe(form == "async");
        var body = model.GetOperation(escape.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        body.Operations.Length.ShouldBe(form == "async" ? 2 : 1);
        if (form == "async")
        {
            var suspension = body.Operations[0].ShouldBeAssignableTo<IExpressionStatementOperation>()!.Operation
                .ShouldBeAssignableTo<IAwaitOperation>()!;
            suspension.Operation.ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod
                .GetDocumentationCommentId().ShouldBe("M:System.Threading.Tasks.Task.Yield");
        }
        var returned = body.Operations.Last().ShouldBeAssignableTo<IReturnOperation>()!;
        returned.Kind.ShouldBe(form == "async" ? OperationKind.Return : OperationKind.YieldReturn);
        var invocation = returned.ReturnedValue.ShouldBeAssignableTo<IInvocationOperation>()!;
        AssertObjectMethod(model, invocation.TargetMethod, "M:System.Object.ToString");
        var consumer = model.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        AssertBaseReceiver(model, invocation.Instance, consumer);
        body.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldHaveSingleItem().ShouldBeSameAs(invocation.Instance);
        body.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldNotContain(receiver =>
            SymbolEqualityComparer.Default.Equals(receiver.Type, consumer),
            "the base receiver has Object static type; no Consumer-typed reference can mask this ownership gap");
        OperationAncestors(invocation).Where(operation =>
            operation is IAnonymousFunctionOperation or ILocalFunctionOperation).ShouldBeEmpty();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "an explicit base receiver must not escape through the " + form + " state machine");
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, form);
    }

    [Fact]
    public async Task refuses_selected_consumer_finalizer_read_after_caller_lifetime()
    {
        using var fixture = await CreateAsync(lifetimeForm: "selected finalizer");
        var (model, consumer, selectedCalls) = await AssertOrdinarySelectedSourcesAsync(fixture);
        var destructor = consumer.Members.OfType<DestructorDeclarationSyntax>().ShouldHaveSingleItem();
        var symbol = model.GetDeclaredSymbol(destructor).ShouldNotBeNull();
        symbol.MethodKind.ShouldBe(MethodKind.Destructor);
        symbol.IsStatic.ShouldBeFalse();
        var selectedType = model.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        SymbolEqualityComparer.Default.Equals(symbol.ContainingType, selectedType).ShouldBeTrue();
        selectedType.GetMembers().OfType<IMethodSymbol>().Where(method => method.MethodKind == MethodKind.Destructor)
            .ShouldHaveSingleItem().ShouldBe(symbol);
        var read = selectedType.GetMembers("Read").OfType<IMethodSymbol>().ShouldHaveSingleItem();
        read.IsStatic.ShouldBeFalse();
        read.MethodKind.ShouldBe(MethodKind.Ordinary);
        var body = model.GetOperation(destructor.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        var invocation = body.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, read).ShouldBeTrue();
        invocation.Syntax.ToString().ShouldBe("Read(\"late\")");
        var receiver = invocation.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        SymbolEqualityComparer.Default.Equals(receiver.Type, selectedType).ShouldBeTrue();
        SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(selectedCalls[1].SpanStart), read).ShouldBeTrue(
            "the finalizer reaches the exact instance method containing the selected source call");
        await AssertOrdinaryConsumerCallerAsync(fixture, read);
        fixture.Sources["Callers.cs"].ShouldBe(Callers);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "a selected consumer finalizer must not outlive the introduced dependency's caller lifetime");
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && !string.IsNullOrWhiteSpace(site.Code)
            && (site.Message.Contains("finaliz", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("destructor", StringComparison.OrdinalIgnoreCase))
            && site.Message.Contains("lifetime", StringComparison.OrdinalIgnoreCase),
            "the typed refusal must explain why finalization cannot satisfy the declared dependency lifetime");
        AssertBlockedWithoutCandidate(plan, fixture.Solution);
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task prepares_when_only_unrelated_nested_type_has_base_clone_and_finalizer()
    {
        using var fixture = await CreateAsync(lifetimeForm: "nested other");
        var (model, consumer, selectedCalls) = await AssertOrdinarySelectedSourcesAsync(fixture);
        selectedCalls.Length.ShouldBe(2);
        var selectedType = model.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        selectedType.GetMembers().OfType<IMethodSymbol>().ShouldNotContain(method => method.MethodKind == MethodKind.Destructor);
        consumer.Members.OfType<DestructorDeclarationSyntax>().ShouldBeEmpty();
        var other = selectedType.GetTypeMembers("Other").ShouldHaveSingleItem();
        other.GetDocumentationCommentId().ShouldBe("T:Fixture.Consumer.Other");
        SymbolEqualityComparer.Default.Equals(other, selectedType).ShouldBeFalse();
        var nested = consumer.Members.OfType<ClassDeclarationSyntax>().ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(nested), other).ShouldBeTrue();
        var clone = nested.Members.OfType<MethodDeclarationSyntax>().ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(clone).ShouldNotBeNull().ContainingType, other).ShouldBeTrue();
        var invocation = model.GetOperation(clone.ExpressionBody!.Expression).ShouldBeAssignableTo<IInvocationOperation>()!;
        AssertObjectMethod(model, invocation.TargetMethod, "M:System.Object.MemberwiseClone");
        AssertBaseReceiver(model, invocation.Instance, other);
        var destructor = nested.Members.OfType<DestructorDeclarationSyntax>().ShouldHaveSingleItem();
        var finalizer = model.GetDeclaredSymbol(destructor).ShouldNotBeNull();
        finalizer.MethodKind.ShouldBe(MethodKind.Destructor);
        SymbolEqualityComparer.Default.Equals(finalizer.ContainingType, other).ShouldBeTrue();
        model.GetOperation(destructor.Body!).ShouldBeAssignableTo<IBlockOperation>()!.Operations.ShouldBeEmpty();
        await AssertOrdinaryConsumerCallerAsync(fixture, selectedType.GetMembers("Read").OfType<IMethodSymbol>().ShouldHaveSingleItem());
        fixture.Sources["Callers.cs"].ShouldBe(Callers);

        await AssertPreparesSingleConstructorAsync(fixture);
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
    public async Task refuses_consumer_retention_in_async_caller()
    {
        using var fixture = await CreateAsync(callerForm: "async");
        var selectedCalls = await AssertCallerRetentionAsync(fixture, isAsync: true);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "async caller retention must block dependency introduction");
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "async", "introduction.caller-state-machine:");
    }

    [Fact]
    public async Task refuses_consumer_retention_in_iterator_caller()
    {
        using var fixture = await CreateAsync(callerForm: "iterator");
        var selectedCalls = await AssertCallerRetentionAsync(fixture, isAsync: false);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "iterator caller retention must block dependency introduction");
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "iterator", "introduction.caller-state-machine:");
    }

    [Fact]
    public async Task refuses_compiler_generated_query_selector_capture_in_caller()
        => await AssertCallerQueryRefusalAsync("query");

    [Fact]
    public async Task refuses_compiler_generated_query_predicate_capture_in_caller()
        => await AssertCallerQueryRefusalAsync("query-predicate");

    private static async Task AssertCallerQueryRefusalAsync(string form)
    {
        using var fixture = await CreateAsync(callerForm: form);
        var compilation = await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (consumerRoot, consumerModel) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var selectedCalls = CallsBoundTo(consumerRoot, consumerModel, SourceId);
        selectedCalls.Length.ShouldBe(2);
        selectedCalls.Select(call => consumerModel.GetEnclosingSymbol(call.SpanStart)!.GetDocumentationCommentId())
            .ShouldBe([OriginalConstructorId, "M:Fixture.Consumer.Read(System.String)"]);
        var (root, model) = await InspectAsync(fixture.Solution, "Callers.cs");
        var first = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "First");
        var caller = model.GetDeclaredSymbol(first).ShouldNotBeNull();
        caller.MethodKind.ShouldBe(MethodKind.Ordinary);
        caller.IsAsync.ShouldBeFalse();
        var binding = fixture.Request.Callers[0];
        var resolved = DocumentationCommentId.GetSymbolsForDeclarationId(binding.MethodDocumentationId, compilation)
            .ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(resolved, caller).ShouldBeTrue();
        caller.Parameters[binding.ParameterOrdinal].Type.GetDocumentationCommentId().ShouldBe("T:Fixture.IReader");
        var body = model.GetOperation(first.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        body.Operations.Length.ShouldBe(2);
        var variable = body.Operations[0].DescendantsAndSelf().OfType<IVariableDeclaratorOperation>().ShouldHaveSingleItem();
        var creation = variable.Initializer.ShouldNotBeNull().Value.ShouldBeAssignableTo<IObjectCreationOperation>()!;
        creation.Constructor!.GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        creation.Syntax.ToString().ShouldBe("new Consumer(Stamp(code), path: Trace(path))");
        SymbolEqualityComparer.Default.Equals(variable.Symbol.Type, compilation.GetTypeByMetadataName("Fixture.Consumer"))
            .ShouldBeTrue();
        IAnonymousFunctionOperation selector;
        if (form == "query")
            selector = AssertQuerySelector(model, first);
        else
        {
            var predicateQuery = first.DescendantNodes().OfType<QueryExpressionSyntax>().ShouldHaveSingleItem();
            predicateQuery.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>().ShouldBeEmpty();
            var translated = model.GetOperation(predicateQuery).ShouldBeAssignableTo<ITranslatedQueryOperation>()!;
            var functions = translated.DescendantsAndSelf().OfType<IAnonymousFunctionOperation>().ToArray();
            functions.Length.ShouldBe(2);
            functions.ShouldAllBe(function => function.IsImplicit);
            selector = functions.Where(function => function.Body.DescendantsAndSelf().OfType<ILocalReferenceOperation>()
                .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Local, variable.Symbol))).ShouldHaveSingleItem();
            selector.Symbol.ReturnType.SpecialType.ShouldBe(SpecialType.System_Boolean);
            selector.Symbol.Parameters.ShouldHaveSingleItem().Type.SpecialType.ShouldBe(SpecialType.System_Int32);
        }
        var read = selector.Body.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldHaveSingleItem();
        read.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Read(System.String)");
        var capture = read.Instance.ShouldBeAssignableTo<ILocalReferenceOperation>()!;
        SymbolEqualityComparer.Default.Equals(capture.Local, variable.Symbol).ShouldBeTrue(
            "the compiler-generated selector captures the exact constructed consumer local");
        var query = first.DescendantNodes().OfType<QueryExpressionSyntax>().ShouldHaveSingleItem();
        query.Parent.ShouldBeAssignableTo<ReturnStatementSyntax>();
        creation.Syntax.Span.End.ShouldBeLessThan(query.SpanStart);
        var second = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Second");
        var originalSecond = SyntaxFactory.ParseCompilationUnit(Callers).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Second");
        second.ToFullString().ShouldBe(originalSecond.ToFullString(), "the second synchronous caller must remain unchanged");

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "compiler-generated caller query capture must block dependency introduction");
        var diagnostic = "introduction.consumer:" + fixture.PathFor("Callers.cs") + ":" + creation.Syntax.SpanStart;
        plan.Sites.Count(site => site.Code == diagnostic).ShouldBe(1);
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "caller", diagnostic);
    }

    [Fact]
    public async Task prepares_eager_caller_query_source_without_retaining_consumer()
    {
        using var fixture = await CreateAsync(callerForm: "query-eager");
        await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (consumerRoot, consumerModel) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var selectedCalls = CallsBoundTo(consumerRoot, consumerModel, SourceId);
        selectedCalls.Length.ShouldBe(2);
        var (root, model) = await InspectAsync(fixture.Solution, "Callers.cs");
        var first = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "First");
        model.GetDeclaredSymbol(first).ShouldNotBeNull().IsAsync.ShouldBeFalse();
        var creation = first.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ShouldHaveSingleItem();
        model.GetOperation(creation).ShouldBeAssignableTo<IObjectCreationOperation>()!.Constructor!
            .GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        var declaration = creation.Ancestors().OfType<VariableDeclaratorSyntax>().First();
        var local = model.GetDeclaredSymbol(declaration).ShouldBeAssignableTo<ILocalSymbol>()!;
        local.Type.GetDocumentationCommentId().ShouldBe(ConsumerId);
        var query = first.DescendantNodes().OfType<QueryExpressionSyntax>().ShouldHaveSingleItem();
        query.Parent.ShouldBeAssignableTo<ReturnStatementSyntax>();
        var translated = model.GetOperation(query).ShouldBeAssignableTo<ITranslatedQueryOperation>()!;
        var selector = translated.DescendantsAndSelf().OfType<IAnonymousFunctionOperation>().ShouldHaveSingleItem();
        selector.IsImplicit.ShouldBeTrue();
        selector.Body.DescendantsAndSelf().OfType<ILocalReferenceOperation>().ShouldNotContain(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Local, local),
            "the deferred selector must not retain the consumer used to create its source");
        var source = model.GetOperation(query.FromClause.Expression).ShouldBeAssignableTo<IArrayCreationOperation>()!;
        var read = source.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldHaveSingleItem();
        read.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Read(System.String)");
        SymbolEqualityComparer.Default.Equals(read.Instance.ShouldBeAssignableTo<ILocalReferenceOperation>()!.Local, local)
            .ShouldBeTrue("the consumer call is evaluated eagerly in the initial array source");

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertDeferredPreparedAsync(fixture, plan, selectedCalls, expectedSelectedCalls: 2);
    }

    [Fact]
    public async Task refuses_compiler_generated_query_selector_capture()
    {
        using var fixture = await CreateAsync(deferredForm: "query", deferredExpression: "_initial");
        var (model, escape, selectedCalls) = await AssertDeferredConsumerAsync(fixture);
        var selector = AssertQuerySelector(model, escape);
        var capture = selector.Body.DescendantsAndSelf().OfType<IFieldReferenceOperation>().ShouldHaveSingleItem();
        capture.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._initial");
        var receiver = capture.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        receiver.IsImplicit.ShouldBeTrue();
        SymbolEqualityComparer.Default.Equals(receiver.Type,
            model.Compilation.GetTypeByMetadataName("Fixture.Consumer")).ShouldBeTrue(
            "the compiler-generated selector captures the selected consumer, not its range variable");

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "compiler-generated query capture must block dependency introduction");
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "query", "introduction.implicit-instance-capture:");
    }

    [Fact]
    public async Task refuses_query_capture_introduced_by_selected_rewrite()
    {
        using var fixture = await CreateAsync(deferredForm: "query", deferredExpression: "System.IO.File.ReadAllText(path)");
        var (model, escape, selectedCalls) = await AssertSelectedRewriteOriginalAsync(fixture);
        AssertSelectedQueryFunction(model, escape, selectedCalls[1], predicate: false);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "selected rewrite must not introduce deferred consumer capture");
        await AssertSelectedRewriteRefusalAsync(fixture, plan, selectedCalls, "query");
    }

    [Fact]
    public async Task refuses_query_predicate_capture_introduced_by_selected_rewrite()
    {
        using var fixture = await CreateAsync(deferredForm: "query predicate", deferredExpression: "System.IO.File.ReadAllText(path)");
        var (model, escape, selectedCalls) = await AssertSelectedRewriteOriginalAsync(fixture);
        AssertSelectedQueryFunction(model, escape, selectedCalls[1], predicate: true);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertSelectedRewriteRefusalAsync(fixture, plan, selectedCalls, "query");
    }

    [Fact]
    public async Task refuses_constructor_owned_query_capture_introduced_by_selected_rewrite()
    {
        using var fixture = await CreateAsync(deferredForm: "constructor query", deferredExpression: "System.IO.File.ReadAllText(path)");
        var (model, escape, selectedCalls) = await AssertSelectedRewriteOriginalAsync(fixture, constructorOwned: true);
        var constructor = selectedCalls[1].Ancestors().OfType<ConstructorDeclarationSyntax>().ShouldHaveSingleItem();
        AssertSelectedQueryFunction(model, constructor, selectedCalls[1], predicate: false);
        var assignment = model.GetOperation(constructor).ShouldNotBeNull().DescendantsAndSelf()
            .OfType<ISimpleAssignmentOperation>().Single(operation =>
                operation.Target is IFieldReferenceOperation field && field.Field.Name == "_deferred");
        var stored = assignment.Target.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field;
        stored.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._deferred");
        stored.Type.ToDisplayString().ShouldBe("System.Collections.Generic.IEnumerable<string>");
        assignment.Value.DescendantsAndSelf().OfType<ITranslatedQueryOperation>().ShouldHaveSingleItem();
        var exposed = model.GetOperation(escape.ExpressionBody!.Expression).ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        SymbolEqualityComparer.Default.Equals(exposed.Field, stored).ShouldBeTrue(
            "the synchronous wrapper exposes the exact query stored by the constructor");
        model.GetDeclaredSymbol(escape).ShouldNotBeNull().IsAsync.ShouldBeFalse();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertSelectedRewriteRefusalAsync(fixture, plan, selectedCalls, "query");
    }

    [Theory]
    [InlineData("async")]
    [InlineData("iterator")]
    public async Task refuses_state_machine_retention_introduced_by_selected_rewrite(string form)
    {
        using var fixture = await CreateAsync(deferredForm: form, deferredExpression: "System.IO.File.ReadAllText(path)");
        var (model, escape, selectedCalls) = await AssertSelectedRewriteOriginalAsync(fixture);
        model.GetDeclaredSymbol(escape).ShouldNotBeNull().IsAsync.ShouldBe(form == "async");
        var body = model.GetOperation(escape.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        body.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldBeEmpty(
            "the original state machine contains no consumer receiver to trigger original-only escape checks");
        body.Operations.Length.ShouldBe(form == "async" ? 2 : 1);
        if (form == "async")
        {
            var suspension = body.Operations[0].ShouldBeAssignableTo<IExpressionStatementOperation>()!.Operation
                .ShouldBeAssignableTo<IAwaitOperation>()!;
            suspension.Operation.ShouldBeAssignableTo<IInvocationOperation>()!.TargetMethod
                .GetDocumentationCommentId().ShouldBe("M:System.Threading.Tasks.Task.Yield");
            suspension.Syntax.Span.End.ShouldBeLessThan(selectedCalls[1].SpanStart);
        }
        var returned = body.Operations.Last().ShouldBeAssignableTo<IReturnOperation>()!;
        returned.Kind.ShouldBe(form == "async" ? OperationKind.Return : OperationKind.YieldReturn);
        var selected = returned.ReturnedValue.ShouldBeAssignableTo<IInvocationOperation>()!;
        selected.Syntax.Span.ShouldBe(selectedCalls[1].Span);
        selected.TargetMethod.GetDocumentationCommentId().ShouldBe(SourceId);
        OperationAncestors(selected).Where(operation =>
            operation is IAnonymousFunctionOperation or ILocalFunctionOperation).ShouldBeEmpty();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertSelectedRewriteRefusalAsync(fixture, plan, selectedCalls, form);
    }

    [Fact]
    public async Task prepares_eager_consumer_query_source_without_introducing_deferred_capture()
    {
        using var fixture = await CreateAsync(deferredForm: "eager query", deferredExpression: "System.IO.File.ReadAllText(path)");
        var (model, escape, selectedCalls) = await AssertSelectedRewriteOriginalAsync(fixture);
        var query = escape.DescendantNodes().OfType<QueryExpressionSyntax>().ShouldHaveSingleItem();
        var source = model.GetOperation(query.FromClause.Expression).ShouldBeAssignableTo<IArrayCreationOperation>()!;
        var eager = source.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldHaveSingleItem();
        eager.Syntax.Span.ShouldBe(selectedCalls[1].Span);
        eager.TargetMethod.GetDocumentationCommentId().ShouldBe(SourceId);
        OperationAncestors(eager).Where(operation =>
            operation is IAnonymousFunctionOperation or ILocalFunctionOperation).ShouldBeEmpty(
            "the initial source executes before the enumerable is returned");
        var selector = AssertQuerySelector(model, escape, SpecialType.System_String);
        selector.Body.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldBeEmpty();
        selector.Body.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldBeEmpty();
        var value = selector.Body.DescendantsAndSelf().OfType<IParameterReferenceOperation>().ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(value.Parameter, selector.Symbol.Parameters[0]).ShouldBeTrue();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertDeferredPreparedAsync(fixture, plan, selectedCalls, expectedSelectedCalls: 3);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var (candidateRoot, candidateModel) = await InspectAsync(candidate, "Consumer.cs");
        var mapped = CallsBoundTo(candidateRoot, candidateModel, TargetId);
        mapped.Length.ShouldBe(3);
        CallsBoundTo(candidateRoot, candidateModel, SourceId).ShouldBeEmpty();
        var target = DocumentationCommentId.GetSymbolsForDeclarationId(TargetId, candidateModel.Compilation).ShouldHaveSingleItem();
        var field = candidateModel.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull()
            .GetMembers("_reader").OfType<IFieldSymbol>().ShouldHaveSingleItem();
        field.GetDocumentationCommentId().ShouldBe(FieldId);
        foreach (var (before, after) in selectedCalls.Zip(mapped))
        {
            var original = model.GetOperation(before).ShouldBeAssignableTo<IInvocationOperation>()!;
            var replacement = candidateModel.GetOperation(after).ShouldBeAssignableTo<IInvocationOperation>()!;
            SymbolEqualityComparer.Default.Equals(replacement.TargetMethod, target).ShouldBeTrue();
            replacement.TargetMethod.ContainingAssembly.Identity.ToString().ShouldBe(fixture.Request.Mapping.Target.AssemblyIdentity);
            after.ArgumentList.ToString().ShouldBe(before.ArgumentList.ToString());
            replacement.Arguments.Select(ArgumentBinding).ShouldBe(original.Arguments.Select(ArgumentBinding));
            var receiver = replacement.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
            SymbolEqualityComparer.Default.Equals(receiver.Field, field).ShouldBeTrue();
            receiver.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!.Type!.GetDocumentationCommentId().ShouldBe(ConsumerId);
            OperationAncestors(replacement).Where(operation =>
                operation is IAnonymousFunctionOperation or ILocalFunctionOperation).ShouldBeEmpty();
        }
        var candidateEscape = candidateRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Escape");
        var candidateSelector = AssertQuerySelector(candidateModel, candidateEscape, SpecialType.System_String);
        candidateSelector.Body.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldBeEmpty(
            "the candidate selector must remain independent of the consumer");
        candidateSelector.Body.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldBeEmpty();
        var candidateQuery = candidateEscape.DescendantNodes().OfType<QueryExpressionSyntax>().ShouldHaveSingleItem();
        var candidateSource = candidateModel.GetOperation(candidateQuery.FromClause.Expression).ShouldBeAssignableTo<IArrayCreationOperation>()!;
        candidateSource.DescendantsAndSelf().OfType<IInvocationOperation>().ShouldHaveSingleItem().Syntax.Span.ShouldBe(mapped[1].Span);
    }

    [Fact]
    public async Task refuses_iterator_property_retention_reached_through_ordinary_wrapper()
    {
        using var fixture = await CreateAsync(deferredForm: "iterator property");
        await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var selectedCalls = CallsBoundTo(root, model, SourceId);
        selectedCalls.Length.ShouldBe(2);
        var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().ShouldHaveSingleItem();
        var getter = property.AccessorList!.Accessors.ShouldHaveSingleItem();
        var getterSymbol = model.GetDeclaredSymbol(getter).ShouldNotBeNull();
        getterSymbol.MethodKind.ShouldBe(MethodKind.PropertyGet);
        var yielded = model.GetOperation(getter).ShouldNotBeNull().DescendantsAndSelf()
            .OfType<IReturnOperation>().Single(operation => operation.Kind == OperationKind.YieldReturn);
        var capture = yielded.ReturnedValue.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        capture.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._initial");
        var receiver = capture.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        SymbolEqualityComparer.Default.Equals(receiver.Type, getterSymbol.ContainingType).ShouldBeTrue();
        receiver.Type!.GetDocumentationCommentId().ShouldBe(ConsumerId);
        var wrapper = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "GetEscape");
        var wrapperSymbol = model.GetDeclaredSymbol(wrapper).ShouldNotBeNull();
        wrapperSymbol.MethodKind.ShouldBe(MethodKind.Ordinary);
        wrapperSymbol.IsAsync.ShouldBeFalse();
        var wrapperOperation = model.GetOperation(wrapper).ShouldNotBeNull();
        wrapperOperation.DescendantsAndSelf().ShouldNotContain(operation => operation.Kind == OperationKind.YieldReturn);
        var access = wrapperOperation.DescendantsAndSelf().OfType<IPropertyReferenceOperation>().ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(access.Property.GetMethod, getterSymbol).ShouldBeTrue();
        var (callerRoot, callerModel) = await InspectAsync(fixture.Solution, "Callers.cs");
        var call = CallsBoundTo(callerRoot, callerModel, "M:Fixture.Consumer.GetEscape(System.String)").ShouldHaveSingleItem();
        call.Parent.ShouldBeAssignableTo<ReturnStatementSyntax>();
        var invocation = callerModel.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
        invocation.Instance.ShouldBeAssignableTo<ILocalReferenceOperation>()!.Local.Type
            .GetDocumentationCommentId().ShouldBe(ConsumerId);
        callerModel.GetDeclaredSymbol(call.Ancestors().OfType<MethodDeclarationSyntax>().First())
            .ShouldNotBeNull().IsAsync.ShouldBeFalse();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        // Existing accessor scanning already refuses this; preserve it as a characterization.
        await AssertDeferredRefusalAsync(fixture, plan, selectedCalls, "iterator", "introduction.state-machine-capture:");
    }

    [Theory]
    [InlineData("query")]
    [InlineData("async")]
    [InlineData("iterator")]
    public async Task prepares_deferred_members_without_selected_instance_operations(string form)
    {
        using var fixture = await CreateAsync(deferredForm: form, deferredExpression: "string.Empty");
        var (model, escape, selectedCalls) = await AssertDeferredConsumerAsync(fixture);
        var operation = model.GetOperation(escape).ShouldNotBeNull();
        operation.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldNotContain(receiver =>
            receiver.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance
            && SymbolEqualityComparer.Default.Equals(receiver.Type, model.Compilation.GetTypeByMetadataName("Fixture.Consumer")));
        var field = operation.DescendantsAndSelf().OfType<IFieldReferenceOperation>().ShouldHaveSingleItem();
        field.Field.GetDocumentationCommentId().ShouldBe("F:System.String.Empty");
        field.Field.IsStatic.ShouldBeTrue();
        field.Instance.ShouldBeNull();
        if (form == "query")
            AssertQuerySelector(model, escape).Body.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldBeEmpty();
        else if (form == "async")
        {
            model.GetDeclaredSymbol(escape).ShouldNotBeNull().IsAsync.ShouldBeTrue();
            operation.DescendantsAndSelf().OfType<IAwaitOperation>().ShouldHaveSingleItem();
        }
        else
            operation.DescendantsAndSelf().Count(value => value.Kind == OperationKind.YieldReturn).ShouldBe(1);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        await AssertDeferredPreparedAsync(fixture, plan, selectedCalls, expectedSelectedCalls: 2);
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

        await AssertDeferredPreparedAsync(fixture, plan, selectedCalls, expectedSelectedCalls: 2);
    }

    private static async Task AssertDeferredPreparedAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        InvocationExpressionSyntax[] selectedCalls, int expectedSelectedCalls)
    {
        selectedCalls.Length.ShouldBe(expectedSelectedCalls);
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        plan.Changes.OriginalSolution.ShouldBeSameAs(fixture.Solution);
        plan.Changes.HasChanges.ShouldBeTrue();
        AssertNoFileEffects(plan.Changes);
        plan.Sites.Count.ShouldBe(expectedSelectedCalls + 1);
        plan.Sites.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(expectedSelectedCalls + 1);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
        var converted = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate).ToArray();
        converted.Length.ShouldBe(expectedSelectedCalls);
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
        await AssertClosureAsync(fixture, plan, selectedCalls, compilation, expectedSelectedCalls);
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
    [InlineData("T:Fixture.IReader")]
    [InlineData("T:Fixture.Callers")]
    [InlineData("T:Fixture.Unknown")]
    public async Task refuses_invalid_consumer_type_before_selecting_source_sites(string typeId)
    {
        using var fixture = await CreateAsync();
        var compilation = await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var types = DocumentationCommentId.GetSymbolsForDeclarationId(typeId, compilation).OfType<INamedTypeSymbol>().ToArray();
        if (typeId == "T:Fixture.Unknown")
            types.ShouldBeEmpty();
        else if (typeId == "T:Fixture.IReader")
            types.ShouldHaveSingleItem().TypeKind.ShouldBe(TypeKind.Interface);
        else
            types.ShouldHaveSingleItem().IsStatic.ShouldBeTrue();
        var request = fixture.Request with { Selection = fixture.Request.Selection with { TypeDocumentationId = typeId } };

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, request);

        AssertBlockedWithoutCandidate(plan, fixture.Solution);
        AssertSelectionBlocker(plan.Sites, request.Selection, "introduction.type");
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task refuses_original_acquisition_authority_drift_even_when_replacement_is_complete()
    {
        using var fixture = await CreateAsync();
        var original = fixture.Solution;
        await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var receipt = fixture.Receipt.ShouldNotBeNull();
        var acquired = await StaticDependencyPreparation.AcquireAsync(original, fixture.Request.Selection,
            fixture.Request.ExpectedSourceFingerprint, CancellationToken.None);
        acquired.Blockers.ShouldBeEmpty();
        var preparation = acquired.Preparation.ShouldNotBeNull();
        preparation.Original.ShouldBeSameAs(original);
        var replacement = new WorkspaceAcquisitionReceipt(original, receipt.Manifest, receipt.Outcomes, receipt.Provenance);
        replacement.ShouldNotBeSameAs(receipt);
        replacement.Solution.ShouldBeSameAs(receipt.Solution);
        replacement.Manifest.ShouldBeSameAs(receipt.Manifest);
        replacement.Outcomes.ShouldBe(receipt.Outcomes);
        replacement.Provenance.ShouldBeSameAs(receipt.Provenance);
        WorkspaceAcquisitionRegistry.Attach(fixture.Workspace, replacement);
        try
        {
            await AssertCompleteAsync(original);

            var blockers = await preparation.VerifyAsync(original, CancellationToken.None);

            AssertSelectionBlocker(blockers, fixture.Request.Selection, "introduction.original-drift");
            preparation.Original.ShouldBeSameAs(original);
            preparation.Fingerprint.ShouldBe(fixture.Request.ExpectedSourceFingerprint);
            // The deliberate receipt replacement is the only input change; verification must leave it intact.
            await (fixture with { Receipt = replacement }).AssertUntouchedAsync();
        }
        finally
        {
            WorkspaceAcquisitionRegistry.Attach(fixture.Workspace, receipt);
        }
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task refuses_ambiguous_project_path_in_complete_multiple_context_acquisition()
    {
        using var fixture = await CreateAsync();
        using var workspace = CreateWorkspace(fixture.Root, fixture.Sources);
        var first = workspace.CurrentSolution.Projects.Single();
        var secondId = ProjectId.CreateNewId();
        workspace.AddProject(ProjectInfo.Create(secondId, VersionStamp.Create(), "AlternateContext", "AlternateContext",
            LanguageNames.CSharp, filePath: first.FilePath, compilationOptions: first.CompilationOptions,
            parseOptions: first.ParseOptions, metadataReferences: first.MetadataReferences));
        foreach (var (name, source) in fixture.Sources)
            workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(secondId), name,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())),
                filePath: fixture.PathFor(name)));
        var original = workspace.CurrentSolution;
        var projects = original.Projects.ToArray();
        projects.Length.ShouldBe(2);
        projects.Select(project => project.FilePath).Distinct(StringComparer.Ordinal).ShouldHaveSingleItem()
            .ShouldBe(fixture.Request.Selection.ProjectPath);
        foreach (var project in projects)
        {
            var compilation = (await project.GetCompilationAsync()).ShouldNotBeNull();
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        }
        var fingerprint = await SolutionRevision.ComputeAsync(original);
        var receipt = AttachMultipleContextAcquisition(original, fixture.Receipt.ShouldNotBeNull(), fingerprint);
        receipt.Manifest.Inputs.Where(input => input.Kind == SemanticInputKind.Project).ShouldHaveSingleItem();
        var contexts = receipt.Manifest.Inputs.Where(input => input.Kind == SemanticInputKind.CompilationContext).ToArray();
        contexts.Length.ShouldBe(2);
        contexts.Select(input => input.Context).Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
        contexts.Select(input => input.Context).Order(StringComparer.Ordinal)
            .ShouldBe(projects.Select(WorkspaceCompleteness.CompilationContextIdentity).Order(StringComparer.Ordinal));
        foreach (var context in contexts)
        {
            receipt.Manifest.Inputs.Where(input => input.Kind == SemanticInputKind.SourceDocument && input.Context == context.Context)
                .Select(input => input.Path).Order(StringComparer.Ordinal)
                .ShouldBe(fixture.Sources.Keys.Select(fixture.PathFor).Order(StringComparer.Ordinal));
            receipt.Manifest.Inputs.Count(input => input.Kind == SemanticInputKind.MetadataReference && input.Context == context.Context)
                .ShouldBe(first.MetadataReferences.Count);
        }
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(original, CancellationToken.None);
        completeness.State.ShouldBe(WorkspaceCompletenessState.Complete, string.Join("; ", completeness.Diagnostics));
        WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence.ShouldBeTrue();
        completeness.RequestedProjects.ShouldBe([fixture.Request.Selection.ProjectPath]);
        var request = fixture.Request with { ExpectedSourceFingerprint = fingerprint };

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(original, request);

        AssertBlockedWithoutCandidate(plan, original);
        AssertSelectionBlocker(plan.Sites, request.Selection, "introduction.project");
        workspace.CurrentSolution.ShouldBeSameAs(original);
        WorkspaceAcquisitionRegistry.Get(workspace).Receipt.ShouldBeSameAs(receipt);
        (await SolutionRevision.ComputeAsync(original)).ShouldBe(fingerprint);
        await fixture.AssertUntouchedAsync();
    }

    private static WorkspaceAcquisitionReceipt AttachMultipleContextAcquisition(Solution solution,
        WorkspaceAcquisitionReceipt template, string fingerprint)
    {
        // Reuse the controlled fixture's inputs, but declare its shared physical project once.
        // Each Roslyn context gets distinct source/reference declarations, as in the real collector.
        var inputs = new List<ExpectedSemanticInput>();
        var outcomes = new List<SemanticInputOutcome>();
        foreach (var input in template.Manifest.Inputs)
        {
            var outcome = template.Outcomes.Single(value => value.InputId == input.Id);
            if (input.Kind is SemanticInputKind.SourceSnapshot or SemanticInputKind.WorkspaceInputSet)
            {
                inputs.Add(input with { Fingerprint = fingerprint });
                outcomes.Add(outcome with { ObservedFingerprint = fingerprint });
            }
            else if (input.ProjectPath is null || input.Kind == SemanticInputKind.Project)
            {
                inputs.Add(input);
                outcomes.Add(outcome);
            }
            else
            {
                foreach (var project in solution.Projects)
                {
                    var id = input.Id + ":" + project.Name;
                    inputs.Add(input with { Id = id, Context = WorkspaceCompleteness.CompilationContextIdentity(project) });
                    outcomes.Add(outcome with { InputId = id });
                }
            }
        }
        var manifest = new SemanticInputManifest(solution.FilePath, inputs, ManifestDiscoveryState.Closed);
        var provenance = template.Provenance with
        {
            SourceFingerprint = fingerprint,
            Configuration = "Two controlled in-memory contexts sharing one physical project"
        };
        var receipt = new WorkspaceAcquisitionReceipt(solution, manifest, outcomes, provenance);
        WorkspaceAcquisitionRegistry.Attach(solution.Workspace, receipt);
        return receipt;
    }

    private static void AssertSelectionBlocker(IReadOnlyList<StaticDependencySite> sites,
        StaticDependencySelection selection, string code)
    {
        var blocker = sites.ShouldHaveSingleItem("selection failure must not invent original selected source sites");
        blocker.Code.ShouldBe(code);
        blocker.Disposition.ShouldBe(StaticDependencySiteDisposition.Blocked);
        blocker.ProjectPath.ShouldBe(selection.ProjectPath);
        blocker.TypeDocumentationId.ShouldBe(selection.TypeDocumentationId);
        blocker.Path.ShouldBe(selection.ProjectPath);
        blocker.SpanStart.ShouldBe(0);
        blocker.SpanLength.ShouldBe(0);
        blocker.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("missing-caller-binding")]
    [InlineData("multiple-constructors")]
    [InlineData("public-consumer")]
    [InlineData("escaping-consumer")]
    [InlineData("missing-acquisition")]
    [InlineData("stale-acquisition")]
    [InlineData("stale-fingerprint")]
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

    private static async Task<(SemanticModel Model, ClassDeclarationSyntax Consumer, InvocationExpressionSyntax[] SelectedCalls)>
        AssertOrdinarySelectedSourcesAsync(Fixture fixture)
    {
        await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var consumer = SelectedConsumerDeclaration(root, model);
        var selectedCalls = CallsBoundTo(root, model, SourceId);
        selectedCalls.Length.ShouldBe(2, "only the original constructor and Read may invoke the selected BCL overload");
        selectedCalls.Select(call => model.GetEnclosingSymbol(call.SpanStart)!.GetDocumentationCommentId())
            .ShouldBe(OrdinarySelectedCallOwners);
        var source = DocumentationCommentId.GetSymbolsForDeclarationId(SourceId, model.Compilation)
            .ShouldHaveSingleItem().ShouldBeAssignableTo<IMethodSymbol>()!;
        source.IsStatic.ShouldBeTrue();
        source.Locations.ShouldAllBe(location => location.IsInMetadata);
        source.ContainingAssembly.Identity.ToString().ShouldBe(fixture.Request.Mapping.Source.AssemblyIdentity);
        foreach (var call in selectedCalls)
        {
            var invocation = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
            SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, source).ShouldBeTrue();
            invocation.Instance.ShouldBeNull();
            var argument = invocation.Arguments.ShouldHaveSingleItem();
            SymbolEqualityComparer.Default.Equals(argument.Parameter, source.Parameters.ShouldHaveSingleItem()).ShouldBeTrue();
            argument.ArgumentKind.ShouldBe(ArgumentKind.Explicit);
            argument.InConversion.IsIdentity.ShouldBeTrue();
            argument.OutConversion.IsIdentity.ShouldBeTrue();
            OperationAncestors(invocation).Where(operation =>
                operation is IAnonymousFunctionOperation or ILocalFunctionOperation).ShouldBeEmpty();
            SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(call.Ancestors().OfType<ClassDeclarationSyntax>().First()),
                model.GetDeclaredSymbol(consumer)).ShouldBeTrue();
        }
        return (model, consumer, selectedCalls);
    }

    private static async Task<(SemanticModel Model, MethodDeclarationSyntax Escape, InvocationExpressionSyntax[] SelectedCalls)>
        AssertBaseEscapeAsync(Fixture fixture)
    {
        var (model, consumer, selectedCalls) = await AssertOrdinarySelectedSourcesAsync(fixture);
        var escape = consumer.Members.OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Escape");
        var symbol = model.GetDeclaredSymbol(escape).ShouldNotBeNull();
        symbol.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Escape(System.String)");
        symbol.MethodKind.ShouldBe(MethodKind.Ordinary);
        symbol.IsStatic.ShouldBeFalse();
        symbol.IsVirtual.ShouldBeFalse();
        symbol.IsOverride.ShouldBeFalse();
        symbol.IsAbstract.ShouldBeFalse();
        var selectedType = model.Compilation.GetTypeByMetadataName("Fixture.Consumer").ShouldNotBeNull();
        SymbolEqualityComparer.Default.Equals(symbol.ContainingType, selectedType).ShouldBeTrue();
        selectedType.GetMembers().OfType<IMethodSymbol>().ShouldNotContain(method => method.MethodKind == MethodKind.Destructor);
        await AssertOrdinaryConsumerCallerAsync(fixture, symbol);
        return (model, escape, selectedCalls);
    }

    private static void AssertObjectMethod(SemanticModel model, IMethodSymbol method, string documentationId)
    {
        var expected = DocumentationCommentId.GetSymbolsForDeclarationId(documentationId, model.Compilation).ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(method, expected).ShouldBeTrue();
        method.IsStatic.ShouldBeFalse();
        method.ContainingType.SpecialType.ShouldBe(SpecialType.System_Object);
        method.Locations.ShouldAllBe(location => location.IsInMetadata);
    }

    private static void AssertBaseReceiver(SemanticModel model, IOperation? instance, INamedTypeSymbol owner)
    {
        var receiver = instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!;
        receiver.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
        receiver.IsImplicit.ShouldBeFalse();
        receiver.Type!.SpecialType.ShouldBe(SpecialType.System_Object);
        SymbolEqualityComparer.Default.Equals(receiver.Type, model.Compilation.GetSpecialType(SpecialType.System_Object)).ShouldBeTrue();
        var syntax = receiver.Syntax.ShouldBeAssignableTo<BaseExpressionSyntax>()!;
        SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(syntax.SpanStart)!.ContainingType, owner).ShouldBeTrue(
            "the enclosing semantic owner, not the Object static type, identifies whose base receiver is captured");
        var declaration = syntax.Ancestors().OfType<ClassDeclarationSyntax>().First();
        SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(declaration), owner).ShouldBeTrue();
    }

    private static ClassDeclarationSyntax SelectedConsumerDeclaration(SyntaxNode root, SemanticModel model)
    {
        var selected = DocumentationCommentId.GetSymbolsForDeclarationId(ConsumerId, model.Compilation)
            .ShouldHaveSingleItem().ShouldBeAssignableTo<INamedTypeSymbol>()!;
        SymbolEqualityComparer.Default.Equals(selected, model.Compilation.GetTypeByMetadataName("Fixture.Consumer")).ShouldBeTrue();
        return root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(declaration => SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(declaration), selected))
            .ShouldHaveSingleItem();
    }

    private static async Task<(SemanticModel Model, MethodDeclarationSyntax Escape, InvocationExpressionSyntax[] SelectedCalls)>
        AssertSelectedRewriteOriginalAsync(Fixture fixture, bool constructorOwned = false)
    {
        var compilation = await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var calls = CallsBoundTo(root, model, SourceId);
        calls.Length.ShouldBe(3, "the two ordinary selected calls and one additional exact source invocation are required");
        calls.Select(call => call.ToString()).ShouldBe(SelectedRewriteOriginalCallTexts);
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ShouldHaveSingleItem();
        var escape = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Escape");
        var read = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Read");
        // Query calls can have implicit lambda enclosing symbols; establish the source owners independently.
        calls.Select(call => call.Ancestors().First(node => node is BaseMethodDeclarationSyntax).Span)
            .ShouldBe(new[] { constructor.Span, constructorOwned ? constructor.Span : escape.Span, read.Span });
        model.GetDeclaredSymbol(constructor).ShouldNotBeNull().GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        var escapeSymbol = model.GetDeclaredSymbol(escape).ShouldNotBeNull();
        escapeSymbol.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Escape(System.String)");
        escapeSymbol.MethodKind.ShouldBe(MethodKind.Ordinary);
        escapeSymbol.IsStatic.ShouldBeFalse();
        var source = DocumentationCommentId.GetSymbolsForDeclarationId(SourceId, compilation)
            .ShouldHaveSingleItem().ShouldBeAssignableTo<IMethodSymbol>()!;
        source.IsStatic.ShouldBeTrue();
        source.Locations.ShouldAllBe(location => location.IsInMetadata);
        source.ContainingAssembly.Identity.ToString().ShouldBe(fixture.Request.Mapping.Source.AssemblyIdentity);
        var sourceParameter = source.Parameters.ShouldHaveSingleItem();
        sourceParameter.Name.ShouldBe("path");
        sourceParameter.Type.SpecialType.ShouldBe(SpecialType.System_String);
        foreach (var call in calls)
        {
            var invocation = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
            SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, source).ShouldBeTrue();
            invocation.Instance.ShouldBeNull();
            var argument = invocation.Arguments.ShouldHaveSingleItem();
            SymbolEqualityComparer.Default.Equals(argument.Parameter, sourceParameter).ShouldBeTrue();
            argument.ArgumentKind.ShouldBe(ArgumentKind.Explicit);
            argument.InConversion.IsIdentity.ShouldBeTrue();
            argument.OutConversion.IsIdentity.ShouldBeTrue();
        }
        var added = model.GetOperation(calls[1]).ShouldBeAssignableTo<IInvocationOperation>()!;
        var passed = added.Arguments[0].Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter;
        var owner = constructorOwned ? model.GetDeclaredSymbol(constructor).ShouldNotBeNull() : escapeSymbol;
        var path = owner.Parameters.Single(parameter => parameter.Name == "path");
        SymbolEqualityComparer.Default.Equals(passed, path).ShouldBeTrue(
            "the selected invocation must pass the exact original source owner's path parameter");
        await AssertOrdinaryConsumerCallerAsync(fixture, escapeSymbol);
        return (model, escape, calls);
    }

    private static async Task AssertOrdinaryConsumerCallerAsync(Fixture fixture, IMethodSymbol target)
    {
        var (root, model) = await InspectAsync(fixture.Solution, "Callers.cs");
        root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Count().ShouldBe(2);
        fixture.Request.Callers.Count.ShouldBe(2);
        foreach (var binding in fixture.Request.Callers)
        {
            var method = DocumentationCommentId.GetSymbolsForDeclarationId(binding.MethodDocumentationId, model.Compilation)
                .ShouldHaveSingleItem().ShouldBeAssignableTo<IMethodSymbol>()!;
            method.MethodKind.ShouldBe(MethodKind.Ordinary);
            method.IsAsync.ShouldBeFalse();
            SymbolEqualityComparer.Default.Equals(method.Parameters[binding.ParameterOrdinal].Type,
                model.Compilation.GetTypeByMetadataName("Fixture.IReader")).ShouldBeTrue();
        }
        var first = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "First");
        var caller = model.GetDeclaredSymbol(first).ShouldNotBeNull();
        caller.IsAsync.ShouldBeFalse();
        caller.MethodKind.ShouldBe(MethodKind.Ordinary);
        caller.GetDocumentationCommentId().ShouldBe(fixture.Request.Callers[0].MethodDocumentationId);
        var body = model.GetOperation(first.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        body.Operations.Length.ShouldBe(2);
        body.DescendantsAndSelf().Where(operation => operation is IAwaitOperation
            or IAnonymousFunctionOperation or ILocalFunctionOperation || operation.Kind == OperationKind.YieldReturn).ShouldBeEmpty();
        var variable = body.Operations[0].DescendantsAndSelf().OfType<IVariableDeclaratorOperation>().ShouldHaveSingleItem();
        var creation = variable.Initializer.ShouldNotBeNull().Value.ShouldBeAssignableTo<IObjectCreationOperation>()!;
        creation.Syntax.ToString().ShouldBe("new Consumer(Stamp(code), path: Trace(path))");
        creation.Constructor!.GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        variable.Symbol.Type.GetDocumentationCommentId().ShouldBe(ConsumerId);
        var returned = body.Operations[1].ShouldBeAssignableTo<IReturnOperation>()!;
        returned.Kind.ShouldBe(OperationKind.Return);
        var invocation = returned.ReturnedValue.ShouldBeAssignableTo<IInvocationOperation>()!;
        invocation.Syntax.ToString().ShouldBe("consumer." + target.Name + "(path)");
        SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, target).ShouldBeTrue();
        SymbolEqualityComparer.Default.Equals(caller.ReturnType, target.ReturnType).ShouldBeTrue();
        var receiver = invocation.Instance.ShouldBeAssignableTo<ILocalReferenceOperation>()!;
        SymbolEqualityComparer.Default.Equals(receiver.Local, variable.Symbol).ShouldBeTrue();
        var argument = invocation.Arguments.ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(argument.Parameter, target.Parameters[0]).ShouldBeTrue();
        SymbolEqualityComparer.Default.Equals(argument.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter,
            caller.Parameters[2]).ShouldBeTrue();
        var second = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Second");
        var originalSecond = SyntaxFactory.ParseCompilationUnit(Callers).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Second");
        second.ToFullString().ShouldBe(originalSecond.ToFullString(), "the second synchronous caller must remain unchanged");
        model.GetDeclaredSymbol(second).ShouldNotBeNull().IsAsync.ShouldBeFalse();
        var secondCreation = second.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ShouldHaveSingleItem();
        model.GetOperation(secondCreation).ShouldBeAssignableTo<IObjectCreationOperation>()!.Constructor!
            .GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
    }

    private static void AssertSelectedQueryFunction(SemanticModel model, SyntaxNode owner,
        InvocationExpressionSyntax selectedCall, bool predicate)
    {
        var query = owner.DescendantNodes().OfType<QueryExpressionSyntax>().ShouldHaveSingleItem();
        query.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>().ShouldBeEmpty();
        var translated = model.GetOperation(query).ShouldBeAssignableTo<ITranslatedQueryOperation>()!;
        var select = translated.Operation.ShouldBeAssignableTo<IInvocationOperation>()!;
        select.TargetMethod.Name.ShouldBe("Select");
        select.TargetMethod.ContainingType.ToDisplayString().ShouldBe("System.Linq.Enumerable");
        var functions = translated.DescendantsAndSelf().OfType<IAnonymousFunctionOperation>().ToArray();
        functions.Length.ShouldBe(predicate ? 2 : 1);
        functions.ShouldAllBe(function => function.IsImplicit);
        translated.DescendantsAndSelf().OfType<IInstanceReferenceOperation>().ShouldBeEmpty(
            "the original query must contain no consumer-instance receiver");
        var selected = translated.DescendantsAndSelf().OfType<IInvocationOperation>()
            .Single(operation => operation.Syntax.Span == selectedCall.Span);
        selected.TargetMethod.GetDocumentationCommentId().ShouldBe(SourceId);
        selected.Instance.ShouldBeNull();
        var function = OperationAncestors(selected).OfType<IAnonymousFunctionOperation>().ShouldHaveSingleItem();
        function.Symbol.Parameters.ShouldHaveSingleItem().Type.SpecialType.ShouldBe(SpecialType.System_Int32);
        function.Symbol.ReturnType.SpecialType.ShouldBe(predicate ? SpecialType.System_Boolean : SpecialType.System_String);
        if (predicate)
        {
            var where = translated.DescendantsAndSelf().OfType<IInvocationOperation>()
                .Single(operation => operation.TargetMethod.Name == "Where");
            where.TargetMethod.ContainingType.ToDisplayString().ShouldBe("System.Linq.Enumerable");
            var clause = query.Body.Clauses.OfType<WhereClauseSyntax>().ShouldHaveSingleItem();
            clause.Condition.ToString().ShouldBe("System.IO.File.ReadAllText(path).Length > 0");
            query.Body.SelectOrGroup.ToString().ShouldBe("select string.Empty");
            clause.Span.Contains(selectedCall.Span).ShouldBeTrue();
        }
        else
            query.Body.SelectOrGroup.ShouldBeAssignableTo<SelectClauseSyntax>()!.Expression.Span.ShouldBe(selectedCall.Span);
    }

    private static IEnumerable<IOperation> OperationAncestors(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
            yield return parent;
    }

    private static async Task AssertSelectedRewriteRefusalAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        InvocationExpressionSyntax[] selectedCalls, string context)
    {
        selectedCalls.Length.ShouldBe(3);
        AssertBlockedWithoutCandidate(plan, fixture.Solution);
        await AssertAtomicSiteDispositionsAsync(fixture, plan, selectedCalls);
        var offending = selectedCalls[1];
        var diagnostic = plan.Sites.Where(site => site.Path == fixture.PathFor("Consumer.cs")
            && site.SpanStart == offending.SpanStart && site.SpanLength == offending.Span.Length).ShouldHaveSingleItem();
        var message = diagnostic.Message.ToLowerInvariant();
        (message.Contains(context, StringComparison.Ordinal)
            || message.Contains(context == "query" ? "deferred" : "state machine", StringComparison.Ordinal)
            || message.Contains(context == "query" ? "function" : "state-machine", StringComparison.Ordinal))
            .ShouldBeTrue("the offending selected site must identify its deferred or state-machine execution context");
        (message.Contains("consumer", StringComparison.Ordinal) || message.Contains("instance", StringComparison.Ordinal)
            || message.Contains("receiver", StringComparison.Ordinal)).ShouldBeTrue();
        (message.Contains("captur", StringComparison.Ordinal) || message.Contains("retain", StringComparison.Ordinal)
            || message.Contains("lifetime", StringComparison.Ordinal) || message.Contains("escap", StringComparison.Ordinal))
            .ShouldBeTrue("the contextual diagnostic must explain why introducing the receiver is unsafe");
        await fixture.AssertUntouchedAsync();
        await AssertCompleteAsync(fixture.Solution);
    }

    private static IAnonymousFunctionOperation AssertQuerySelector(SemanticModel model, MethodDeclarationSyntax escape,
        SpecialType expectedRangeType = SpecialType.System_Int32)
    {
        var query = escape.DescendantNodes().OfType<QueryExpressionSyntax>().ShouldHaveSingleItem();
        query.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>().ShouldBeEmpty();
        var translated = model.GetOperation(query).ShouldBeAssignableTo<ITranslatedQueryOperation>()!;
        var select = translated.Operation.ShouldBeAssignableTo<IInvocationOperation>()!;
        select.TargetMethod.Name.ShouldBe("Select");
        select.TargetMethod.ContainingType.ToDisplayString().ShouldBe("System.Linq.Enumerable");
        var selector = translated.Operation.DescendantsAndSelf().OfType<IAnonymousFunctionOperation>().ShouldHaveSingleItem();
        selector.IsImplicit.ShouldBeTrue("the selector is compiler-generated rather than an explicit source lambda");
        selector.Symbol.Parameters.ShouldHaveSingleItem().Type.SpecialType.ShouldBe(expectedRangeType);
        return selector;
    }

    private static async Task<InvocationExpressionSyntax[]> AssertCallerRetentionAsync(Fixture fixture, bool isAsync)
    {
        var compilation = await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (consumerRoot, consumerModel) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var selectedCalls = CallsBoundTo(consumerRoot, consumerModel, SourceId);
        selectedCalls.Length.ShouldBe(2);
        selectedCalls.Select(call => consumerModel.GetEnclosingSymbol(call.SpanStart)!.GetDocumentationCommentId())
            .ShouldBe([OriginalConstructorId, "M:Fixture.Consumer.Read(System.String)"]);
        var (root, model) = await InspectAsync(fixture.Solution, "Callers.cs");
        var first = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "First");
        var caller = model.GetDeclaredSymbol(first).ShouldNotBeNull();
        caller.IsAsync.ShouldBe(isAsync);
        caller.MethodKind.ShouldBe(MethodKind.Ordinary);
        var binding = fixture.Request.Callers[0];
        var resolved = DocumentationCommentId.GetSymbolsForDeclarationId(binding.MethodDocumentationId, compilation)
            .ShouldHaveSingleItem();
        SymbolEqualityComparer.Default.Equals(resolved, caller).ShouldBeTrue();
        caller.Parameters[binding.ParameterOrdinal].Type.GetDocumentationCommentId().ShouldBe("T:Fixture.IReader");
        var body = model.GetOperation(first.Body!).ShouldBeAssignableTo<IBlockOperation>()!;
        body.Operations.Length.ShouldBe(3);
        var variable = body.Operations[0].DescendantsAndSelf().OfType<IVariableDeclaratorOperation>().ShouldHaveSingleItem();
        var creation = variable.Initializer.ShouldNotBeNull().Value.ShouldBeAssignableTo<IObjectCreationOperation>()!;
        creation.Constructor!.GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
        creation.Syntax.ToString().ShouldBe("new Consumer(Stamp(code), path: Trace(path))");
        SymbolEqualityComparer.Default.Equals(variable.Symbol.Type, compilation.GetTypeByMetadataName("Fixture.Consumer"))
            .ShouldBeTrue();
        IOperation suspension;
        if (isAsync)
        {
            suspension = body.Operations[1].ShouldBeAssignableTo<IExpressionStatementOperation>()!.Operation;
            suspension.ShouldBeAssignableTo<IAwaitOperation>()!.Operation.ShouldBeAssignableTo<IInvocationOperation>()!
                .TargetMethod.GetDocumentationCommentId().ShouldBe("M:System.Threading.Tasks.Task.Yield");
        }
        else
        {
            suspension = body.Operations[1];
            suspension.Kind.ShouldBe(OperationKind.YieldReturn);
            suspension.ShouldBeAssignableTo<IReturnOperation>()!.ReturnedValue
                .ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.GetDocumentationCommentId().ShouldBe("F:System.String.Empty");
        }
        var returned = body.Operations[2].ShouldBeAssignableTo<IReturnOperation>()!;
        returned.Kind.ShouldBe(isAsync ? OperationKind.Return : OperationKind.YieldReturn);
        var read = returned.ReturnedValue.ShouldBeAssignableTo<IInvocationOperation>()!;
        read.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Read(System.String)");
        var use = read.Instance.ShouldBeAssignableTo<ILocalReferenceOperation>()!;
        SymbolEqualityComparer.Default.Equals(use.Local, variable.Symbol).ShouldBeTrue(
            "the exact constructed local must be retained for the read after suspension");
        creation.Syntax.Span.End.ShouldBeLessThan(suspension.Syntax.SpanStart);
        suspension.Syntax.Span.End.ShouldBeLessThan(use.Syntax.SpanStart);
        var second = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Second");
        var originalSecond = SyntaxFactory.ParseCompilationUnit(Callers).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Second");
        second.ToFullString().ShouldBe(originalSecond.ToFullString(), "the second synchronous caller must remain unchanged");
        model.GetDeclaredSymbol(second).ShouldNotBeNull().IsAsync.ShouldBeFalse();
        return selectedCalls;
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
        InvocationExpressionSyntax[] selectedCalls, string context, string? diagnosticPrefix = null)
    {
        AssertBlockedWithoutCandidate(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && !string.IsNullOrWhiteSpace(site.Code)
            && (diagnosticPrefix == null || site.Code.StartsWith(diagnosticPrefix, StringComparison.Ordinal))
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

    private static Task AssertAtomicSiteDispositionsAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        InvocationExpressionSyntax[] selectedCalls) =>
        AssertSiteDispositionsAsync(fixture, plan, selectedCalls, StaticDependencySiteDisposition.Blocked);

    private static async Task AssertSiteDispositionsAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        InvocationExpressionSyntax[] selectedCalls, StaticDependencySiteDisposition expectedDisposition)
    {
        plan.Sites.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(plan.Sites.Count);
        plan.Sites.Count(site => site.Path.EndsWith(".cs", StringComparison.Ordinal)).ShouldBe(selectedCalls.Length + 1,
            "each original selected call and the unselected adapter must have exactly one source site record");
        foreach (var call in selectedCalls)
        {
            var source = fixture.Solution.GetDocument(call.SyntaxTree).ShouldNotBeNull();
            var site = plan.Sites.Where(site => site.Path == source.FilePath
                && site.SpanStart == call.SpanStart && site.SpanLength == call.Span.Length).ShouldHaveSingleItem();
            site.Disposition.ShouldBe(expectedDisposition, "all selected source calls must have the same outcome");
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

    private static string ExpectedWiredConstructor(string source) => source.Replace("private readonly int _code;",
            "private readonly global::Fixture.IReader _reader;\n    private readonly int _code;", StringComparison.Ordinal)
        .Replace("Consumer(int code, string path)",
            "Consumer(int code, string path, global::Fixture.IReader reader)", StringComparison.Ordinal)
        .Replace("_code = code;", "this._reader = reader;\n        _code = code;", StringComparison.Ordinal)
        .Replace("FileAlias.ReadAllText", "this._reader.ReadAllText", StringComparison.Ordinal);

    private static string ExpectedWiredCallers() => Callers.Replace("path: Trace(path));",
            "path: Trace(path), reader: input);", StringComparison.Ordinal)
        .Replace("code: Stamp(2));", "code: Stamp(2), reader: alternate);", StringComparison.Ordinal);

    private static async Task AssertDocumentChangesAsync(Fixture fixture, StaticDependencyIntroductionPlan plan,
        IReadOnlyDictionary<string, string> expectedDocuments)
    {
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var expectedPaths = expectedDocuments.Keys.Select(fixture.PathFor).Order(StringComparer.Ordinal).ToArray();
        plan.Documents.Select(document => document.Path).ShouldBe(expectedPaths);
        plan.Changes.AffectedPaths.ShouldBe(expectedPaths);
        foreach (var change in plan.Documents)
        {
            var name = Path.GetFileName(change.Path);
            change.Before.ShouldBe(fixture.Sources[name]);
            change.Before.ShouldBe((await FindDocument(fixture.Solution, name).GetTextAsync()).ToString());
            var actual = (await FindDocument(candidate, name).GetTextAsync()).ToString();
            change.After.ShouldBe(actual, "the diff must contain the entire final document");
            SyntaxFactory.ParseCompilationUnit(actual).NormalizeWhitespace().ToFullString().ShouldBe(
                SyntaxFactory.ParseCompilationUnit(expectedDocuments[name])
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
        var candidateConstructor = DocumentationCommentId.GetSymbolsForDeclarationId(CandidateConstructorId, newModel.Compilation)
            .ShouldHaveSingleItem().ShouldBeAssignableTo<IMethodSymbol>()!;
        before.Length.ShouldBe(2);
        after.Length.ShouldBe(before.Length);
        foreach (var (oldCreation, newCreation) in before.Zip(after))
        {
            var oldOperation = oldModel.GetOperation(oldCreation).ShouldBeAssignableTo<IObjectCreationOperation>()!;
            var newOperation = newModel.GetOperation(newCreation).ShouldBeAssignableTo<IObjectCreationOperation>()!;
            oldOperation.Constructor!.GetDocumentationCommentId().ShouldBe(OriginalConstructorId);
            newOperation.Constructor!.GetDocumentationCommentId().ShouldBe(CandidateConstructorId);
            SymbolEqualityComparer.Default.Equals(newOperation.Constructor, candidateConstructor).ShouldBeTrue();
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
        InvocationExpressionSyntax[] originalCalls, Compilation candidateCompilation, int expectedSelectedCalls)
    {
        originalCalls.Length.ShouldBe(expectedSelectedCalls);
        plan.Closure.Count.ShouldBe(5 + expectedSelectedCalls);
        plan.Closure.Select(change => change.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(5 + expectedSelectedCalls);
        plan.Closure.Select(change => change.Kind).Order().ShouldBe(new[]
        {
            StaticDependencyClosureChangeKind.Field,
            StaticDependencyClosureChangeKind.ConstructorParameter,
            StaticDependencyClosureChangeKind.ConstructorAssignment,
            StaticDependencyClosureChangeKind.ConstructionArgument,
            StaticDependencyClosureChangeKind.ConstructionArgument
        }.Concat(Enumerable.Repeat(StaticDependencyClosureChangeKind.MappedCall, expectedSelectedCalls)).Order());
        var originalCompilation = await AssertCompilesAsync(fixture.Solution);
        var (consumerRoot, consumerModel) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var originalType = SelectedConsumerDeclaration(consumerRoot, consumerModel);
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
        solution.Projects.Single(project => project.Name == ProjectName).Documents.Single(document => document.Name == name);

    private static async Task<(SyntaxNode Root, SemanticModel Model)> InspectAsync(Solution solution, string name)
    {
        var document = FindDocument(solution, name);
        return ((await document.GetSyntaxRootAsync()).ShouldNotBeNull(),
            (await document.GetSemanticModelAsync()).ShouldNotBeNull());
    }

    private static async Task<Compilation> AssertCompilesAsync(Solution solution)
    {
        var compilation = (await solution.Projects.Single(project => project.Name == ProjectName).GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty("the independent fixture must compile, independently of planner behavior");
        foreach (var project in solution.Projects.Where(project => project.Name != ProjectName))
        {
            var peer = (await project.GetCompilationAsync()).ShouldNotBeNull();
            peer.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty($"fixture project {project.Name} must compile before planning");
        }
        return compilation;
    }

    private static async Task AssertCompleteAsync(Solution solution, params string[] additionalProjectPaths)
    {
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
        completeness.State.ShouldBe(WorkspaceCompletenessState.Complete, string.Join("; ", completeness.Diagnostics));
        WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence.ShouldBeTrue();
        var selectedPath = solution.Projects.Single(project => project.Name == ProjectName).FilePath!;
        completeness.RequestedProjects.ShouldBe(additionalProjectPaths.Append(selectedPath).Order(StringComparer.Ordinal));
    }

    private static void AssertNoFileEffects(PreparedChangeSet changes)
    {
        changes.NewFiles.ShouldBeEmpty();
        changes.DeletedFiles.ShouldBeEmpty();
        changes.MovedFiles.ShouldBeEmpty();
        changes.ProjectFileEdits.ShouldBeEmpty();
        changes.TextEdits.ShouldBeEmpty();
    }

    private static AdhocWorkspace CreateWorkspace(string root, IReadOnlyDictionary<string, string> sources,
        AdhocProjectBuilder.ProjectDef? generator = null, bool relativeProbePath = false)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var references = new[]
        {
            typeof(object).Assembly.Location, typeof(File).Assembly.Location, typeof(Encoding).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")
        }.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        workspace.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), ProjectName, ProjectName,
            LanguageNames.CSharp, filePath: Path.Combine(root, "Fixture.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12), metadataReferences: references,
            analyzerReferences: generator is null ? [] : generator.CreateAnalyzerReferences()));
        foreach (var (name, source) in sources)
            workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(projectId), name,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())),
                filePath: relativeProbePath && name == UnselectedPart ? name : Path.Combine(root, name)));
        return workspace;
    }

    private static Task<Fixture> CreatePartialAsync(string? form = null)
    {
        var typeStart = Consumer.IndexOf("internal sealed class Consumer", StringComparison.Ordinal);
        var readStart = Consumer.IndexOf("    internal string Read", StringComparison.Ordinal);
        var constructorPart = "using FileAlias = System.IO.File;\nnamespace Fixture;\n"
            + Consumer[typeStart..readStart].Replace("sealed class", "sealed partial class", StringComparison.Ordinal)
            + "}";
        // Keep the alias bound after the selected constructor call is changed.
        constructorPart = constructorPart.Replace("private readonly int _code;",
            "private readonly int _code;\n    private static System.Type FileType() => typeof(FileAlias);", StringComparison.Ordinal);
        var readPart = "using System.IO;\nusing System.Text;\nnamespace Fixture;\ninternal sealed partial class Consumer\n{\n"
            + Consumer[readStart..];
        var probeMember = form switch
        {
            "local-collision" => "private static int PartialProbe() { int _reader = 153; return _reader; }",
            "self-escape" => "private object PartialProbe() => this;",
            "deferred" => "private System.Func<string> PartialProbe() => () => this._initial;",
            "async" => "private async System.Threading.Tasks.Task<string> PartialProbe() { await System.Threading.Tasks.Task.Yield(); return _initial; }",
            "iterator" => "private System.Collections.Generic.IEnumerable<string> PartialProbe() { yield return _initial; }",
            "finalizer" => "~Consumer() { _ = _initial; }",
            "multiple" => "internal Consumer(string path) : this(0, path) { }",
            _ => Probe
        };
        var probePart = $"internal sealed partial class Consumer\n{{\n    {probeMember}\n}}";
        var separateProbe = form is "self-escape" or "async" or "iterator" or "finalizer" or "relative" or "shared" or "multiple";
        if (!separateProbe && form != "generated")
            readPart += "\n\n" + probePart;

        var callers = Callers;
        if (form is "primary" or "implicit")
        {
            constructorPart = form == "primary"
                ? "namespace Fixture; internal sealed partial class Consumer(int code, string path) { private readonly int _code = code; private readonly string _initial = path; }"
                : "namespace Fixture; internal sealed partial class Consumer { private readonly int _code = 0; private readonly string _initial = string.Empty; }";
            if (form == "implicit")
                callers = Callers.Replace("new Consumer(Stamp(code), path: Trace(path))", "new Consumer()", StringComparison.Ordinal)
                    .Replace("new Consumer(path: Trace(path), code: Stamp(2))", "new Consumer()", StringComparison.Ordinal);
        }
        if (form == "chained")
            constructorPart = constructorPart.Replace("Consumer(int code, string path)",
                    "Consumer(int code, string path) : base()", StringComparison.Ordinal)
                .Replace("_initial = FileAlias.ReadAllText(path: path + \"-ctor\");", "_initial = path;", StringComparison.Ordinal);
        if (form is "primary" or "implicit" or "chained")
        {
            // Keep both selected calls in an ordinary method so the constructor guard owns the refusal.
            readPart = readPart.Replace("var exact =", "var initial = File.ReadAllText(path: path + \"-ctor\");\n        var exact =",
                StringComparison.Ordinal).Replace("return _initial +", "return _initial + initial +", StringComparison.Ordinal);
        }
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ReadPart] = readPart,
            ["Consumer.cs"] = constructorPart,
            ["Contracts.cs"] = Contracts,
            ["Callers.cs"] = callers
        };
        if (separateProbe)
            sources.Add(UnselectedPart, "namespace Fixture;\n" + probePart);
        var generator = form == "generated"
            ? new AdhocProjectBuilder.ProjectDef(ProjectName).AddGeneratedSource(GeneratedPart, "namespace Fixture;\n" + probePart)
            : null;
        return CreateAsync(sourceOverride: sources, probeContext: form, generator: generator);
    }

    private static async Task<Fixture> CreateAsync(string? refusal = null, string? deferredForm = null,
        string? deferredExpression = null, string? callerForm = null, string? lifetimeForm = null,
        Dictionary<string, string>? sourceOverride = null, string? probeContext = null,
        AdhocProjectBuilder.ProjectDef? generator = null)
    {
        var sources = sourceOverride ?? new Dictionary<string, string>(StringComparer.Ordinal)
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
                "base clone" => "object",
                "inherited method group" or "base method group" => "System.Func<object>",
                "async" => "System.Threading.Tasks.Task<string>",
                "iterator" or "query" or "query predicate" or "constructor query" or "eager query" or "iterator property"
                    => "System.Collections.Generic.IEnumerable<string>",
                _ => "System.Func<string>"
            };
            var escape = deferredForm switch
            {
                "lambda" => $"internal System.Func<string> Escape(string path) => () => {deferredExpression};",
                "inherited method group" => "internal System.Func<object> Escape(string path) => MemberwiseClone;",
                "base clone" => "internal object Escape(string path) => base.MemberwiseClone();",
                "base method group" => "internal System.Func<object> Escape(string path) => base.MemberwiseClone;",
                "async" => $$"""
                    internal async System.Threading.Tasks.Task<string> Escape(string path)
                    {
                        await System.Threading.Tasks.Task.Yield();
                        return {{deferredExpression ?? "_initial"}};
                    }
                    """,
                "iterator" => $$"""
                    internal System.Collections.Generic.IEnumerable<string> Escape(string path)
                    {
                        yield return {{deferredExpression ?? "_initial"}};
                    }
                    """,
                "query" => $"internal System.Collections.Generic.IEnumerable<string> Escape(string path) => from value in new[] {{ 0 }} select {deferredExpression};",
                "query predicate" => $"internal System.Collections.Generic.IEnumerable<string> Escape(string path) => from value in new[] {{ 0 }} where {deferredExpression}.Length > 0 select string.Empty;",
                "constructor query" => "internal System.Collections.Generic.IEnumerable<string> Escape(string path) => _deferred;",
                "eager query" => $"internal System.Collections.Generic.IEnumerable<string> Escape(string path) => from value in new[] {{ {deferredExpression} }} select value;",
                "iterator property" => """
                    internal System.Collections.Generic.IEnumerable<string> Escape
                    {
                        get { yield return _initial; }
                    }
                    internal System.Collections.Generic.IEnumerable<string> GetEscape(string path) => Escape;
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
            if (deferredForm == "constructor query")
                sources["Consumer.cs"] = sources["Consumer.cs"].Replace("private readonly int _code;",
                        "private readonly System.Collections.Generic.IEnumerable<string> _deferred;\n    private readonly int _code;", StringComparison.Ordinal)
                    .Replace("_initial = FileAlias.ReadAllText(path: path + \"-ctor\");",
                        "_initial = FileAlias.ReadAllText(path: path + \"-ctor\");\n        _deferred = from value in new[] { 0 } select "
                        + deferredExpression + ";", StringComparison.Ordinal);
            if (deferredForm is "query" or "query predicate" or "constructor query" or "eager query")
                sources["Consumer.cs"] = "using System.Linq;\n" + sources["Consumer.cs"];
            var escapeCall = deferredForm == "iterator property" ? "GetEscape" : "Escape";
            sources["Callers.cs"] = Callers.Replace("static string First", "static " + returnType + " First", StringComparison.Ordinal)
                .Replace("path: Trace(path));\n        return consumer.Read(path);",
                    "path: Trace(path));\n        return consumer." + escapeCall + "(path);", StringComparison.Ordinal);
        }
        if (callerForm is not null)
        {
            var returnType = callerForm == "async"
                ? "async System.Threading.Tasks.Task<string>" : "System.Collections.Generic.IEnumerable<string>";
            var suspensionAndRead = callerForm switch
            {
                "async" => "await System.Threading.Tasks.Task.Yield();\n        return consumer.Read(path);",
                "query" => "return from value in new[] { 0 } select consumer.Read(path);",
                "query-predicate" => "return from value in new[] { 0 } where consumer.Read(path).Length > 0 select string.Empty;",
                "query-eager" => "return from value in new[] { consumer.Read(path) } select value;",
                _ => "yield return string.Empty;\n        yield return consumer.Read(path);"
            };
            sources["Callers.cs"] = Callers.Replace("static string First", "static " + returnType + " First", StringComparison.Ordinal)
                .Replace("path: Trace(path));\n        return consumer.Read(path);",
                    "path: Trace(path));\n        " + suspensionAndRead, StringComparison.Ordinal);
            if (callerForm.StartsWith("query", StringComparison.Ordinal))
                sources["Callers.cs"] = "using System.Linq;\n" + sources["Callers.cs"];
        }
        if (lifetimeForm is not null)
        {
            var member = lifetimeForm switch
            {
                "selected finalizer" => "~Consumer() { _ = Read(\"late\"); }",
                "nested other" => """
                    private sealed class Other
                    {
                        internal object Clone() => base.MemberwiseClone();
                        ~Other() { }
                    }
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(lifetimeForm))
            };
            sources["Consumer.cs"] = sources["Consumer.cs"].Replace("internal string Read(string path)",
                member + "\n    internal string Read(string path)", StringComparison.Ordinal);
        }
        var root = Path.Combine(Path.GetTempPath(), "rt153-simple-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var (name, source) in sources)
            await File.WriteAllTextAsync(Path.Combine(root, name), source);
        await File.WriteAllTextAsync(Path.Combine(root, "Fixture.csproj"), "<Project />");
        var workspace = CreateWorkspace(root, sources, generator, relativeProbePath: probeContext == "relative");
        var selectedProject = workspace.CurrentSolution.Projects.Single();
        var linkedProjectPath = Path.Combine(root, "LinkedProbe.csproj");
        if (probeContext == "shared")
        {
            var linkedId = ProjectId.CreateNewId();
            workspace.AddProject(ProjectInfo.Create(linkedId, VersionStamp.Create(), LinkedProject, LinkedProject,
                LanguageNames.CSharp, filePath: linkedProjectPath, compilationOptions: selectedProject.CompilationOptions,
                parseOptions: selectedProject.ParseOptions, metadataReferences: selectedProject.MetadataReferences));
            workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(linkedId), UnselectedPart,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(sources[UnselectedPart]), VersionStamp.Create())),
                filePath: Path.Combine(root, UnselectedPart)));
        }
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
            await ExplicitWorkspaceAcquisition.AttachAsync(solution, probeContext == "shared" ? [ProjectName, LinkedProject] : [ProjectName],
                generator is null ? null : new Dictionary<string, string[]> { [ProjectName] = [GeneratedPart] });
            if (probeContext == "shared")
                await AssertCompleteAsync(solution, linkedProjectPath);
            else
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
        var projectPath = selectedProject.FilePath!;
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
