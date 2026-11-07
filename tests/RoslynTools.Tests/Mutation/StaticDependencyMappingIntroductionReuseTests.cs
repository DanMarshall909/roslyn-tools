using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;
using static RoslynTools.Tests.Mutation.StaticDependencyReuseFixture;

namespace RoslynTools.Tests.Mutation;

public sealed class StaticDependencyMappingIntroductionReuseTests
{
    [Fact]
    public async Task prepares_explicit_existing_receiver_without_constructor_changes()
    {
        using var fixture = await CreateAsync();
        var original = fixture.Solution;
        var compilation = await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var source = Symbol<IMethodSymbol>(compilation, SourceId);
        source.IsStatic.ShouldBeTrue();
        source.Locations.All(location => location.IsInMetadata).ShouldBeTrue();
        source.ContainingType.ToDisplayString().ShouldBe("System.IO.File");
        source.Parameters.ShouldHaveSingleItem().Type.SpecialType.ShouldBe(SpecialType.System_String);
        source.ContainingAssembly.Identity.ToString().ShouldBe(fixture.Request.Mapping.Source.AssemblyIdentity);
        var target = Symbol<IMethodSymbol>(compilation, TargetId);
        target.IsStatic.ShouldBeFalse();
        target.ContainingType.TypeKind.ShouldBe(TypeKind.Interface);
        target.Locations.All(location => location.IsInSource).ShouldBeTrue();
        target.ContainingType.GetDocumentationCommentId().ShouldBe(ReceiverId);
        target.ContainingAssembly.ShouldBe(compilation.Assembly);
        target.Parameters.ShouldHaveSingleItem().Type.SpecialType.ShouldBe(SpecialType.System_String);
        var field = Symbol<IFieldSymbol>(compilation, FieldId);
        field.IsStatic.ShouldBeFalse();
        field.IsReadOnly.ShouldBeTrue();
        field.Name.ShouldBe("_existing");
        field.Name.ShouldNotBe(fixture.Request.PreferredFieldName);
        field.Type.ShouldBe(target.ContainingType);
        field.ContainingType.GetDocumentationCommentId().ShouldBe(ConsumerId);
        field.ContainingAssembly.ShouldBe(compilation.Assembly);
        var (root, model) = await InspectAsync(original, "Consumer.cs");
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ShouldHaveSingleItem();
        constructor.Initializer.ShouldBeNull();
        model.GetDeclaredSymbol(constructor).ShouldNotBeNull().GetDocumentationCommentId().ShouldBe(ConstructorId);
        AssertInitialAssignment(constructor, model, field);
        var selected = SourceCalls(root, model);
        selected.Select(call => call.ToString()).ShouldBe([ConstructorCall, MethodCall]);
        selected.Select(call => model.GetEnclosingSymbol(call.SpanStart)!.GetDocumentationCommentId())
            .ShouldBe([ConstructorId, "M:Fixture.Consumer.Read(System.String)"]);
        constructor.Body!.Statements[0].Span.End.ShouldBeLessThan(selected[0].SpanStart);
        foreach (var call in selected)
        {
            var argument = Invocation(model, call).Arguments.ShouldHaveSingleItem();
            argument.InConversion.IsIdentity.ShouldBeTrue();
            argument.OutConversion.IsIdentity.ShouldBeTrue();
            var trace = argument.Value.ShouldBeAssignableTo<IInvocationOperation>()!;
            trace.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Trace(System.String,System.String)");
            trace.Arguments.Select(value => value.Parameter!.Ordinal).ShouldBe([0, 1]);
        }
        fixture.Request.Callers.ShouldBeEmpty();

        var service = new StaticDependencyIntroductionService();
        var plan = await service.PrepareAsync(original, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared,
            "explicit compatible receiver reuse must prepare without constructor changes");
        await AssertPreparedAsync(fixture, fixture.Request, plan, ExpectedConsumer(Consumer), selected);
        var repeated = await service.PrepareAsync(original, fixture.Request);
        AssertSamePlan(plan, repeated);
        foreach (var (name, text) in fixture.Sources)
            (await Document(repeated.Changes.ChangedSolution.ShouldNotBeNull(), name).GetTextAsync()).ToString()
                .ShouldBe(name == "Consumer.cs" ? ExpectedConsumer(Consumer) : text);
        await AssertFreshNoOpAsync(fixture, plan);
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("_existing", "supplied")]
    [InlineData("_initial", "path")]
    [InlineData("Read", "Read")]
    public async Task explicit_field_identity_overrides_unused_colliding_name_hints(string fieldHint, string parameterHint)
    {
        using var fixture = await CreateAsync();
        var request = fixture.Request with { PreferredFieldName = fieldHint, PreferredParameterName = parameterHint };
        var compilation = await AssertCompilesAsync(fixture.Solution);
        var type = Symbol<INamedTypeSymbol>(compilation, ConsumerId);
        type.GetMembers(fieldHint).ShouldNotBeEmpty("the unused field hint deliberately collides with an existing member");
        if (parameterHint != "Read")
            type.InstanceConstructors.ShouldHaveSingleItem().Parameters.ShouldContain(parameter => parameter.Name == parameterHint);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var service = new StaticDependencyIntroductionService();

        var plan = await service.PrepareAsync(fixture.Solution, request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared, "new-name hints are unused during explicit field reuse");
        await AssertPreparedAsync(fixture, request, plan, ExpectedConsumer(Consumer), SourceCalls(root, model));
        var differentHints = await service.PrepareAsync(fixture.Solution, fixture.Request);
        differentHints.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        differentHints.MappingFingerprint.ShouldBe(plan.MappingFingerprint,
            "v1 fingerprints bind the mapping, not the complete request or unused hints");
        differentHints.Documents.ShouldBe(plan.Documents);
        differentHints.Resolution.ShouldBe(plan.Resolution);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task method_only_reuse_does_not_require_constructor_or_consumer_confinement()
    {
        var consumer = MethodOnlyConsumer.Replace("internal sealed class Consumer", "internal class Consumer", StringComparison.Ordinal)
            .Replace("internal Consumer(IReader supplied, string path)",
                "internal Consumer(IReader supplied) : this(supplied, string.Empty) { }\n    internal Consumer(IReader supplied, string path)",
                StringComparison.Ordinal)
            .Replace("internal string Read", "internal object Escape() => this;\n    internal string Read", StringComparison.Ordinal);
        var callers = Callers.Replace("internal static string First", "internal static Consumer Publish(IReader input) => new Consumer(input);\n    internal static string First", StringComparison.Ordinal);
        using var fixture = await CreateAsync(consumer, callers: callers);
        var compilation = await AssertCompilesAsync(fixture.Solution);
        var type = Symbol<INamedTypeSymbol>(compilation, ConsumerId);
        type.IsSealed.ShouldBeFalse();
        type.InstanceConstructors.Length.ShouldBe(2);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var chain = root.DescendantNodes().OfType<ConstructorInitializerSyntax>().ShouldHaveSingleItem();
        chain.ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword).ShouldBeTrue();
        model.GetSymbolInfo(chain).Symbol.ShouldNotBeNull().GetDocumentationCommentId().ShouldBe(ConstructorId);
        var ordinary = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single(item => item.Initializer is null);
        AssertInitialAssignment(ordinary, model, Symbol<IFieldSymbol>(compilation, FieldId));
        var escape = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Escape");
        model.GetOperation(escape.ExpressionBody!.Expression).ShouldBeAssignableTo<IInstanceReferenceOperation>();
        var (callerRoot, callerModel) = await InspectAsync(fixture.Solution, "Callers.cs");
        var publish = callerRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Publish");
        callerModel.GetDeclaredSymbol(publish).ShouldNotBeNull().ReturnType.GetDocumentationCommentId().ShouldBe(ConsumerId);
        var selected = SourceCalls(root, model);
        selected.ShouldHaveSingleItem().ToString().ShouldBe(MethodCall);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared,
            "method-only reuse makes no constructor API edit and needs no constructor closure or consumer confinement proof");
        await AssertPreparedAsync(fixture, fixture.Request, plan, ExpectedConsumer(consumer), selected);
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("other-owner", "F:Fixture.Other._other")]
    [InlineData("inherited", "F:Fixture.Base._inherited")]
    [InlineData("property", "P:Fixture.Consumer.Reader")]
    [InlineData("static", "F:Fixture.Consumer._static")]
    [InlineData("incompatible", "F:Fixture.Consumer._wrong")]
    [InlineData("convertible", "F:Fixture.Consumer._concrete")]
    [InlineData("unknown", "F:Fixture.Consumer._missing")]
    public async Task invalid_exact_field_is_a_contextual_atomic_refusal(string kind, string requestedField)
    {
        var consumer = Consumer.Replace("internal string Read", "internal IReader Reader => _existing;\n    private static IReader _static = new BclReader();\n    private readonly object _wrong = new object();\n    private readonly BclReader _concrete = new BclReader();\n    internal string Read", StringComparison.Ordinal);
        var contracts = Contracts + "\ninternal class Other { internal IReader _other = new BclReader(); }\ninternal class Base { protected IReader _inherited = new BclReader(); }";
        if (kind == "inherited")
            consumer = consumer.Replace("class Consumer", "class Consumer : Base", StringComparison.Ordinal);
        using var fixture = await CreateAsync(consumer, contracts);
        var compilation = await AssertCompilesAsync(fixture.Solution);
        var type = Symbol<INamedTypeSymbol>(compilation, ConsumerId);
        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(requestedField, compilation);
        if (kind == "unknown")
            symbols.ShouldBeEmpty();
        else if (kind == "property")
            symbols.ShouldHaveSingleItem().ShouldBeAssignableTo<IPropertySymbol>()!.ContainingType.ShouldBe(type);
        else
        {
            var field = symbols.ShouldHaveSingleItem().ShouldBeAssignableTo<IFieldSymbol>()!;
            field.ContainingAssembly.ShouldBe(compilation.Assembly);
            if (kind is "other-owner" or "inherited")
            {
                field.ContainingType.ShouldNotBe(type);
                if (kind == "inherited")
                    type.BaseType.ShouldBe(field.ContainingType);
            }
            else
                field.ContainingType.ShouldBe(type);
            field.IsStatic.ShouldBe(kind == "static");
            if (kind is "incompatible" or "convertible")
            {
                var reader = Symbol<INamedTypeSymbol>(compilation, ReceiverId);
                SymbolEqualityComparer.Default.Equals(field.Type, reader).ShouldBeFalse();
                ((CSharpCompilation)compilation).ClassifyConversion(field.Type, reader).IsImplicit.ShouldBe(kind == "convertible",
                    "implicit convertibility alone is insufficient: the declared field type must match exactly");
            }
        }
        await AssertCompleteAsync(fixture.Solution);
        var request = fixture.Request with { ExistingReceiverFieldDocumentationId = requestedField };

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, request);

        AssertBlocked(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.Message.Contains(requestedField, StringComparison.Ordinal)
            && site.Message.Contains("field", StringComparison.OrdinalIgnoreCase),
            "the refusal must identify the explicitly selected field, not reject unrelated constructor wiring");
        var expectedReason = kind switch
        {
            "other-owner" or "inherited" => "selected",
            "property" or "unknown" => "field",
            "static" => "instance",
            _ => "type"
        };
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.Message.Contains(expectedReason, StringComparison.OrdinalIgnoreCase));
        await AssertInventoryAsync(fixture, request, plan, StaticDependencySiteDisposition.Blocked);
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("conditional")]
    [InlineData("nested")]
    [InlineData("later")]
    [InlineData("rhs")]
    [InlineData("helper-first")]
    [InlineData("competing-write")]
    [InlineData("base-initializer")]
    [InlineData("this-chain")]
    [InlineData("convertible-parameter")]
    [InlineData("optional-parameter")]
    [InlineData("ref-parameter")]
    public async Task selected_constructor_requires_the_narrow_first_assignment_proof(string shape)
    {
        var consumer = ConstructorShape(shape);
        var callers = shape switch
        {
            "convertible-parameter" => Callers.Replace("IReader input", "BclReader input", StringComparison.Ordinal)
                .Replace("IReader alternate", "BclReader alternate", StringComparison.Ordinal),
            "ref-parameter" => Callers.Replace("Consumer(input, path)", "Consumer(ref input, path)", StringComparison.Ordinal)
                .Replace("supplied: alternate", "supplied: ref alternate", StringComparison.Ordinal),
            _ => Callers
        };
        using var fixture = await CreateAsync(consumer, callers: callers);
        await AssertConstructorWitnessAsync(fixture, shape);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        AssertBlocked(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && (site.Message.Contains("constructor", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("assign", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("initializ", StringComparison.OrdinalIgnoreCase)),
            "refusal must describe the unavailable constructor proof");
        await AssertInventoryAsync(fixture, fixture.Request, plan, StaticDependencySiteDisposition.Blocked);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task narrowing_mixed_preinitialization_sites_to_the_safe_method_preserves_the_skipped_constructor()
    {
        var consumer = ConstructorShape("later");
        using var fixture = await CreateAsync(consumer);
        await AssertConstructorWitnessAsync(fixture, "later");
        var service = new StaticDependencyIntroductionService();
        var mixed = await service.PrepareAsync(fixture.Solution, fixture.Request);
        AssertBlocked(mixed, fixture.Solution);
        await AssertInventoryAsync(fixture, fixture.Request, mixed, StaticDependencySiteDisposition.Blocked);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var safe = SourceCalls(root, model).Single(call => call.ToString() == MethodCall);
        // Inventory supplies only the opaque selection handle; expected syntax and bindings come from the original model.
        var inventory = await StaticDependencyMappingInventory.FindAsync(fixture.Solution, fixture.Request.Mapping.Source, CancellationToken.None);
        var safeId = inventory.Single(entry => entry.Document.Id == Document(fixture.Solution, "Consumer.cs").Id
            && entry.Reference.Span == safe.Span).Site.Id;
        var request = fixture.Request with { Selection = fixture.Request.Selection with { SiteIds = [safeId] } };

        var plan = await service.PrepareAsync(fixture.Solution, request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        var expected = consumer.Replace(MethodCall, "this._existing.ReadAllText(Trace(path, \"-body\"))", StringComparison.Ordinal);
        await AssertPreparedAsync(fixture, request, plan, expected, [safe]);
        var constructorCall = SourceCalls(root, model).Single(call => call.ToString() == ConstructorCall);
        plan.Sites.Single(site => site.Path == fixture.PathFor("Consumer.cs") && site.SpanStart == constructorCall.SpanStart)
            .Disposition.ShouldBe(StaticDependencySiteDisposition.Skipped);
        plan.MappingFingerprint.ShouldBe(mixed.MappingFingerprint, "selection is not part of the v1 mapping fingerprint");
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("lambda")]
    [InlineData("local-function")]
    [InlineData("async")]
    [InlineData("iterator")]
    [InlineData("query")]
    [InlineData("static")]
    [InlineData("accessor")]
    [InlineData("initializer")]
    public async Task reuse_retains_selected_site_admission_and_deferred_execution_guards(string context)
    {
        var member = context switch
        {
            "lambda" => "internal System.Func<string> Deferred(string path) => () => System.IO.File.ReadAllText(path);",
            "local-function" => "internal string Deferred(string path) { string Later() => System.IO.File.ReadAllText(path); return Later(); }",
            "async" => "internal async System.Threading.Tasks.Task<string> Deferred(string path) { await System.Threading.Tasks.Task.Yield(); return System.IO.File.ReadAllText(path); }",
            "iterator" => "internal System.Collections.Generic.IEnumerable<string> Deferred(string path) { yield return System.IO.File.ReadAllText(path); }",
            "query" => "internal System.Collections.Generic.IEnumerable<string> Deferred(string path) => from n in new[] { 1 } select System.IO.File.ReadAllText(path);",
            "static" => "internal static string Deferred(string path) => System.IO.File.ReadAllText(path);",
            "initializer" => "private readonly string _early = System.IO.File.ReadAllText(string.Empty);",
            _ => "internal string Deferred { get { return System.IO.File.ReadAllText(string.Empty); } }"
        };
        var consumer = MethodOnlyConsumer.Replace("using FileAlias", "using System.Linq;\nusing FileAlias", StringComparison.Ordinal)
            .Replace("internal string Read", member + "\n    internal string Read", StringComparison.Ordinal);
        using var fixture = await CreateAsync(consumer);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var unsafeCall = SourceCalls(root, model).Single(call => call.ToString() != MethodCall);
        AssertDeferredWitness(unsafeCall, model, context);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        AssertBlocked(plan, fixture.Solution);
        await AssertInventoryAsync(fixture, fixture.Request, plan, StaticDependencySiteDisposition.Blocked);
        var blocked = plan.Sites.Single(site => site.Path == fixture.PathFor("Consumer.cs") && site.SpanStart == unsafeCall.SpanStart);
        blocked.Code.ShouldNotBeNullOrWhiteSpace();
        blocked.Message.ShouldNotBeNullOrWhiteSpace();
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("stale-receipt")]
    [InlineData("stale-fingerprint")]
    public async Task reuse_never_upgrades_missing_partial_or_stale_original_authority(string authority)
    {
        using var fixture = await CreateAsync(acquire: authority != "missing");
        var original = fixture.Solution;
        var retained = fixture;
        if (authority == "partial")
        {
            var receipt = fixture.Receipt.ShouldNotBeNull();
            var context = receipt.Manifest.Inputs.Single(input => input.Kind == SemanticInputKind.CompilationContext);
            var outcomes = receipt.Outcomes.Select(outcome => outcome.InputId == context.Id
                ? outcome with { State = SemanticInputState.Failed, Detail = "Explicit unavailable compilation-context evidence." }
                : outcome).ToArray();
            var partial = new WorkspaceAcquisitionReceipt(original, receipt.Manifest, outcomes, receipt.Provenance);
            WorkspaceAcquisitionRegistry.Attach(fixture.Workspace, partial);
            retained = fixture with { Receipt = partial };
        }
        if (authority == "stale-receipt")
            original = original.WithDocumentText(Document(original, "Consumer.cs").Id, SourceText.From(Consumer + "\n// New snapshot"));
        var request = fixture.Request with { ExpectedSourceFingerprint = await SolutionRevision.ComputeAsync(original) };
        if (authority == "stale-fingerprint")
            request = request with { ExpectedSourceFingerprint = new string('0', 64) };
        await AssertCompilesAsync(original);
        (await WorkspaceCompleteness.AssessSemanticAsync(original, CancellationToken.None)).State
            .ShouldBe(authority == "stale-fingerprint" ? WorkspaceCompletenessState.Complete : WorkspaceCompletenessState.Partial);

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(original, request);

        AssertBlocked(plan, original);
        plan.Sites.ShouldContain(site => site.Code == (authority == "stale-fingerprint" ? "introduction.fingerprint" : "introduction.acquisition"));
        await retained.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("unknown")]
    [InlineData("incompatible")]
    [InlineData("convertible")]
    public async Task empty_source_inventory_still_validates_the_explicit_field(string fieldKind)
    {
        var consumer = ExpectedConsumer(Consumer).Replace("internal string Read",
            "private readonly object _wrong = new object();\n    private readonly BclReader _concrete = new BclReader();\n    internal string Read", StringComparison.Ordinal);
        var contracts = Contracts.Replace("System.IO.File.ReadAllText(path)", "path", StringComparison.Ordinal);
        using var fixture = await CreateAsync(consumer, contracts);
        foreach (var document in fixture.Solution.Projects.Single().Documents)
        {
            var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
            var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
            SourceCalls(root, model).ShouldBeEmpty();
        }
        var requestedField = fieldKind switch
        {
            "valid" => FieldId,
            "incompatible" => "F:Fixture.Consumer._wrong",
            "convertible" => "F:Fixture.Consumer._concrete",
            _ => "F:Fixture.Consumer._missing"
        };
        var compilation = await AssertCompilesAsync(fixture.Solution);
        if (fieldKind == "unknown")
            DocumentationCommentId.GetSymbolsForDeclarationId(requestedField, compilation).ShouldBeEmpty();
        else
        {
            var field = Symbol<IFieldSymbol>(compilation, requestedField);
            field.ContainingType.GetDocumentationCommentId().ShouldBe(ConsumerId);
            field.IsStatic.ShouldBeFalse();
            field.Type.GetDocumentationCommentId().ShouldBe(fieldKind switch
            {
                "valid" => ReceiverId,
                "convertible" => "T:Fixture.BclReader",
                _ => "T:System.Object"
            });
        }
        var request = fixture.Request with { ExistingReceiverFieldDocumentationId = requestedField };

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, request);

        if (fieldKind == "valid")
            AssertNoOp(plan, fixture.Solution);
        else
        {
            AssertBlocked(plan, fixture.Solution);
            plan.Sites.ShouldContain(site => site.Message.Contains(request.ExistingReceiverFieldDocumentationId!, StringComparison.Ordinal)
                && site.Message.Contains("field", StringComparison.OrdinalIgnoreCase));
        }
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task shared_selected_roslyn_path_is_refused_across_compiling_complete_project_contexts()
    {
        using var fixture = await CreateAsync(MethodOnlyConsumer, linked: true);
        await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var documents = fixture.Solution.Projects.SelectMany(project => project.Documents)
            .Where(document => document.FilePath == fixture.PathFor("Consumer.cs")).ToArray();
        documents.Length.ShouldBe(2);
        documents.Select(document => document.Project.Id).Distinct().Count().ShouldBe(2);
        documents.Select(document => document.Id).Distinct().Count().ShouldBe(2);
        foreach (var document in documents)
        {
            (document is SourceGeneratedDocument).ShouldBeFalse();
            Path.IsPathFullyQualified(document.FilePath!).ShouldBeTrue();
            var compilation = (await document.Project.GetCompilationAsync()).ShouldNotBeNull();
            Symbol<INamedTypeSymbol>(compilation, ConsumerId).Locations.Count(location => location.IsInSource).ShouldBe(1);
        }

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        AssertBlocked(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.Message.Contains("path", StringComparison.OrdinalIgnoreCase)
            && (site.Message.Contains("shared", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                || site.Message.Contains("unambiguous", StringComparison.OrdinalIgnoreCase)));
        await AssertInventoryAsync(fixture, fixture.Request, plan, StaticDependencySiteDisposition.Blocked);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task relative_selected_document_path_is_refused_even_with_complete_acquisition()
    {
        using var fixture = await CreateAsync(MethodOnlyConsumer, selectedPath: "Consumer.cs");
        await AssertCompleteAsync(fixture.Solution);
        Path.IsPathFullyQualified(Document(fixture.Solution, "Consumer.cs").FilePath!).ShouldBeFalse();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, fixture.Request);

        AssertBlocked(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Message.Contains("path", StringComparison.OrdinalIgnoreCase));
        await AssertInventoryAsync(fixture, fixture.Request, plan, StaticDependencySiteDisposition.Blocked);
        await fixture.AssertUntouchedAsync();
    }

    [Fact]
    public async Task reuse_requires_a_selected_source_type_owned_by_the_acquired_project()
    {
        using var fixture = await CreateAsync(MethodOnlyConsumer);
        var compilation = await AssertCompilesAsync(fixture.Solution);
        var metadataType = Symbol<INamedTypeSymbol>(compilation, "T:System.IO.StreamReader");
        metadataType.TypeKind.ShouldBe(TypeKind.Class);
        metadataType.Locations.All(location => location.IsInMetadata).ShouldBeTrue();
        SymbolEqualityComparer.Default.Equals(metadataType.ContainingAssembly, compilation.Assembly).ShouldBeFalse();
        var request = fixture.Request with { Selection = fixture.Request.Selection with { TypeDocumentationId = "T:System.IO.StreamReader" } };

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, request);

        AssertBlocked(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Code == "introduction.type");
        await fixture.AssertUntouchedAsync();
    }

    [Theory]
    [InlineData("availability")]
    [InlineData("lifetime")]
    [InlineData("non-null")]
    [InlineData("exceptions")]
    [InlineData("encoding")]
    [InlineData("cancellation")]
    [InlineData("resources")]
    [InlineData("evidence")]
    public async Task structural_reuse_does_not_replace_declared_receiver_and_adapter_preconditions(string missing)
    {
        using var fixture = await CreateAsync();
        var declared = fixture.Request.Mapping.Preconditions;
        var incomplete = missing switch
        {
            "availability" => declared with { ReceiverAvailability = "" },
            "lifetime" => declared with { ReceiverLifetime = "" },
            "non-null" => declared with { ReceiverNonNull = "" },
            "exceptions" => declared with { AdapterExceptions = "" },
            "encoding" => declared with { AdapterEncoding = "" },
            "cancellation" => declared with { AdapterCancellation = "" },
            "resources" => declared with { AdapterResourceOwnership = "" },
            _ => declared with { ContractEvidence = [] }
        };
        var request = fixture.Request with { Mapping = fixture.Request.Mapping with { Preconditions = incomplete } };

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(fixture.Solution, request);

        AssertBlocked(plan, fixture.Solution);
        plan.Sites.ShouldContain(site => site.Message.Contains(missing == "resources" ? "resource" : missing,
            StringComparison.OrdinalIgnoreCase));
        await AssertInventoryAsync(fixture, request, plan, StaticDependencySiteDisposition.Blocked);
        await fixture.AssertUntouchedAsync();
    }

    private static string ExpectedConsumer(string original) => original
        .Replace(ConstructorCall, "this._existing.ReadAllText(path: Trace(path, \"-ctor\"))", StringComparison.Ordinal)
        .Replace(MethodCall, "this._existing.ReadAllText(Trace(path, \"-body\"))", StringComparison.Ordinal);

    private static T Symbol<T>(Compilation compilation, string id) where T : class, ISymbol =>
        DocumentationCommentId.GetSymbolsForDeclarationId(id, compilation).ShouldHaveSingleItem().ShouldBeAssignableTo<T>()!;

    private static async Task<(SyntaxNode Root, SemanticModel Model)> InspectAsync(Solution solution, string name)
    {
        var document = Document(solution, name);
        return ((await document.GetSyntaxRootAsync()).ShouldNotBeNull(), (await document.GetSemanticModelAsync()).ShouldNotBeNull());
    }

    private static InvocationExpressionSyntax[] SourceCalls(SyntaxNode root, SemanticModel model) =>
        Calls(root, model, SourceId);

    private static InvocationExpressionSyntax[] Calls(SyntaxNode root, SemanticModel model, string id) => root.DescendantNodes()
        .OfType<InvocationExpressionSyntax>().Where(call => model.GetOperation(call) is IInvocationOperation operation
            && operation.TargetMethod.GetDocumentationCommentId() == id).ToArray();

    private static IInvocationOperation Invocation(SemanticModel model, InvocationExpressionSyntax call) =>
        model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;

    private static void AssertInitialAssignment(ConstructorDeclarationSyntax constructor, SemanticModel model, IFieldSymbol field)
    {
        var assignment = model.GetOperation(constructor.Body.ShouldNotBeNull().Statements[0])
            .ShouldBeAssignableTo<IExpressionStatementOperation>()!.Operation.ShouldBeAssignableTo<ISimpleAssignmentOperation>()!;
        assignment.Target.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.ShouldBe(field);
        var parameter = assignment.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter;
        parameter.Name.ShouldBe("supplied");
        parameter.Ordinal.ShouldBe(0);
        parameter.RefKind.ShouldBe(RefKind.None);
        parameter.IsParams.ShouldBeFalse();
        parameter.IsOptional.ShouldBeFalse();
        parameter.Type.ShouldBe(field.Type);
        parameter.ContainingSymbol.GetDocumentationCommentId().ShouldBe(ConstructorId);
    }

    private static async Task AssertPreparedAsync(StaticDependencyReuseFixture fixture,
        StaticDependencyIntroductionRequest request, StaticDependencyIntroductionPlan plan,
        string expectedConsumer, InvocationExpressionSyntax[] selected)
    {
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        plan.SchemaVersion.ShouldBe("rt.static-dependency.introduction-plan.v1");
        plan.Request.ShouldBe(request);
        plan.SourceFingerprint.ShouldBe(fixture.Request.ExpectedSourceFingerprint);
        plan.MappingFingerprint.ShouldBe(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request.Mapping))),
            "the v1 mapping fingerprint must not become a whole-request hash");
        plan.Resolution.ShouldBe(new StaticDependencyIntroductionResolution(true, FieldId, "_existing", null));
        request.Callers.ShouldBeEmpty();
        plan.Changes.OriginalSolution.ShouldBeSameAs(fixture.Solution);
        plan.Changes.HasChanges.ShouldBeTrue();
        plan.Changes.AffectedPaths.ShouldBe([fixture.PathFor("Consumer.cs")]);
        AssertNoEffects(plan.Changes);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        candidate.Workspace.ShouldBeSameAs(fixture.Workspace);
        candidate.ProjectIds.ShouldBe(fixture.Solution.ProjectIds);
        candidate.Projects.Single().DocumentIds.ShouldBe(fixture.Solution.Projects.Single().DocumentIds);
        plan.Documents.ShouldBe([new StaticDependencyDocumentChange(fixture.PathFor("Consumer.cs"), fixture.Sources["Consumer.cs"], expectedConsumer)],
            "the full candidate document may change only the explicitly selected callee expressions");
        foreach (var (name, originalText) in fixture.Sources)
            (await Document(candidate, name).GetTextAsync()).ToString().ShouldBe(name == "Consumer.cs" ? expectedConsumer : originalText);
        var projectChanges = candidate.GetChanges(fixture.Solution).GetProjectChanges().ShouldHaveSingleItem();
        projectChanges.GetChangedDocuments().ShouldBe([Document(fixture.Solution, "Consumer.cs").Id]);
        projectChanges.GetAddedDocuments().ShouldBeEmpty();
        projectChanges.GetRemovedDocuments().ShouldBeEmpty();
        var beforeCompilation = await AssertCompilesAsync(fixture.Solution);
        var afterCompilation = await AssertCompilesAsync(candidate);
        var beforeType = Symbol<INamedTypeSymbol>(beforeCompilation, ConsumerId);
        var afterType = Symbol<INamedTypeSymbol>(afterCompilation, ConsumerId);
        afterType.GetMembers().OfType<IFieldSymbol>().Select(FieldShape).Order(StringComparer.Ordinal)
            .ShouldBe(beforeType.GetMembers().OfType<IFieldSymbol>().Select(FieldShape).Order(StringComparer.Ordinal));
        afterType.InstanceConstructors.Select(method => method.GetDocumentationCommentId()).Order(StringComparer.Ordinal)
            .ShouldBe(beforeType.InstanceConstructors.Select(method => method.GetDocumentationCommentId()).Order(StringComparer.Ordinal));
        afterType.GetMembers().OfType<IFieldSymbol>().Count(field => field.Type.GetDocumentationCommentId() == ReceiverId)
            .ShouldBe(1, "there is exactly the original dependency, with no fallback or duplicate field");
        var (beforeRoot, beforeModel) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var (afterRoot, afterModel) = await InspectAsync(candidate, "Consumer.cs");
        afterRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Select(ctor => (ctor.ParameterList.ToFullString(), ctor.Initializer?.ToFullString()))
            .ShouldBe(beforeRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Select(ctor => (ctor.ParameterList.ToFullString(), ctor.Initializer?.ToFullString())));
        var beforeAssignments = FieldAssignments(beforeRoot, beforeModel);
        var afterAssignments = FieldAssignments(afterRoot, afterModel);
        afterAssignments.Select(assignment => assignment.Syntax.ToFullString())
            .ShouldBe(beforeAssignments.Select(assignment => assignment.Syntax.ToFullString()), "existing dependency assignments must remain byte-for-byte intact");
        var mapped = Calls(afterRoot, afterModel, TargetId);
        mapped.Length.ShouldBe(selected.Length);
        foreach (var (before, after) in selected.Zip(mapped))
        {
            var oldCall = Invocation(beforeModel, before);
            var newCall = Invocation(afterModel, after);
            newCall.TargetMethod.GetDocumentationCommentId().ShouldBe(TargetId);
            newCall.TargetMethod.ContainingAssembly.Identity.ToString().ShouldBe(request.Mapping.Target.AssemblyIdentity);
            newCall.TargetMethod.ContainingType.GetDocumentationCommentId().ShouldBe(ReceiverId);
            var receiver = newCall.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
            receiver.Field.GetDocumentationCommentId().ShouldBe(FieldId);
            receiver.Field.ContainingAssembly.Identity.ToString().ShouldBe(request.Mapping.Target.AssemblyIdentity);
            receiver.Type.ShouldBe(Symbol<INamedTypeSymbol>(afterCompilation, ReceiverId));
            receiver.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>()!.ReferenceKind.ShouldBe(InstanceReferenceKind.ContainingTypeInstance);
            after.ArgumentList.ToFullString().ShouldBe(before.ArgumentList.ToFullString());
            newCall.Arguments.Select(ArgumentEvidence).ShouldBe(oldCall.Arguments.Select(ArgumentEvidence),
                "ordered argument syntax, parameter associations, conversions and side-effecting nested calls must be preserved");
        }
        await AssertCallerBindingsAsync(fixture.Solution, candidate);
        await AssertInventoryAsync(fixture, request, plan, StaticDependencySiteDisposition.ConvertedInCandidate);
        var converted = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate).ToArray();
        converted.Select(site => (site.Path, site.SpanStart, site.SpanLength))
            .ShouldBe(selected.Select(call => (fixture.PathFor("Consumer.cs"), call.SpanStart, call.Span.Length)));
        foreach (var site in converted)
        {
            site.Message.ShouldContain("existing", Case.Insensitive);
            site.Message.ShouldNotContain("constructor/caller wiring", Case.Insensitive);
        }
        plan.Closure.Count.ShouldBe(selected.Length);
        plan.Closure.Select(change => change.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(selected.Length);
        foreach (var change in plan.Closure)
        {
            change.Kind.ShouldBe(StaticDependencyClosureChangeKind.MappedCall);
            change.ProjectPath.ShouldBe(request.Selection.ProjectPath);
            change.OriginalSymbolDocumentationId.ShouldBe(SourceId);
            change.CandidateSymbolDocumentationId.ShouldBe(TargetId);
            change.Detail.ShouldNotBeNullOrWhiteSpace();
        }
        plan.Closure.Select(change => (change.Path, change.SpanStart, change.SpanLength))
            .ShouldBe(selected.Select(call => (fixture.PathFor("Consumer.cs"), call.SpanStart, call.Span.Length)));
        WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldBeSameAs(fixture.Receipt);
        (await WorkspaceCompleteness.AssessSemanticAsync(candidate, CancellationToken.None)).State.ShouldBe(WorkspaceCompletenessState.Partial,
            "a candidate compilation is not a new original acquisition receipt");
        await AssertCompleteAsync(fixture.Solution);
    }

    private static string FieldShape(IFieldSymbol field) =>
        $"{field.GetDocumentationCommentId()}|{field.Type.GetDocumentationCommentId()}|{field.IsStatic}|{field.IsReadOnly}|{field.DeclaredAccessibility}";

    private static ISimpleAssignmentOperation[] FieldAssignments(SyntaxNode root, SemanticModel model) => root.DescendantNodes()
        .OfType<AssignmentExpressionSyntax>().Select(node => model.GetOperation(node)).OfType<ISimpleAssignmentOperation>()
        .Where(assignment => assignment.Target is IFieldReferenceOperation field && field.Field.GetDocumentationCommentId() == FieldId).ToArray();

    private static string ArgumentEvidence(IArgumentOperation argument) =>
        $"{argument.Syntax.ToFullString()}|{argument.Parameter!.Ordinal}|{argument.Parameter.Type.ToDisplayString()}|{argument.ArgumentKind}|"
        + $"{ConversionEvidence(argument.InConversion)}|{ConversionEvidence(argument.OutConversion)}|"
        + string.Join(";", argument.Value.DescendantsAndSelf().Select(operation => operation switch
        {
            IInvocationOperation call => $"call:{call.TargetMethod.GetDocumentationCommentId()}:{call.TargetMethod.ContainingAssembly.Identity}",
            IConversionOperation conversion => $"conversion:{ConversionEvidence(conversion.Conversion)}:{conversion.IsChecked}",
            IParameterReferenceOperation parameter => $"parameter:{parameter.Parameter.ContainingSymbol.GetDocumentationCommentId()}:{parameter.Parameter.Ordinal}",
            _ => $"{operation.Kind}:{operation.Type?.ToDisplayString()}:{operation.Syntax}"
        }));

    private static string ConversionEvidence(CommonConversion conversion) =>
        $"{conversion.Exists}:{conversion.IsIdentity}:{conversion.IsImplicit}:{conversion.IsReference}:{conversion.IsNumeric}:{conversion.IsNullable}:{conversion.IsUserDefined}:{conversion.MethodSymbol?.GetDocumentationCommentId()}";

    private static async Task AssertCallerBindingsAsync(Solution original, Solution candidate)
    {
        var (beforeRoot, beforeModel) = await InspectAsync(original, "Callers.cs");
        var (afterRoot, afterModel) = await InspectAsync(candidate, "Callers.cs");
        var beforeCreations = beforeRoot.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().ToArray();
        var afterCreations = afterRoot.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().ToArray();
        beforeCreations.Length.ShouldBeGreaterThanOrEqualTo(2);
        afterCreations.Length.ShouldBe(beforeCreations.Length);
        foreach (var (before, after) in beforeCreations.Zip(afterCreations))
        {
            var oldCreation = beforeModel.GetOperation(before).ShouldBeAssignableTo<IObjectCreationOperation>()!;
            var newCreation = afterModel.GetOperation(after).ShouldBeAssignableTo<IObjectCreationOperation>()!;
            oldCreation.Constructor!.ContainingType.GetDocumentationCommentId().ShouldBe(ConsumerId);
            newCreation.Constructor!.GetDocumentationCommentId().ShouldBe(oldCreation.Constructor!.GetDocumentationCommentId());
            newCreation.Arguments.Select(ArgumentEvidence).ShouldBe(oldCreation.Arguments.Select(ArgumentEvidence));
            var reader = oldCreation.Arguments.Single(argument => argument.Parameter!.Type.GetDocumentationCommentId() == ReceiverId);
            reader.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter.Type.GetDocumentationCommentId().ShouldBe(ReceiverId);
        }
    }

    private static async Task AssertInventoryAsync(StaticDependencyReuseFixture fixture, StaticDependencyIntroductionRequest request,
        StaticDependencyIntroductionPlan plan, StaticDependencySiteDisposition selectedDisposition)
    {
        var originalCalls = 0;
        plan.Sites.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(plan.Sites.Count);
        foreach (var document in fixture.Solution.Projects.SelectMany(project => project.Documents))
        {
            var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
            var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
            foreach (var call in SourceCalls(root, model))
            {
                originalCalls++;
                var owner = model.GetDeclaredSymbol(call.Ancestors().OfType<TypeDeclarationSyntax>().First()).ShouldNotBeNull();
                var site = plan.Sites.Where(site => site.ProjectPath == document.Project.FilePath && site.Path == document.FilePath
                    && site.SpanStart == call.SpanStart && site.SpanLength == call.Span.Length).ShouldHaveSingleItem();
                site.TypeDocumentationId.ShouldBe(owner.GetDocumentationCommentId());
                site.Id.ShouldNotBeNullOrWhiteSpace();
                var selected = site.ProjectPath == request.Selection.ProjectPath && site.TypeDocumentationId == ConsumerId
                    && (request.Selection.SiteIds is null || request.Selection.SiteIds.Contains(site.Id, StringComparer.Ordinal));
                site.Disposition.ShouldBe(selected ? selectedDisposition : StaticDependencySiteDisposition.Skipped);
                site.Code.ShouldNotBeNullOrWhiteSpace();
                site.Message.ShouldNotBeNullOrWhiteSpace();
            }
        }
        plan.Sites.Count(site => site.SpanLength > 0).ShouldBe(originalCalls,
            "all original source anchors, including the unselected adapter and other project contexts, must survive exactly once");
        plan.Sites.ShouldContain(site => site.TypeDocumentationId == "T:Fixture.BclReader"
            && site.Disposition == StaticDependencySiteDisposition.Skipped);
    }

    private static void AssertSamePlan(StaticDependencyIntroductionPlan expected, StaticDependencyIntroductionPlan actual)
    {
        actual.Status.ShouldBe(expected.Status);
        actual.SchemaVersion.ShouldBe(expected.SchemaVersion);
        actual.SourceFingerprint.ShouldBe(expected.SourceFingerprint);
        actual.MappingFingerprint.ShouldBe(expected.MappingFingerprint);
        actual.Documents.ShouldBe(expected.Documents);
        actual.Sites.ShouldBe(expected.Sites);
        actual.Closure.ShouldBe(expected.Closure);
        actual.Resolution.ShouldBe(expected.Resolution);
    }

    private static async Task AssertFreshNoOpAsync(StaticDependencyReuseFixture fixture, StaticDependencyIntroductionPlan plan)
    {
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in candidate.Projects.Single().Documents)
            sources.Add(document.Name, (await document.GetTextAsync()).ToString());
        using var fresh = CreateWorkspace(fixture.Root, sources);
        var original = fresh.CurrentSolution;
        fresh.ShouldNotBeSameAs(fixture.Workspace);
        await ExplicitWorkspaceAcquisition.AttachAsync(original, [ProjectName]);
        await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var receipt = WorkspaceAcquisitionRegistry.Get(fresh).Receipt.ShouldNotBeNull();
        receipt.ShouldNotBeSameAs(fixture.Receipt);
        var fingerprint = await SolutionRevision.ComputeAsync(original);
        fingerprint.ShouldBe(await SolutionRevision.ComputeAsync(candidate));
        fingerprint.ShouldNotBe(plan.SourceFingerprint);
        var request = fixture.Request with { ExpectedSourceFingerprint = fingerprint };
        var service = new StaticDependencyIntroductionService();

        var noOp = await service.PrepareAsync(original, request);

        AssertNoOp(noOp, original);
        noOp.SourceFingerprint.ShouldBe(fingerprint);
        noOp.MappingFingerprint.ShouldBe(plan.MappingFingerprint);
        noOp.Sites.ShouldHaveSingleItem().Disposition.ShouldBe(StaticDependencySiteDisposition.Skipped);
        noOp.Sites[0].TypeDocumentationId.ShouldBe("T:Fixture.BclReader");
        var repeated = await service.PrepareAsync(original, request);
        AssertSamePlan(noOp, repeated);
        fresh.CurrentSolution.ShouldBeSameAs(original);
        WorkspaceAcquisitionRegistry.Get(fresh).Receipt.ShouldBeSameAs(receipt);
        (await SolutionRevision.ComputeAsync(original)).ShouldBe(fingerprint);
    }

    private static void AssertNoEffects(PreparedChangeSet changes)
    {
        changes.NewFiles.ShouldBeEmpty();
        changes.DeletedFiles.ShouldBeEmpty();
        changes.MovedFiles.ShouldBeEmpty();
        changes.ProjectFileEdits.ShouldBeEmpty();
        changes.TextEdits.ShouldBeEmpty();
    }

    private static void AssertUnchangedPlan(StaticDependencyIntroductionPlan plan, Solution original)
    {
        plan.Documents.ShouldBeEmpty();
        plan.Closure.ShouldBeEmpty();
        plan.Resolution.ShouldBeNull();
        plan.Changes.HasChanges.ShouldBeFalse();
        plan.Changes.AffectedPaths.ShouldBeEmpty();
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        plan.Changes.ChangedSolution.ShouldBeSameAs(original);
        AssertNoEffects(plan.Changes);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate);
    }

    private static void AssertNoOp(StaticDependencyIntroductionPlan plan, Solution original)
    {
        plan.Status.ShouldBe(StaticDependencyPlanStatus.NoOp);
        AssertUnchangedPlan(plan, original);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
    }

    private static void AssertBlocked(StaticDependencyIntroductionPlan plan, Solution original)
    {
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        AssertUnchangedPlan(plan, original);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
    }

    private static string ConstructorShape(string shape)
    {
        var consumer = shape switch
        {
            "conditional" => Consumer.Replace(Assignment,
                "if (path.Length > 0) { this._existing = supplied; } else { this._existing = supplied; }", StringComparison.Ordinal),
            "nested" => Consumer.Replace(Assignment, "{ this._existing = supplied; }", StringComparison.Ordinal),
            "later" => Consumer.Replace(Assignment + "\n        _initial = " + ConstructorCall + ";",
                "_initial = " + ConstructorCall + ";\n        " + Assignment, StringComparison.Ordinal),
            "rhs" => Consumer.Replace(Assignment, "this._existing = Choose(supplied, FileAlias.ReadAllText(path));", StringComparison.Ordinal)
                .Replace("internal string Read", "private static IReader Choose(IReader supplied, string ignored) => supplied;\n    internal string Read", StringComparison.Ordinal),
            "helper-first" => Consumer.Replace(Assignment, "Trace(path, \"-before\");\n        " + Assignment, StringComparison.Ordinal),
            "competing-write" => Consumer.Replace(Assignment, Assignment + "\n        this._existing = new BclReader();", StringComparison.Ordinal),
            "base-initializer" => Consumer.Replace("Consumer(IReader supplied, string path)", "Consumer(IReader supplied, string path) : base()", StringComparison.Ordinal),
            "this-chain" => Consumer.Replace("internal Consumer(IReader supplied, string path)",
                    "internal Consumer(IReader supplied) { this._existing = supplied; _initial = string.Empty; }\n    internal Consumer(IReader supplied, string path) : this(supplied)", StringComparison.Ordinal)
                .Replace("        " + Assignment + "\n", "", StringComparison.Ordinal),
            "convertible-parameter" => Consumer.Replace("Consumer(IReader supplied, string path)", "Consumer(BclReader supplied, string path)", StringComparison.Ordinal),
            "optional-parameter" => Consumer.Replace("Consumer(IReader supplied, string path)", "Consumer(IReader supplied = null, string path = \"\")", StringComparison.Ordinal),
            "ref-parameter" => Consumer.Replace("Consumer(IReader supplied, string path)", "Consumer(ref IReader supplied, string path)", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        consumer.ShouldNotBe(Consumer, "the negative fixture must actually contain its stated semantic difference");
        return consumer;
    }

    private static async Task AssertConstructorWitnessAsync(StaticDependencyReuseFixture fixture, string shape)
    {
        await AssertCompilesAsync(fixture.Solution);
        await AssertCompleteAsync(fixture.Solution);
        var (root, model) = await InspectAsync(fixture.Solution, "Consumer.cs");
        var calls = SourceCalls(root, model);
        calls.ShouldContain(call => call.ToString() == MethodCall);
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
            .Single(ctor => calls.Any(call => call.Ancestors().Contains(ctor)));
        var constructorCalls = calls.Where(call => call.Ancestors().Contains(constructor)).ToArray();
        constructorCalls.ShouldNotBeEmpty();
        var assignments = FieldAssignments(constructor, model);
        var first = constructor.Body.ShouldNotBeNull().Statements[0];
        switch (shape)
        {
            case "conditional":
                first.ShouldBeAssignableTo<IfStatementSyntax>();
                assignments.Length.ShouldBe(2);
                foreach (var assignment in assignments)
                    assignment.Syntax.Ancestors().ShouldContain(node => node is IfStatementSyntax);
                break;
            case "nested":
                first.ShouldBeAssignableTo<BlockSyntax>();
                first.Span.Contains(assignments.ShouldHaveSingleItem().Syntax.Span).ShouldBeTrue();
                break;
            case "later":
                constructorCalls.ShouldHaveSingleItem().SpanStart.ShouldBeLessThan(assignments.ShouldHaveSingleItem().Syntax.SpanStart);
                first.Span.Contains(constructorCalls[0].Span).ShouldBeTrue();
                break;
            case "rhs":
                var stored = assignments.ShouldHaveSingleItem().Value.ShouldBeAssignableTo<IInvocationOperation>()!;
                stored.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Choose(Fixture.IReader,System.String)");
                stored.DescendantsAndSelf().OfType<IInvocationOperation>().Count(call => call.TargetMethod.GetDocumentationCommentId() == SourceId).ShouldBe(1);
                constructorCalls.Count(call => stored.Syntax.Span.Contains(call.Span)).ShouldBe(1);
                break;
            case "helper-first":
                var helper = model.GetOperation(first).ShouldBeAssignableTo<IExpressionStatementOperation>()!
                    .Operation.ShouldBeAssignableTo<IInvocationOperation>()!;
                helper.TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.Trace(System.String,System.String)");
                helper.Syntax.SpanStart.ShouldBeLessThan(assignments.ShouldHaveSingleItem().Syntax.SpanStart);
                assignments[0].Syntax.Span.End.ShouldBeLessThan(constructorCalls.ShouldHaveSingleItem().SpanStart);
                break;
            case "competing-write":
                assignments.Length.ShouldBe(2);
                assignments[0].Value.ShouldBeAssignableTo<IParameterReferenceOperation>();
                assignments[1].Value.DescendantsAndSelf().OfType<IObjectCreationOperation>().ShouldHaveSingleItem()
                    .Type!.GetDocumentationCommentId().ShouldBe("T:Fixture.BclReader");
                assignments[1].Syntax.Span.End.ShouldBeLessThan(constructorCalls.ShouldHaveSingleItem().SpanStart);
                break;
            case "base-initializer":
                constructor.Initializer.ShouldNotBeNull().ThisOrBaseKeyword.IsKind(SyntaxKind.BaseKeyword).ShouldBeTrue();
                model.GetSymbolInfo(constructor.Initializer!).Symbol.ShouldBeAssignableTo<IMethodSymbol>()!
                    .ContainingType.SpecialType.ShouldBe(SpecialType.System_Object);
                break;
            case "this-chain":
                constructor.Initializer.ShouldNotBeNull().ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword).ShouldBeTrue();
                model.GetSymbolInfo(constructor.Initializer!).Symbol.ShouldNotBeNull().GetDocumentationCommentId()
                    .ShouldBe("M:Fixture.Consumer.#ctor(Fixture.IReader)");
                assignments.ShouldBeEmpty();
                FieldAssignments(root, model).ShouldHaveSingleItem();
                break;
            case "convertible-parameter":
                var conversion = assignments.ShouldHaveSingleItem().Value.ShouldBeAssignableTo<IConversionOperation>()!;
                conversion.Conversion.IsImplicit.ShouldBeTrue();
                conversion.Conversion.IsReference.ShouldBeTrue();
                conversion.Operand.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter.Type.GetDocumentationCommentId()
                    .ShouldBe("T:Fixture.BclReader");
                conversion.Type!.GetDocumentationCommentId().ShouldBe(ReceiverId);
                break;
            case "optional-parameter":
                assignments.ShouldHaveSingleItem().Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!
                    .Parameter.IsOptional.ShouldBeTrue();
                break;
            case "ref-parameter":
                assignments.ShouldHaveSingleItem().Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!
                    .Parameter.RefKind.ShouldBe(RefKind.Ref);
                break;
        }
        foreach (var assignment in assignments)
            assignment.Target.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.GetDocumentationCommentId().ShouldBe(FieldId);
    }

    private static void AssertDeferredWitness(InvocationExpressionSyntax call, SemanticModel model, string context)
    {
        var operation = Invocation(model, call);
        operation.TargetMethod.Locations.All(location => location.IsInMetadata).ShouldBeTrue();
        switch (context)
        {
            case "lambda":
                call.Ancestors().OfType<LambdaExpressionSyntax>().ShouldHaveSingleItem();
                break;
            case "local-function":
                call.Ancestors().OfType<LocalFunctionStatementSyntax>().ShouldHaveSingleItem();
                break;
            case "async":
                model.GetEnclosingSymbol(call.SpanStart).ShouldBeAssignableTo<IMethodSymbol>()!.IsAsync.ShouldBeTrue();
                break;
            case "iterator":
                call.Ancestors().OfType<YieldStatementSyntax>().ShouldHaveSingleItem();
                break;
            case "query":
                call.Ancestors().OfType<SelectClauseSyntax>().ShouldHaveSingleItem();
                var parents = new List<IOperation>();
                for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
                    parents.Add(parent);
                parents.OfType<IAnonymousFunctionOperation>().ShouldNotBeEmpty(
                    "this source call belongs to a deferred query selector, not an eager initial source");
                break;
            case "static":
                model.GetEnclosingSymbol(call.SpanStart).ShouldBeAssignableTo<IMethodSymbol>()!.IsStatic.ShouldBeTrue();
                break;
            case "accessor":
                call.Ancestors().OfType<AccessorDeclarationSyntax>().ShouldHaveSingleItem();
                break;
            case "initializer":
                call.Ancestors().OfType<FieldDeclarationSyntax>().ShouldHaveSingleItem();
                call.Ancestors().OfType<BaseMethodDeclarationSyntax>().ShouldBeEmpty();
                break;
        }
    }
}
