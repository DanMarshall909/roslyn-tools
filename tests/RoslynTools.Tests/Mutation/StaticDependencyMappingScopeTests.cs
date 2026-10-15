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

public sealed class StaticDependencyMappingScopeTests
{
    private const string ProjectName = "MappingScope";
    private static readonly string ProjectPath = Path.GetFullPath("/virtual/rt153-scope/MappingScope.csproj");
    private static readonly string DocumentPath = Path.GetFullPath("/virtual/rt153-scope/Consumer.cs");
    private const string ConsumerId = "T:Fixture.Consumer";
    private const string SourceId = "M:System.IO.File.ReadAllText(System.String)";
    private const string TargetId = "M:Fixture.IReader.ReadAllText(System.String)";
    private const string Reader = "public interface IReader { string ReadAllText(string path); }";
    private const string Supported = "public string Read(string path) => File.ReadAllText(path);";

    [Fact]
    public async Task selected_generated_partial_blocks_the_authored_and_generated_selection_together()
    {
        const string hint = "Consumer.Selected.g.cs";
        const string generatedText = """
            namespace Fixture;
            public partial class Consumer
            {
                public partial string GeneratedRead(string path) => global::System.IO.File.ReadAllText(path);
            }
            """;
        var generator = new AdhocProjectBuilder.ProjectDef(ProjectName).AddGeneratedSource(hint, generatedText);
        using var fixture = await CreateAsync(Supported + "\npublic partial string GeneratedRead(string path);",
            generator: generator, expectedHints: [hint]);
        var project = fixture.Solution.GetProject(fixture.ProjectId)!;
        var generated = (await project.GetSourceGeneratedDocumentsAsync()).ShouldHaveSingleItem();
        generated.Name.ShouldBe(hint);
        (await generated.GetTextAsync()).ToString().ShouldBe(generatedText);
        project.DocumentIds.ShouldNotContain(generated.Id);
        var generatedTree = (await generated.GetSyntaxTreeAsync()).ShouldNotBeNull();
        fixture.Compilation.SyntaxTrees.ShouldContain(tree => tree == generatedTree);
        var generatedRoot = (await generated.GetSyntaxRootAsync()).ShouldNotBeNull();
        var generatedModel = fixture.Compilation.GetSemanticModel(generatedTree);
        var generatedMethod = generatedModel.GetDeclaredSymbol(generatedRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single()).ShouldNotBeNull();
        generatedMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.GeneratedRead(System.String)");
        generatedMethod.PartialDefinitionPart.ShouldNotBeNull();
        generatedMethod.DeclaringSyntaxReferences.ShouldContain(reference => reference.SyntaxTree == generatedTree);
        var generatedCall = generatedRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var generatedOperation = generatedModel.GetOperation(generatedCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        SymbolEqualityComparer.Default.Equals(generatedOperation.TargetMethod, fixture.SourceMethod).ShouldBeTrue();
        var authored = fixture.Solution.GetDocument(fixture.DocumentId)!;
        var authoredCall = await BoundCallAsync(authored, "File.ReadAllText", fixture.SourceMethod);
        authored.FilePath.ShouldNotBe(generated.FilePath);

        var plan = await AssertBlockedAsync(fixture, "mapping.unsupported");

        plan.Sites.Select(site => site.Path).Distinct(StringComparer.Ordinal).Count().ShouldBeGreaterThanOrEqualTo(2);
        plan.Sites.ShouldContain(site => site.Path == authored.FilePath && Covers(site, authoredCall));
        plan.Sites.ShouldContain(site => site.Path == generated.FilePath && Covers(site, generatedCall)
            && site.TypeDocumentationId == ConsumerId && site.ProjectPath == ProjectPath
            && site.Disposition == StaticDependencySiteDisposition.Blocked && site.Code == "mapping.unsupported");
        (await project.GetSourceGeneratedDocumentsAsync()).ShouldHaveSingleItem().Name.ShouldBe(hint);
        (await generated.GetTextAsync()).ToString().ShouldBe(generatedText);
    }

    [Fact]
    public async Task stable_generated_helper_outside_the_selected_type_does_not_block_the_authored_call()
    {
        const string hint = "UnselectedHelper.g.cs";
        const string generatedText = """
            namespace Fixture;
            public static class GeneratedHelper
            {
                public static string Read(string path) => global::System.IO.File.ReadAllText(path);
            }
            """;
        var generator = new AdhocProjectBuilder.ProjectDef(ProjectName).AddGeneratedSource(hint, generatedText);
        using var fixture = await CreateAsync(generator: generator, expectedHints: [hint]);
        var originalProject = fixture.Solution.GetProject(fixture.ProjectId)!;
        originalProject.AnalyzerReferences.ShouldHaveSingleItem();
        var generated = (await originalProject.GetSourceGeneratedDocumentsAsync()).ShouldHaveSingleItem();
        generated.Name.ShouldBe(hint);
        (await generated.GetTextAsync()).ToString().ShouldBe(generatedText);
        var tree = (await generated.GetSyntaxTreeAsync()).ShouldNotBeNull();
        fixture.Compilation.SyntaxTrees.ShouldContain(candidate => candidate == tree);
        var helper = fixture.Compilation.GetTypeByMetadataName("Fixture.GeneratedHelper").ShouldNotBeNull();
        helper.GetDocumentationCommentId().ShouldNotBe(ConsumerId);
        helper.DeclaringSyntaxReferences.ShouldContain(reference => reference.SyntaxTree == tree);
        await BoundCallAsync(generated, "global::System.IO.File.ReadAllText", fixture.SourceMethod);

        var plan = await PrepareAsync(fixture);

        var candidate = await AssertPreparedAsync(fixture, plan,
            fixture.Source.Replace("File.ReadAllText(path)", "this._reader.ReadAllText(path)", StringComparison.Ordinal));
        plan.Sites.Count(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate).ShouldBe(1);
        // Reporting an out-of-scope generated site is optional; changing it is never permitted.
        plan.Sites.Where(site => site.Path == generated.FilePath)
            .ShouldAllBe(site => site.Disposition == StaticDependencySiteDisposition.Skipped);
        var candidateGenerated = (await candidate.GetProject(fixture.ProjectId)!.GetSourceGeneratedDocumentsAsync())
            .ShouldHaveSingleItem();
        candidateGenerated.Name.ShouldBe(hint);
        candidateGenerated.FilePath.ShouldBe(generated.FilePath);
        (await candidateGenerated.GetTextAsync()).ToString().ShouldBe(generatedText);
    }

    [Fact]
    public async Task authored_rewrite_that_changes_a_controlled_generated_output_blocks_the_whole_selection()
    {
        const string hint = "MappingState.g.cs";
        const string beforeGenerated = "namespace Fixture; internal static class GeneratedState { internal const string Phase = \"before\"; }";
        const string afterGenerated = "namespace Fixture; internal static class GeneratedState { internal const string Phase = \"after\"; internal static int Added => 7; }";
        var generator = new AdhocProjectBuilder.ProjectDef(ProjectName)
            .AddGeneratedSource(hint, "this._reader.ReadAllText", beforeGenerated, afterGenerated);
        using var fixture = await CreateAsync(
            "public string Read(string path) => File.ReadAllText(path) + GeneratedState.Phase;",
            generator: generator, expectedHints: [hint]);
        var originalReceipt = WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldNotBeNull();
        var originalGenerated = (await fixture.Solution.GetProject(fixture.ProjectId)!.GetSourceGeneratedDocumentsAsync())
            .ShouldHaveSingleItem();
        originalGenerated.Name.ShouldBe(hint);
        (await originalGenerated.GetTextAsync()).ToString().ShouldBe(beforeGenerated);
        await BoundCallAsync(fixture.Solution.GetDocument(fixture.DocumentId)!, "File.ReadAllText", fixture.SourceMethod);
        var expectedAuthored = fixture.Source.Replace("File.ReadAllText(path)", "this._reader.ReadAllText(path)", StringComparison.Ordinal);
        var independent = fixture.Solution.WithDocumentText(fixture.DocumentId, SourceText.From(expectedAuthored));
        var independentCompilation = await AssertCompilesAsync(independent);
        var independentlyGenerated = (await independent.GetProject(fixture.ProjectId)!.GetSourceGeneratedDocumentsAsync())
            .ShouldHaveSingleItem();
        independentlyGenerated.Name.ShouldBe(hint);
        independentlyGenerated.FilePath.ShouldBe(originalGenerated.FilePath);
        (await independentlyGenerated.GetTextAsync()).ToString().ShouldBe(afterGenerated);
        afterGenerated.ShouldNotBe(beforeGenerated);
        var independentTree = (await independentlyGenerated.GetSyntaxTreeAsync()).ShouldNotBeNull();
        independentCompilation.SyntaxTrees.ShouldContain(tree => tree == independentTree);
        independentCompilation.GetTypeByMetadataName("Fixture.GeneratedState").ShouldNotBeNull()
            .GetMembers("Added").ShouldHaveSingleItem();
        await BoundCallAsync(independent.GetDocument(fixture.DocumentId)!, "this._reader.ReadAllText",
            ResolveMethod(independentCompilation, TargetId));
        // This manual semantic witness is not a new acquisition. The original receipt stays authoritative.
        WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldBeSameAs(originalReceipt);
        (await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Complete);

        var plan = await AssertBlockedAsync(fixture, "mapping.generated-drift");

        plan.Sites.ShouldContain(site => site.Code == "mapping.generated-drift"
            && site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.ProjectPath == ProjectPath && site.Path == originalGenerated.FilePath
            && site.TypeDocumentationId == "T:Fixture.GeneratedState" && site.SpanLength == beforeGenerated.Length
            && site.Message.Contains(hint, StringComparison.Ordinal));
        WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldBeSameAs(originalReceipt);
        (await (await fixture.Solution.GetProject(fixture.ProjectId)!.GetSourceGeneratedDocumentsAsync())
            .ShouldHaveSingleItem().GetTextAsync()).ToString().ShouldBe(beforeGenerated);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task generated_document_addition_or_removal_blocks_the_complete_selection(bool adding)
    {
        const string alwaysHint = "Always.g.cs";
        const string optionalHint = "Optional.g.cs";
        const string optionalText = "#nullable enable";
        var generator = new AdhocProjectBuilder.ProjectDef(ProjectName)
            .AddGeneratedSource(alwaysHint, "namespace Fixture; internal static class Always { }")
            .AddConditionalGeneratedSource(optionalHint, "this._reader.ReadAllText", optionalText, adding);
        var beforeHints = adding ? new[] { alwaysHint } : [alwaysHint, optionalHint];
        var afterHints = adding ? new[] { alwaysHint, optionalHint } : [alwaysHint];
        using var fixture = await CreateAsync(generator: generator, expectedHints: beforeHints);
        var before = await fixture.Solution.GetProject(fixture.ProjectId)!.GetSourceGeneratedDocumentsAsync();
        before.Select(document => document.Name).Order(StringComparer.Ordinal).ShouldBe(beforeHints.Order(StringComparer.Ordinal));
        var independent = fixture.Solution.WithDocumentText(fixture.DocumentId, SourceText.From(
            fixture.Source.Replace("File.ReadAllText(path)", "this._reader.ReadAllText(path)", StringComparison.Ordinal)));
        await AssertCompilesAsync(independent);
        var after = await independent.GetProject(fixture.ProjectId)!.GetSourceGeneratedDocumentsAsync();
        after.Select(document => document.Name).Order(StringComparer.Ordinal).ShouldBe(afterHints.Order(StringComparer.Ordinal));
        var optional = (adding ? after : before).Single(document => document.Name == optionalHint);
        (await optional.GetTextAsync()).ToString().ShouldBe(optionalText);
        (await optional.GetSyntaxRootAsync()).ShouldNotBeNull().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ShouldBeEmpty();
        (await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Complete);

        var plan = await AssertBlockedAsync(fixture, "mapping.generated-drift");

        var site = plan.Sites.Single(candidate => candidate.Code == "mapping.generated-drift");
        site.Path.ShouldBe(optional.FilePath);
        site.TypeDocumentationId.ShouldBe("T:<generated>");
        site.SpanLength.ShouldBe(optionalText.Length);
        (await fixture.Solution.GetProject(fixture.ProjectId)!.GetSourceGeneratedDocumentsAsync())
            .Select(document => document.Name).Order(StringComparer.Ordinal).ShouldBe(beforeHints.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("nonstatic-source")]
    [InlineData("generic-source")]
    [InlineData("conversion-source")]
    [InlineData("operator-source")]
    [InlineData("static-target")]
    [InlineData("constructor-target")]
    [InlineData("ref-source")]
    [InlineData("ref-target")]
    [InlineData("return-mismatch")]
    [InlineData("parameter-count")]
    public async Task valid_declarations_outside_the_mapping_grammar_have_actionable_mapping_refusals(string kind)
    {
        var (declarations, members, sourceId, targetId, invalidSource) = kind switch
        {
            "nonstatic-source" => (Reader + " public class Source { public string ReadAllText(string path) => path; }",
                "public string Read(string path) => new Source().ReadAllText(path);",
                "M:Fixture.Source.ReadAllText(System.String)", TargetId, true),
            "generic-source" => (Reader + " public static class Source { public static string ReadAllText<T>(string path) => path; }",
                "public string Read(string path) => Source.ReadAllText<int>(path);",
                "M:Fixture.Source.ReadAllText``1(System.String)", TargetId, true),
            "conversion-source" => (Reader + " public readonly struct Source { public static implicit operator string(Source value) => \"path\"; }",
                "public string Read(Source path) => (string)path;",
                "M:Fixture.Source.op_Implicit(Fixture.Source)~System.String", TargetId, true),
            "operator-source" => (Reader + " public class Source { public static string operator +(Source left, Source right) => \"path\"; }",
                "public string Read(Source left, Source right) => left + right;",
                "M:Fixture.Source.op_Addition(Fixture.Source,Fixture.Source)", TargetId, true),
            "static-target" => ("public interface IReader { static string ReadAllText(string path) => path; }",
                Supported, SourceId, TargetId, false),
            "constructor-target" => ("public class IReader { public IReader(string path) { } }",
                Supported, SourceId, "M:Fixture.IReader.#ctor(System.String)", false),
            "ref-source" => (Reader + " public static class Source { private static string _value = \"value\"; public static ref string ReadAllText(string path) => ref _value; }",
                "public string Read(string path) => Source.ReadAllText(path);",
                "M:Fixture.Source.ReadAllText(System.String)", TargetId, true),
            "ref-target" => ("public interface IReader { ref string ReadAllText(string path); }",
                Supported, SourceId, TargetId, false),
            "return-mismatch" => ("public interface IReader { int ReadAllText(string path); }",
                Supported, SourceId, TargetId, false),
            "parameter-count" => ("public interface IReader { string ReadAllText(string path, int retries); }",
                Supported, SourceId, "M:Fixture.IReader.ReadAllText(System.String,System.Int32)", false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var fixture = await CreateAsync(members, declarations, sourceId, targetId);
        var source = fixture.SourceMethod;
        var target = fixture.TargetMethod;
        var document = fixture.Solution.GetDocument(fixture.DocumentId)!;
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        switch (kind)
        {
            case "nonstatic-source":
                source.MethodKind.ShouldBe(MethodKind.Ordinary);
                source.IsStatic.ShouldBeFalse();
                var instanceCall = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                var instanceOperation = model.GetOperation(instanceCall).ShouldBeAssignableTo<IInvocationOperation>()!;
                SymbolEqualityComparer.Default.Equals(instanceOperation.TargetMethod, source).ShouldBeTrue();
                instanceOperation.Instance.ShouldBeAssignableTo<IObjectCreationOperation>();
                break;
            case "generic-source":
                source.IsStatic.ShouldBeTrue();
                source.IsGenericMethod.ShouldBeTrue();
                source.Arity.ShouldBe(1);
                var genericCall = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
                var genericOperation = model.GetOperation(genericCall).ShouldBeAssignableTo<IInvocationOperation>()!;
                SymbolEqualityComparer.Default.Equals(genericOperation.TargetMethod.OriginalDefinition, source).ShouldBeTrue();
                genericOperation.TargetMethod.TypeArguments.ShouldHaveSingleItem().SpecialType.ShouldBe(SpecialType.System_Int32);
                break;
            case "conversion-source":
                source.MethodKind.ShouldBe(MethodKind.Conversion);
                source.IsStatic.ShouldBeTrue();
                var cast = root.DescendantNodes().OfType<CastExpressionSyntax>().Single();
                var conversion = model.GetOperation(cast).ShouldBeAssignableTo<IConversionOperation>()!;
                SymbolEqualityComparer.Default.Equals(conversion.OperatorMethod, source).ShouldBeTrue();
                break;
            case "operator-source":
                source.MethodKind.ShouldBe(MethodKind.UserDefinedOperator);
                source.IsStatic.ShouldBeTrue();
                var addition = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                    .Single(expression => expression.IsKind(SyntaxKind.AddExpression));
                var binary = model.GetOperation(addition).ShouldBeAssignableTo<IBinaryOperation>()!;
                SymbolEqualityComparer.Default.Equals(binary.OperatorMethod, source).ShouldBeTrue();
                break;
            case "static-target":
                target.MethodKind.ShouldBe(MethodKind.Ordinary);
                target.IsStatic.ShouldBeTrue();
                break;
            case "constructor-target":
                target.MethodKind.ShouldBe(MethodKind.Constructor);
                target.IsStatic.ShouldBeFalse();
                target.ContainingType.GetDocumentationCommentId().ShouldBe("T:Fixture.IReader");
                break;
            case "ref-source":
                source.ReturnsByRef.ShouldBeTrue();
                source.ReturnType.SpecialType.ShouldBe(SpecialType.System_String);
                target.ReturnsByRef.ShouldBeFalse();
                break;
            case "ref-target":
                target.ReturnsByRef.ShouldBeTrue();
                target.ReturnType.SpecialType.ShouldBe(SpecialType.System_String);
                source.ReturnsByRef.ShouldBeFalse();
                break;
            case "return-mismatch":
                source.ReturnType.SpecialType.ShouldBe(SpecialType.System_String);
                target.ReturnType.SpecialType.ShouldBe(SpecialType.System_Int32);
                SymbolEqualityComparer.Default.Equals(source.ReturnType, target.ReturnType).ShouldBeFalse();
                break;
            case "parameter-count":
                source.Parameters.Length.ShouldBe(1);
                target.Parameters.Length.ShouldBe(2);
                target.Parameters[1].Type.SpecialType.ShouldBe(SpecialType.System_Int32);
                target.Parameters[1].IsOptional.ShouldBeFalse();
                target.Parameters[1].IsParams.ShouldBeFalse();
                break;
        }
        var suppliedSymbol = invalidSource ? fixture.Request.Mapping.Source : fixture.Request.Mapping.Target;
        var actualSymbol = invalidSource ? source : target;
        suppliedSymbol.DocumentationId.ShouldBe(actualSymbol.GetDocumentationCommentId());
        suppliedSymbol.AssemblyIdentity.ShouldBe(actualSymbol.ContainingAssembly.Identity.ToString());

        var plan = await AssertBlockedAsync(fixture, "selection.mapping");

        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.Code == "selection.mapping" && site.ProjectPath == ProjectPath
            && site.TypeDocumentationId == ConsumerId
            && site.Message.Contains(suppliedSymbol.DocumentationId, StringComparison.Ordinal));
        string[] reasonParts = kind switch
        {
            "nonstatic-source" => ["static"],
            "generic-source" => ["non-generic"],
            "conversion-source" or "operator-source" => ["ordinary"],
            "static-target" => ["instance"],
            "constructor-target" => ["ordinary"],
            "ref-source" or "ref-target" => ["ref return"],
            "return-mismatch" => ["return", "types"],
            "parameter-count" => ["parameter", "counts"],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        foreach (var part in reasonParts)
            plan.Sites.ShouldContain(site => site.Code == "selection.mapping"
                && site.Message.Contains(part, StringComparison.OrdinalIgnoreCase),
                "the refusal must explain the invalid declaration, as well as identify its symbol");
    }

    [Fact]
    public async Task supported_argument_preserves_user_operators_construction_and_member_reads_in_order()
    {
        const string sourceId = "M:System.IO.File.ReadAllText(System.String,System.Text.Encoding)";
        const string targetId = "M:Fixture.IReader.ReadAllText(System.String,System.Text.Encoding)";
        const string declarations = """
            public interface IReader { string ReadAllText(string path, Encoding encoding); }
            public readonly struct PathInput
            {
                public PathInput(string value) { }
                public static PathInput operator +(PathInput value) => value;
                public static implicit operator string(PathInput value) => "path";
            }
            public sealed class PathBox
            {
                public PathBox(string value) { Value = new PathInput(value); }
                public PathInput Value { get; }
            }
            """;
        using var fixture = await CreateAsync(
            "public string Read(string input) => File.ReadAllText(encoding: Encoding.Unicode, path: /* retain */ +new PathBox(input).Value);",
            declarations, sourceId, targetId);
        var originalDocument = fixture.Solution.GetDocument(fixture.DocumentId)!;
        var originalCall = await BoundCallAsync(originalDocument, "File.ReadAllText", fixture.SourceMethod);
        var originalModel = (await originalDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        var originalOperation = originalModel.GetOperation(originalCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        AssertArgumentEvidence(originalOperation);

        var plan = await PrepareAsync(fixture);

        var candidate = await AssertPreparedAsync(fixture, plan,
            fixture.Source.Replace("File.ReadAllText(", "this._reader.ReadAllText(", StringComparison.Ordinal));
        var candidateCompilation = (await candidate.GetProject(fixture.ProjectId)!.GetCompilationAsync()).ShouldNotBeNull();
        var candidateDocument = candidate.GetDocument(fixture.DocumentId)!;
        var candidateCall = await BoundCallAsync(candidateDocument, "this._reader.ReadAllText",
            ResolveMethod(candidateCompilation, targetId));
        candidateCall.ArgumentList.ToFullString().ShouldBe(originalCall.ArgumentList.ToFullString());
        candidateCall.ArgumentList.Arguments.Select(argument => argument.ToFullString())
            .ShouldBe(originalCall.ArgumentList.Arguments.Select(argument => argument.ToFullString()));
        var candidateModel = (await candidateDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        var candidateOperation = candidateModel.GetOperation(candidateCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        candidateOperation.TargetMethod.ContainingAssembly.Identity.ToString().ShouldBe(fixture.Request.Mapping.Target.AssemblyIdentity);
        var field = candidateOperation.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        field.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._reader");
        field.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
        AssertArgumentEvidence(candidateOperation);

        static void AssertArgumentEvidence(IInvocationOperation operation)
        {
            operation.Arguments.Select(argument => argument.Parameter!.Ordinal).ShouldBe([1, 0]);
            operation.Arguments.Select(argument => argument.Parameter!.Name).ShouldBe(["encoding", "path"]);
            operation.Arguments[0].InConversion.IsIdentity.ShouldBeTrue();
            operation.Arguments[0].Value.ShouldBeAssignableTo<IPropertyReferenceOperation>()!
                .Property.GetDocumentationCommentId().ShouldBe("P:System.Text.Encoding.Unicode");
            var conversion = operation.Arguments[1].Value.ShouldBeAssignableTo<IConversionOperation>()!;
            conversion.Type!.SpecialType.ShouldBe(SpecialType.System_String);
            conversion.OperatorMethod.ShouldNotBeNull().GetDocumentationCommentId()
                .ShouldBe("M:Fixture.PathInput.op_Implicit(Fixture.PathInput)~System.String");
            var unary = conversion.Operand.ShouldBeAssignableTo<IUnaryOperation>()!;
            unary.OperatorMethod.ShouldNotBeNull().GetDocumentationCommentId()
                .ShouldBe("M:Fixture.PathInput.op_UnaryPlus(Fixture.PathInput)");
            var property = unary.Operand.ShouldBeAssignableTo<IPropertyReferenceOperation>()!;
            property.Property.GetDocumentationCommentId().ShouldBe("P:Fixture.PathBox.Value");
            var creation = property.Instance.ShouldBeAssignableTo<IObjectCreationOperation>()!;
            creation.Constructor.ShouldNotBeNull().GetDocumentationCommentId().ShouldBe("M:Fixture.PathBox.#ctor(System.String)");
            var constructorArgument = creation.Arguments.ShouldHaveSingleItem();
            constructorArgument.Parameter!.Ordinal.ShouldBe(0);
            constructorArgument.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter.Name.ShouldBe("input");
        }
    }

    [Fact]
    public async Task nested_selected_invocations_are_explicitly_refused_without_a_partial_argument_rewrite()
    {
        using var fixture = await CreateAsync("public string Read(string path) => File.ReadAllText(File.ReadAllText(path));");
        var document = fixture.Solution.GetDocument(fixture.DocumentId)!;
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        calls.Length.ShouldBe(2);
        foreach (var call in calls)
        {
            var operation = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
            SymbolEqualityComparer.Default.Equals(operation.TargetMethod, fixture.SourceMethod).ShouldBeTrue();
        }
        var outer = model.GetOperation(calls[0]).ShouldBeAssignableTo<IInvocationOperation>()!;
        outer.Arguments.ShouldHaveSingleItem().Value.ShouldBeAssignableTo<IInvocationOperation>()!
            .Syntax.ShouldBe(calls[1]);

        var plan = await AssertBlockedAsync(fixture, "mapping.unsupported");

        foreach (var call in calls)
            plan.Sites.ShouldContain(site => site.Path == DocumentPath && site.TypeDocumentationId == ConsumerId
                && site.SpanStart == call.SpanStart && site.SpanLength == call.Span.Length);
        plan.Sites.ShouldContain(site => site.Path == DocumentPath && Covers(site, calls[0])
            && site.Code == "mapping.unsupported" && site.Disposition == StaticDependencySiteDisposition.Blocked);
    }

    private static bool Covers(StaticDependencySite site, SyntaxNode node) =>
        site.SpanLength > 0 && site.SpanStart <= node.SpanStart && site.SpanStart + site.SpanLength >= node.Span.End;

    private static async Task<InvocationExpressionSyntax> BoundCallAsync(Document document, string expression, IMethodSymbol expected)
    {
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var call = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(node => node.Expression.ToString() == expression);
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var operation = model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!;
        SymbolEqualityComparer.Default.Equals(operation.TargetMethod, expected).ShouldBeTrue();
        return call;
    }

    private static async Task<StaticDependencyPlan> PrepareAsync(Fixture fixture)
    {
        var receipt = WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldNotBeNull();
        StaticDependencyPlan? plan = null;
        var exception = await Record.ExceptionAsync(async () =>
            plan = await new StaticDependencyMappingService().PrepareAsync(fixture.Solution, fixture.Request));
        exception.ShouldBeNull("existing-reader planning must return a complete candidate or an actionable blocked plan");
        WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldBeSameAs(receipt);
        return plan.ShouldNotBeNull();
    }

    private static async Task<StaticDependencyPlan> AssertBlockedAsync(Fixture fixture, string code)
    {
        var receipt = WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldNotBeNull();
        var plan = await PrepareAsync(fixture);
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        plan.Request.ShouldBe(fixture.Request);
        plan.SourceFingerprint.ShouldBe(fixture.Request.ExpectedSourceFingerprint);
        plan.Documents.ShouldBeEmpty("whole-selection refusal must not expose a partial diff");
        plan.Sites.ShouldNotBeEmpty();
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate);
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked && site.Code == code);
        plan.Sites.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(plan.Sites.Count);
        foreach (var site in plan.Sites)
        {
            site.Id.ShouldNotBeNullOrWhiteSpace();
            site.Code.ShouldNotBeNullOrWhiteSpace();
            site.Message.ShouldNotBeNullOrWhiteSpace();
            site.ProjectPath.ShouldNotBeNullOrWhiteSpace();
            site.TypeDocumentationId.ShouldNotBeNullOrWhiteSpace();
            site.Path.ShouldNotBeNullOrWhiteSpace();
        }
        AssertNoFileEffects(plan.Changes);
        plan.Changes.HasChanges.ShouldBeFalse();
        plan.Changes.OriginalSolution.ShouldBeSameAs(fixture.Solution);
        plan.Changes.AffectedPaths.ShouldBeEmpty();
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        candidate.GetChanges(fixture.Solution).GetAddedProjects().ShouldBeEmpty();
        candidate.GetChanges(fixture.Solution).GetRemovedProjects().ShouldBeEmpty();
        candidate.GetChanges(fixture.Solution).GetProjectChanges().ShouldBeEmpty();
        var repeat = await PrepareAsync(fixture);
        repeat.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        repeat.Sites.ShouldBe(plan.Sites);
        repeat.Documents.ShouldBeEmpty();
        repeat.Changes.HasChanges.ShouldBeFalse();
        WorkspaceAcquisitionRegistry.Get(fixture.Workspace).Receipt.ShouldBeSameAs(receipt);
        await AssertUnchangedAsync(fixture);
        return plan;
    }

    private static async Task<Solution> AssertPreparedAsync(Fixture fixture, StaticDependencyPlan plan, string expected)
    {
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        plan.Request.ShouldBe(fixture.Request);
        plan.SourceFingerprint.ShouldBe(fixture.Request.ExpectedSourceFingerprint);
        plan.MappingFingerprint.ShouldNotBeNullOrWhiteSpace();
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
        plan.Documents.ShouldBe([new StaticDependencyDocumentChange(DocumentPath, fixture.Source, expected)]);
        plan.Changes.HasChanges.ShouldBeTrue();
        plan.Changes.OriginalSolution.ShouldBeSameAs(fixture.Solution);
        plan.Changes.AffectedPaths.ShouldBe([DocumentPath]);
        AssertNoFileEffects(plan.Changes);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        (await candidate.GetDocument(fixture.DocumentId)!.GetTextAsync()).ToString().ShouldBe(expected);
        var delta = candidate.GetChanges(fixture.Solution);
        delta.GetAddedProjects().ShouldBeEmpty();
        delta.GetRemovedProjects().ShouldBeEmpty();
        var projectChange = delta.GetProjectChanges().ShouldHaveSingleItem();
        projectChange.GetChangedDocuments().ShouldBe([fixture.DocumentId]);
        projectChange.GetAddedDocuments().ShouldBeEmpty();
        projectChange.GetRemovedDocuments().ShouldBeEmpty();
        await AssertCompilesAsync(candidate);
        await AssertUnchangedAsync(fixture);
        return candidate;
    }

    private static void AssertNoFileEffects(PreparedChangeSet changes)
    {
        changes.NewFiles.ShouldBeEmpty();
        changes.DeletedFiles.ShouldBeEmpty();
        changes.MovedFiles.ShouldBeEmpty();
        changes.ProjectFileEdits.ShouldBeEmpty();
        changes.TextEdits.ShouldBeEmpty();
    }

    private static async Task AssertUnchangedAsync(Fixture fixture)
    {
        fixture.Workspace.CurrentSolution.ShouldBeSameAs(fixture.Solution);
        (await fixture.Solution.GetDocument(fixture.DocumentId)!.GetTextAsync()).ToString().ShouldBe(fixture.Source);
        (await SolutionRevision.ComputeAsync(fixture.Solution)).ShouldBe(fixture.Request.ExpectedSourceFingerprint);
    }

    private static async Task<Compilation> AssertCompilesAsync(Solution solution)
    {
        var compilation = (await solution.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty("the independent original or manually transformed fixture must compile");
        return compilation;
    }

    private static IMethodSymbol ResolveMethod(Compilation compilation, string documentationId)
    {
        var method = DocumentationCommentId.GetSymbolsForDeclarationId(documentationId, compilation)
            .OfType<IMethodSymbol>().ShouldHaveSingleItem();
        method.GetDocumentationCommentId().ShouldBe(documentationId);
        return method;
    }

    private static async Task<Fixture> CreateAsync(string members = Supported, string declarations = Reader,
        string sourceId = SourceId, string targetId = TargetId,
        AdhocProjectBuilder.ProjectDef? generator = null, string[]? expectedHints = null)
    {
        var source = $$"""
            using System;
            using System.IO;
            using System.Text;
            namespace Fixture;
            {{declarations}}
            public partial class Consumer
            {
                private readonly IReader _reader;
                public Consumer(IReader reader) { _reader = reader ?? throw new ArgumentNullException("reader"); }
                {{members}}
            }
            """;
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var references = new[]
        {
            typeof(object).Assembly.Location,
            typeof(File).Assembly.Location,
            typeof(Encoding).Assembly.Location,
            Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")
        }.Distinct(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path));
        workspace.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), ProjectName, ProjectName, LanguageNames.CSharp,
            filePath: ProjectPath, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12), metadataReferences: references,
            analyzerReferences: generator is null ? [] : generator.CreateAnalyzerReferences()));
        workspace.AddDocument(DocumentInfo.Create(documentId, "Consumer.cs",
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())), filePath: DocumentPath));
        var solution = workspace.CurrentSolution;
        var compilation = await AssertCompilesAsync(solution);
        var sourceMethod = ResolveMethod(compilation, sourceId);
        var targetMethod = ResolveMethod(compilation, targetId);
        await ExplicitWorkspaceAcquisition.AttachAsync(solution, [ProjectName],
            new Dictionary<string, string[]> { [ProjectName] = expectedHints ?? [] });
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
        completeness.State.ShouldBe(WorkspaceCompletenessState.Complete,
            "the declared authored/generated fixture must be fully acquired: " + string.Join(Environment.NewLine, completeness.Diagnostics));
        var mapping = new StaticDependencyMethodMapping("rt.static-dependency.mapping.v1",
            new StaticDependencySymbol(sourceId, sourceMethod.ContainingAssembly.Identity.ToString()),
            "F:Fixture.Consumer._reader",
            new StaticDependencySymbol(targetId, targetMethod.ContainingAssembly.Identity.ToString()),
            new StaticDependencyPreconditions(
                "Caller declares the constructor stores the existing field before use.",
                "Caller declares the reader lifetime covers the selected methods.",
                "Caller declares the constructor guard establishes a non-null reader.",
                "Caller declares adapter exception equivalence.",
                "Caller declares adapter encoding equivalence.",
                "Caller declares adapter cancellation equivalence.",
                "Caller declares adapter resource-ownership equivalence.",
                ["Controlled in-process semantic fixture; caller assumptions are not an arbitrary adapter-equivalence proof."]));
        var request = new StaticDependencyMappingRequest(mapping, new StaticDependencySelection(ProjectPath, ConsumerId),
            await SolutionRevision.ComputeAsync(solution));
        return new Fixture(workspace, solution, projectId, documentId, source, compilation, sourceMethod, targetMethod, request);
    }

    private sealed record Fixture(AdhocWorkspace Workspace, Solution Solution, ProjectId ProjectId, DocumentId DocumentId,
        string Source, Compilation Compilation, IMethodSymbol SourceMethod, IMethodSymbol TargetMethod,
        StaticDependencyMappingRequest Request) : IDisposable
    {
        public void Dispose() => Workspace.Dispose();
    }
}
