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

public sealed class StaticDependencyMappingSafetyTests
{
    private const string ProjectName = "MappingSafety";
    private static readonly string ProjectPath = Path.GetFullPath("/virtual/rt153-safety/MappingSafety.csproj");
    private const string ConsumerId = "T:Fixture.Consumer";
    private const string SourceId = "M:System.IO.File.ReadAllText(System.String)";
    private const string TargetId = "M:Fixture.IReader.ReadAllText(System.String)";
    private const string Reader = "public interface IReader { string ReadAllText(string path); }";
    private const string Supported = "public string Read(string path) => File.ReadAllText(path);";

    [Fact]
    public async Task identical_argument_text_and_types_do_not_authorize_contextual_handler_constructor_rebinding()
    {
        const string sourceId = "M:Fixture.Source.ReadAllText(Fixture.Handler)";
        const string targetId = "M:Fixture.IReader.ReadAllText(Fixture.Handler)";
        const string declarations = """
            public interface IReader
            {
                string ReadAllText([System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("")] Handler value);
            }
            [System.Runtime.CompilerServices.InterpolatedStringHandler]
            public ref struct Handler
            {
                public Handler(int literalLength, int formattedCount) { }
                public Handler(int literalLength, int formattedCount, IReader receiver) { }
                public void AppendLiteral(string text) { }
            }
            public static class Source { public static string ReadAllText(Handler value) => "value"; }
            """;
        using var fixture = await CreateAsync("public string Read() => Source.ReadAllText($\"hello\");",
            declarations, sourceId, targetId);
        var originalDocument = fixture.Solution.GetDocument(fixture.DocumentId)!;
        var candidate = fixture.Solution.WithDocumentText(fixture.DocumentId, SourceText.From(
            fixture.Source.Replace("Source.ReadAllText(", "this._reader.ReadAllText(", StringComparison.Ordinal)));
        await AssertCompilesAsync(candidate);
        var before = await GetCallAsync(originalDocument);
        var after = await GetCallAsync(candidate.GetDocument(fixture.DocumentId)!);
        before.TargetMethod.GetDocumentationCommentId().ShouldBe(sourceId);
        after.TargetMethod.GetDocumentationCommentId().ShouldBe(targetId);
        var beforeArgument = before.Arguments.ShouldHaveSingleItem();
        var afterArgument = after.Arguments.ShouldHaveSingleItem();
        beforeArgument.Parameter!.Type.GetDocumentationCommentId().ShouldBe("T:Fixture.Handler");
        afterArgument.Parameter!.Type.GetDocumentationCommentId().ShouldBe("T:Fixture.Handler");
        afterArgument.Syntax.ToFullString().ShouldBe(beforeArgument.Syntax.ToFullString());
        beforeArgument.Value.DescendantsAndSelf().OfType<IObjectCreationOperation>().ShouldHaveSingleItem()
            .Constructor!.GetDocumentationCommentId().ShouldBe("M:Fixture.Handler.#ctor(System.Int32,System.Int32)");
        afterArgument.Value.DescendantsAndSelf().OfType<IObjectCreationOperation>().ShouldHaveSingleItem()
            .Constructor!.GetDocumentationCommentId().ShouldBe("M:Fixture.Handler.#ctor(System.Int32,System.Int32,Fixture.IReader)");

        var plan = await AssertBlockedAsync(fixture.Solution, fixture.Request, "binding", "argument");

        plan.Sites.ShouldContain(site => site.Code == "mapping.binding"
            && site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.TypeDocumentationId == ConsumerId && site.SpanLength > 0);

        static async Task<IInvocationOperation> GetCallAsync(Document document)
        {
            var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
            var call = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ShouldHaveSingleItem();
            return (await document.GetSemanticModelAsync()).ShouldNotBeNull().GetOperation(call)
                .ShouldBeAssignableTo<IInvocationOperation>()!;
        }
    }

    [Fact]
    public async Task preserves_named_argument_order_conversions_trivia_and_the_qualified_receiver()
    {
        const string sourceId = "M:System.IO.File.ReadAllText(System.String,System.Text.Encoding)";
        const string targetId = "M:Fixture.IReader.ReadAllText(System.String,System.Text.Encoding)";
        const string members = """
            private PathInput NextPath() => new PathInput();
            private Encoding NextEncoding() => Encoding.Unicode;
            public string Read()
            {
                var _reader = "local shadow";
                return File.ReadAllText(encoding: NextEncoding(), path: /* retain */ NextPath()) + _reader;
            }
            """;
        using var fixture = await CreateAsync(members,
            "public interface IReader { string ReadAllText(string path, Encoding encoding); }",
            sourceId: sourceId, targetId: targetId);
        var document = fixture.Solution.GetDocument(fixture.DocumentId).ShouldNotBeNull();
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var originalCall = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression.ToString() == "File.ReadAllText");
        var originalOperation = model.GetOperation(originalCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        originalOperation.TargetMethod.GetDocumentationCommentId().ShouldBe(sourceId);
        AssertArgumentBindings(originalOperation);

        var plan = await PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        var expected = fixture.Source.Replace("File.ReadAllText(", "this._reader.ReadAllText(", StringComparison.Ordinal);
        await AssertCandidateAsync(fixture, plan, new Dictionary<DocumentId, string> { [fixture.DocumentId] = expected });
        var candidateDocument = plan.Changes.ChangedSolution!.GetDocument(fixture.DocumentId)!;
        var candidateRoot = (await candidateDocument.GetSyntaxRootAsync()).ShouldNotBeNull();
        var candidateModel = (await candidateDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        var candidateCall = candidateRoot.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression.ToString() == "this._reader.ReadAllText");
        candidateCall.ArgumentList.ToFullString().ShouldBe(originalCall.ArgumentList.ToFullString());
        var candidateOperation = candidateModel.GetOperation(candidateCall).ShouldBeAssignableTo<IInvocationOperation>()!;
        candidateOperation.TargetMethod.GetDocumentationCommentId().ShouldBe(targetId);
        candidateOperation.TargetMethod.ContainingAssembly.Identity.ToString().ShouldBe(fixture.Request.Mapping.Target.AssemblyIdentity);
        var field = candidateOperation.Instance.ShouldBeAssignableTo<IFieldReferenceOperation>()!;
        field.Field.GetDocumentationCommentId().ShouldBe("F:Fixture.Consumer._reader");
        field.Instance.ShouldBeAssignableTo<IInstanceReferenceOperation>();
        AssertArgumentBindings(candidateOperation);

        // Exact expression text and ordered operations prove the proposed edit neither duplicates
        // nor reorders the two evaluations. This does not claim adapter behavioral equivalence.
        candidateCall.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression.ToString()).ShouldBe(["NextEncoding", "NextPath"]);

        static void AssertArgumentBindings(IInvocationOperation operation)
        {
            operation.Arguments.Select(argument => argument.Parameter!.Ordinal).ShouldBe([1, 0]);
            operation.Arguments.Select(argument => argument.Parameter!.Name).ShouldBe(["encoding", "path"]);
            operation.Arguments[0].Value.ShouldBeAssignableTo<IInvocationOperation>()!
                .TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.NextEncoding");
            var conversion = operation.Arguments[1].Value.ShouldBeAssignableTo<IConversionOperation>()!;
            conversion.Type!.SpecialType.ShouldBe(SpecialType.System_String);
            conversion.OperatorMethod.ShouldNotBeNull().GetDocumentationCommentId()
                .ShouldBe("M:Fixture.PathInput.op_Implicit(Fixture.PathInput)~System.String");
            conversion.Operand.ShouldBeAssignableTo<IInvocationOperation>()!
                .TargetMethod.GetDocumentationCommentId().ShouldBe("M:Fixture.Consumer.NextPath");
        }
    }

    [Fact]
    public async Task inventories_all_partials_and_skips_other_types_and_projects_by_exact_selection()
    {
        const string partial = """
            using System.IO;
            namespace Fixture;
            public partial class Consumer { public string Second(string path) => File.ReadAllText(path); }
            public sealed class Other { public string Read(string path) => File.ReadAllText(path); }
            """;
        using var fixture = await CreateAsync(Supported, partialSource: partial, includePeer: true);
        var secondId = fixture.Solution.GetProject(fixture.ProjectId)!.DocumentIds.Single(id => id != fixture.DocumentId);
        var peer = fixture.Solution.Projects.Single(project => project.Name == "Peer");
        var plan = await PrepareAsync(fixture.Solution, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        var expectedPartial = partial.Replace("Second(string path) => File.ReadAllText(path)",
            "Second(string path) => this._reader.ReadAllText(path)", StringComparison.Ordinal);
        await AssertCandidateAsync(fixture, plan, new Dictionary<DocumentId, string>
        {
            [fixture.DocumentId] = fixture.Source.Replace("File.ReadAllText(path)", "this._reader.ReadAllText(path)", StringComparison.Ordinal),
            [secondId] = expectedPartial
        });
        var converted = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate).ToArray();
        converted.Length.ShouldBe(2);
        converted.ShouldAllBe(site => site.ProjectPath == ProjectPath && site.TypeDocumentationId == ConsumerId);
        plan.Sites.ShouldContain(site => site.TypeDocumentationId == "T:Fixture.Other"
            && site.Disposition == StaticDependencySiteDisposition.Skipped);
        plan.Sites.ShouldContain(site => site.ProjectPath == peer.FilePath
            && site.TypeDocumentationId == ConsumerId && site.Disposition == StaticDependencySiteDisposition.Skipped);
        foreach (var document in peer.Documents)
        {
            (await plan.Changes.ChangedSolution!.GetDocument(document.Id)!.GetTextAsync()).ToString()
                .ShouldBe((await document.GetTextAsync()).ToString());
        }
        AssertDispositionIdentities(plan);
        var reverseSelection = fixture.Request with
        {
            Selection = fixture.Request.Selection with { SiteIds = converted.Reverse().Select(site => site.Id).ToArray() }
        };
        var reversed = await PrepareAsync(fixture.Solution, reverseSelection);
        reversed.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        reversed.Documents.ShouldBe(plan.Documents);
        reversed.Sites.ShouldBe(plan.Sites);
    }

    [Theory]
    [InlineData("public static string Read(string path) => File.ReadAllText(path);", "static")]
    [InlineData("public Func<string> Read(string path) => () => File.ReadAllText(path);", "lambda")]
    [InlineData("private readonly string _initial = File.ReadAllText(\"initial\");", "initializer")]
    [InlineData("public Expression<Func<string>> Read(string path) => () => File.ReadAllText(path);", "expression")]
    [InlineData("public Func<string, string> Read() => File.ReadAllText;", "method")]
    public async Task selected_unsupported_context_blocks_the_complete_selection(string member, string reason)
    {
        using var fixture = await CreateAsync(member);
        var root = (await fixture.Solution.GetDocument(fixture.DocumentId)!.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await fixture.Solution.GetDocument(fixture.DocumentId)!.GetSemanticModelAsync()).ShouldNotBeNull();
        var sourceReference = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Single(access => access.ToString() == "File.ReadAllText");
        model.GetSymbolInfo(sourceReference).Symbol.ShouldBeAssignableTo<IMethodSymbol>()!
            .GetDocumentationCommentId().ShouldBe(SourceId);

        var plan = await AssertBlockedAsync(fixture.Solution, fixture.Request, reason);

        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.Path == fixture.DocumentPath && site.SpanLength > 0
            && site.SpanStart <= sourceReference.SpanStart
            && site.SpanStart + site.SpanLength >= sourceReference.Span.End);
        await AssertWorkspaceUnchangedAsync(fixture);
    }

    [Fact]
    public async Task mixed_selection_releases_no_candidate_but_explicit_supported_site_can_be_prepared()
    {
        const string members = """
            public string Good(string path) => File.ReadAllText(path);
            public static string Blocked(string path) => File.ReadAllText(path);
            """;
        using var fixture = await CreateAsync(members);
        var root = (await fixture.Solution.GetDocument(fixture.DocumentId)!.GetSyntaxRootAsync()).ShouldNotBeNull();
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        calls.Length.ShouldBe(2);
        var blocked = await AssertBlockedAsync(fixture.Solution, fixture.Request, "static");
        blocked.Sites.ShouldContain(site => site.SpanStart == calls[1].SpanStart
            && site.Disposition == StaticDependencySiteDisposition.Blocked);
        var supportedSite = blocked.Sites.Single(site => site.Path == fixture.DocumentPath && site.SpanStart == calls[0].SpanStart);
        var narrowRequest = fixture.Request with { Selection = fixture.Request.Selection with { SiteIds = [supportedSite.Id] } };

        var narrow = await PrepareAsync(fixture.Solution, narrowRequest);

        narrow.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        narrow.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate)
            .Select(site => site.Id).ShouldBe([supportedSite.Id]);
        narrow.Sites.ShouldContain(site => site.SpanStart == calls[1].SpanStart
            && site.Disposition == StaticDependencySiteDisposition.Skipped);
        var expected = fixture.Source.Replace("Good(string path) => File.ReadAllText(path)",
            "Good(string path) => this._reader.ReadAllText(path)", StringComparison.Ordinal);
        await AssertCandidateAsync(fixture, narrow, new Dictionary<DocumentId, string> { [fixture.DocumentId] = expected });
    }

    [Theory]
    [InlineData("ref string path", "ref path", "System.String@", "ref")]
    [InlineData("params string[] path", "path, path", "System.String[]", "params")]
    [InlineData("string path = \"default\"", "", "System.String", "optional")]
    public async Task unsupported_parameter_shapes_are_not_silently_adapted(
        string parameter, string arguments, string parameterId, string reason)
    {
        var sourceId = $"M:Fixture.Source.ReadAllText({parameterId})";
        var targetId = $"M:Fixture.IReader.ReadAllText({parameterId})";
        var declarations = $"public interface IReader {{ string ReadAllText({parameter}); }} "
            + $"public static class Source {{ public static string ReadAllText({parameter}) => \"value\"; }}";
        using var fixture = await CreateAsync($"public string Read(string path) => Source.ReadAllText({arguments});",
            declarations, sourceId: sourceId, targetId: targetId);
        var method = ResolveMethod(fixture.Compilation, sourceId);
        if (reason == "ref")
            method.Parameters[0].RefKind.ShouldBe(RefKind.Ref);
        else if (reason == "params")
            method.Parameters[0].IsParams.ShouldBeTrue();
        else
            method.Parameters[0].IsOptional.ShouldBeTrue();

        await AssertBlockedAsync(fixture.Solution, fixture.Request, reason);
        await AssertWorkspaceUnchangedAsync(fixture);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("type")]
    [InlineData("site")]
    [InlineData("ambiguous")]
    public async Task invalid_exact_selection_has_an_explicit_selection_disposition(string kind)
    {
        using var fixture = await CreateAsync(Supported,
            includePeer: kind == "ambiguous", peerUsesSelectedPath: kind == "ambiguous");
        if (kind == "ambiguous")
            fixture.Solution.Projects.Count(project => project.FilePath == ProjectPath).ShouldBe(2);
        var selection = kind switch
        {
            "project" => fixture.Request.Selection with { ProjectPath = ProjectPath + ".missing" },
            "type" => fixture.Request.Selection with { TypeDocumentationId = "T:Fixture.Absent" },
            "site" => fixture.Request.Selection with { SiteIds = ["site:does-not-exist"] },
            _ => fixture.Request.Selection
        };
        var request = fixture.Request with { Selection = selection };

        var plan = await AssertBlockedAsync(fixture.Solution, request, kind);

        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.ProjectPath == selection.ProjectPath && site.TypeDocumentationId == selection.TypeDocumentationId
            && site.SpanLength == 0 && site.Code.Contains("selection", StringComparison.OrdinalIgnoreCase));
        if (kind is "project" or "ambiguous")
        {
            var refusal = plan.Sites.ShouldHaveSingleItem();
            refusal.Code.ShouldBe("selection.project");
            refusal.Message.ShouldContain(selection.ProjectPath);
            refusal.Message.ShouldContain(kind == "project" ? "not loaded" : "ambiguous");
            if (kind == "project")
                refusal.Message.ShouldNotContain("ambiguous");
        }
    }

    [Theory]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("target", false)]
    [InlineData("target", true)]
    public async Task unresolved_or_wrong_assembly_mapping_rejects_with_the_affected_identity(string role, bool wrongAssembly)
    {
        using var fixture = await CreateAsync(Supported);
        var mapping = fixture.Request.Mapping;
        var originalSymbol = role == "source" ? mapping.Source : mapping.Target;
        var invalid = wrongAssembly
            ? originalSymbol with { AssemblyIdentity = "Absent.Assembly, Version=9.8.7.6, Culture=neutral, PublicKeyToken=null" }
            : originalSymbol with { DocumentationId = "M:Fixture.Absent.ReadAllText(System.String)" };
        var request = fixture.Request with
        {
            Mapping = role == "source" ? mapping with { Source = invalid } : mapping with { Target = invalid }
        };

        var plan = await AssertBlockedAsync(fixture.Solution, request, role);

        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.Message.Contains(wrongAssembly ? invalid.AssemblyIdentity : invalid.DocumentationId, StringComparison.Ordinal));
        if (wrongAssembly)
            plan.Sites.ShouldContain(site => site.Message.Contains("assembly", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("receiver")]
    [InlineData("availability")]
    [InlineData("lifetime")]
    [InlineData("non-null")]
    [InlineData("exceptions")]
    [InlineData("encoding")]
    [InlineData("cancellation")]
    [InlineData("ownership")]
    [InlineData("evidence")]
    [InlineData("evidence-mixed")]
    public async Task invalid_structural_or_declared_preconditions_block_with_distinct_actionable_reasons(string kind)
    {
        using var fixture = await CreateAsync(Supported);
        var mapping = fixture.Request.Mapping;
        var preconditions = mapping.Preconditions;
        mapping = kind switch
        {
            "schema" => mapping with { SchemaVersion = "rt.static-dependency.mapping.unknown" },
            "receiver" => mapping with { ReceiverFieldDocumentationId = "F:Fixture.Consumer.absent" },
            _ => mapping with
            {
                Preconditions = kind switch
                {
                    "availability" => preconditions with { ReceiverAvailability = " " },
                    "lifetime" => preconditions with { ReceiverLifetime = " " },
                    "non-null" => preconditions with { ReceiverNonNull = " " },
                    "exceptions" => preconditions with { AdapterExceptions = " " },
                    "encoding" => preconditions with { AdapterEncoding = " " },
                    "cancellation" => preconditions with { AdapterCancellation = " " },
                    "ownership" => preconditions with { AdapterResourceOwnership = " " },
                    "evidence-mixed" => preconditions with { ContractEvidence = ["actual evidence", " "] },
                    _ => preconditions with { ContractEvidence = [] }
                }
            }
        };

        if (kind == "evidence-mixed")
        {
            mapping.Preconditions.ContractEvidence.Any(string.IsNullOrWhiteSpace).ShouldBeTrue();
            mapping.Preconditions.ContractEvidence.All(string.IsNullOrWhiteSpace).ShouldBeFalse();
        }
        var plan = await AssertBlockedAsync(fixture.Solution, fixture.Request with { Mapping = mapping },
            kind == "evidence-mixed" ? "evidence" : kind);

        var structural = kind is "schema" or "receiver";
        plan.Sites.ShouldContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked
            && site.Message.Contains(structural ? "mapping" : "precondition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task caller_declarations_do_not_make_a_static_receiver_structurally_usable()
    {
        using var fixture = await CreateAsync(Supported, staticReceiver: true);
        fixture.Request.Mapping.Preconditions.ReceiverAvailability.ShouldNotBeNullOrWhiteSpace();
        fixture.Compilation.GetTypeByMetadataName("Fixture.Consumer")!.GetMembers("_reader")
            .OfType<IFieldSymbol>().Single().IsStatic.ShouldBeTrue();

        await AssertBlockedAsync(fixture.Solution, fixture.Request, "receiver", "instance");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    public async Task compilation_and_loaded_projects_do_not_replace_authoritative_acquisition(string kind)
    {
        using var fixture = await CreateAsync(Supported, attach: kind != "missing");
        var solution = kind == "stale"
            ? fixture.Solution.WithDocumentText(fixture.DocumentId, SourceText.From(fixture.Source + "\n// newer snapshot"))
            : fixture.Solution;
        await AssertCompilesAsync(solution);
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
        completeness.State.ShouldBe(WorkspaceCompletenessState.Partial);
        WorkspaceProofPolicy.Evaluate(completeness).CanUseAbsenceEvidence.ShouldBeFalse();
        if (kind == "stale")
            completeness.Diagnostics.ShouldContain(message => message.Contains("snapshot", StringComparison.OrdinalIgnoreCase));
        var request = fixture.Request with { ExpectedSourceFingerprint = await SolutionRevision.ComputeAsync(solution) };

        await AssertBlockedAsync(solution, request, "acquisition");
    }

    [Fact]
    public async Task fingerprint_drift_blocks_even_when_the_current_acquisition_is_complete()
    {
        using var fixture = await CreateAsync(Supported);
        var staleFingerprint = await SolutionRevision.ComputeAsync(fixture.Solution.WithDocumentText(
            fixture.DocumentId, SourceText.From(fixture.Source + "\n// different source")));
        staleFingerprint.ShouldNotBe(fixture.Request.ExpectedSourceFingerprint);
        (await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None))
            .State.ShouldBe(WorkspaceCompletenessState.Complete);

        await AssertBlockedAsync(fixture.Solution,
            fixture.Request with { ExpectedSourceFingerprint = staleFingerprint }, "fingerprint");
    }

    [Theory]
    [InlineData("inaccessible")]
    [InlineData("ambiguous")]
    [InlineData("different")]
    public async Task resolves_and_verifies_the_actual_receiver_call_rather_than_accepting_compilation_alone(string kind)
    {
        var declaration = kind switch
        {
            "inaccessible" => "public class IReader { private string ReadAllText(string path) => path; }",
            "ambiguous" => """
                public interface ILeft { string ReadAllText(string path); }
                public interface IRight { string ReadAllText(string path); }
                public interface IReader : ILeft, IRight { }
                """,
            _ => """
                public class BaseReader { public string ReadAllText(string path) => path; }
                public sealed class IReader : BaseReader { public new string ReadAllText(string path) => path; }
                """
        };
        var targetId = kind switch
        {
            "ambiguous" => "M:Fixture.ILeft.ReadAllText(System.String)",
            "different" => "M:Fixture.BaseReader.ReadAllText(System.String)",
            _ => TargetId
        };
        using var fixture = await CreateAsync(Supported, declaration, targetId: targetId);
        var proposedText = fixture.Source.Replace("File.ReadAllText(path)", "this._reader.ReadAllText(path)", StringComparison.Ordinal);
        var independentCandidate = fixture.Solution.WithDocumentText(fixture.DocumentId, SourceText.From(proposedText));
        var compilation = (await independentCandidate.GetProject(fixture.ProjectId)!.GetCompilationAsync()).ShouldNotBeNull();
        if (kind == "different")
        {
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
            var document = independentCandidate.GetDocument(fixture.DocumentId)!;
            var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
            var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
            var call = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            model.GetOperation(call).ShouldBeAssignableTo<IInvocationOperation>()!
                .TargetMethod.GetDocumentationCommentId().ShouldBe(TargetId);
            targetId.ShouldNotBe(TargetId);
        }
        else
        {
            compilation.GetDiagnostics().ShouldContain(diagnostic => diagnostic.Id == (kind == "ambiguous" ? "CS0121" : "CS0122"));
        }

        var plan = await AssertBlockedAsync(fixture.Solution, fixture.Request, kind == "different" ? "binding" : kind);
        if (kind != "different")
        {
            var diagnosticId = kind == "ambiguous" ? "CS0121" : "CS0122";
            plan.Sites.ShouldContain(site => site.Code == "mapping.binding"
                && site.Message.Contains(diagnosticId, StringComparison.Ordinal)
                && site.Message.Contains("ReadAllText", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task empty_source_inventory_still_validates_exact_parameter_compatibility(bool incompatible)
    {
        var parameter = incompatible ? "object" : "string";
        var targetId = incompatible ? "M:Fixture.IReader.ReadAllText(System.Object)" : TargetId;
        using var fixture = await CreateAsync("public string Read(string path) => path;",
            $"public interface IReader {{ string ReadAllText({parameter} path); }}", targetId: targetId);
        var document = fixture.Solution.GetDocument(fixture.DocumentId)!;
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        root.DescendantNodes().OfType<InvocationExpressionSyntax>().ShouldBeEmpty();
        var source = ResolveMethod(fixture.Compilation, SourceId);
        var target = ResolveMethod(fixture.Compilation, targetId);
        source.Parameters.ShouldHaveSingleItem().Type.SpecialType.ShouldBe(SpecialType.System_String);
        target.Parameters.ShouldHaveSingleItem().Type.SpecialType.ShouldBe(
            incompatible ? SpecialType.System_Object : SpecialType.System_String);

        if (incompatible)
        {
            var plan = await AssertBlockedAsync(fixture.Solution, fixture.Request, "parameter", "types");
            plan.Sites.ShouldHaveSingleItem().Code.ShouldBe("selection.mapping");
        }
        else
        {
            var plan = await PrepareAsync(fixture.Solution, fixture.Request);
            plan.Status.ShouldBe(StaticDependencyPlanStatus.NoOp);
            plan.Sites.ShouldBeEmpty();
            plan.Documents.ShouldBeEmpty();
            AssertEmptyChanges(plan, fixture.Solution);
        }
        await AssertWorkspaceUnchangedAsync(fixture);
    }

    [Fact]
    public async Task rejects_a_compiling_target_that_replaces_the_source_user_conversion_with_boxing()
    {
        using var fixture = await CreateAsync("public string Read(PathInput path) => File.ReadAllText(path);",
            "public interface IReader { string ReadAllText(object path); }",
            targetId: "M:Fixture.IReader.ReadAllText(System.Object)");
        var document = fixture.Solution.GetDocument(fixture.DocumentId)!;
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var original = model.GetOperation(root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())
            .ShouldBeAssignableTo<IInvocationOperation>()!;
        original.Arguments.Single().Parameter!.Type.SpecialType.ShouldBe(SpecialType.System_String);
        original.Arguments.Single().Value.ShouldBeAssignableTo<IConversionOperation>()!
            .OperatorMethod.ShouldNotBeNull().Name.ShouldBe("op_Implicit");
        var proposedText = fixture.Source.Replace("File.ReadAllText(path)", "this._reader.ReadAllText(path)", StringComparison.Ordinal);
        var independentCandidate = fixture.Solution.WithDocumentText(fixture.DocumentId, SourceText.From(proposedText));
        await AssertCompilesAsync(independentCandidate);
        var candidateDocument = independentCandidate.GetDocument(fixture.DocumentId)!;
        var candidateRoot = (await candidateDocument.GetSyntaxRootAsync()).ShouldNotBeNull();
        var candidateModel = (await candidateDocument.GetSemanticModelAsync()).ShouldNotBeNull();
        var target = candidateModel.GetOperation(candidateRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())
            .ShouldBeAssignableTo<IInvocationOperation>()!;
        target.Arguments.Single().Parameter!.Type.SpecialType.ShouldBe(SpecialType.System_Object);
        target.Arguments.Single().Value.ShouldBeAssignableTo<IConversionOperation>()!.OperatorMethod.ShouldBeNull();

        await AssertBlockedAsync(fixture.Solution, fixture.Request, "parameter");
    }

    [Fact]
    public async Task mapping_fingerprint_binds_the_caller_assumptions_and_repeat_preparation_is_deterministic()
    {
        using var fixture = await CreateAsync(Supported);
        var first = await PrepareAsync(fixture.Solution, fixture.Request);
        var repeat = await PrepareAsync(fixture.Solution, fixture.Request);
        first.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        repeat.Status.ShouldBe(first.Status);
        repeat.MappingFingerprint.ShouldBe(first.MappingFingerprint);
        repeat.Documents.ShouldBe(first.Documents);
        repeat.Sites.ShouldBe(first.Sites);
        AssertDispositionIdentities(first);
        var changedRequest = fixture.Request with
        {
            Mapping = fixture.Request.Mapping with
            {
                Preconditions = fixture.Request.Mapping.Preconditions with { ReceiverLifetime = "Caller now guarantees a longer lifetime." }
            }
        };

        var changedAssumption = await PrepareAsync(fixture.Solution, changedRequest);

        changedAssumption.Status.ShouldBe(StaticDependencyPlanStatus.Prepared);
        changedAssumption.SourceFingerprint.ShouldBe(first.SourceFingerprint);
        changedAssumption.MappingFingerprint.ShouldNotBe(first.MappingFingerprint);
        changedAssumption.Documents.ShouldBe(first.Documents);
        var expected = fixture.Source.Replace("File.ReadAllText(path)", "this._reader.ReadAllText(path)", StringComparison.Ordinal);
        await AssertCandidateAsync(fixture, first, new Dictionary<DocumentId, string> { [fixture.DocumentId] = expected });

        // Fresh controlled acquisition describes the prepared text. No workspace apply or disk
        // boundary is needed to establish idempotency on a subsequently acquired candidate.
        using var refreshed = await CreateAsync(Supported, completeSource: expected);
        refreshed.Request.ExpectedSourceFingerprint.ShouldNotBe(fixture.Request.ExpectedSourceFingerprint);
        var noOp = await PrepareAsync(refreshed.Solution, refreshed.Request);
        noOp.Status.ShouldBe(StaticDependencyPlanStatus.NoOp);
        noOp.SourceFingerprint.ShouldBe(refreshed.Request.ExpectedSourceFingerprint);
        noOp.Documents.ShouldBeEmpty();
        noOp.Sites.ShouldNotContain(site => site.Disposition != StaticDependencySiteDisposition.Skipped);
        AssertEmptyChanges(noOp, refreshed.Solution);
        await AssertWorkspaceUnchangedAsync(refreshed);
    }

    private static async Task<StaticDependencyPlan> PrepareAsync(Solution solution, StaticDependencyMappingRequest request)
    {
        StaticDependencyPlan? plan = null;
        var exception = await Record.ExceptionAsync(async () => plan = await new StaticDependencyMappingService().PrepareAsync(solution, request));
        exception.ShouldBeNull("existing-reader planning must return a complete candidate or an actionable blocked plan");
        return plan.ShouldNotBeNull();
    }

    private static async Task<StaticDependencyPlan> AssertBlockedAsync(
        Solution solution, StaticDependencyMappingRequest request, params string[] reasons)
    {
        var before = await SolutionRevision.ComputeAsync(solution);
        var workspaceSnapshot = solution.Workspace.CurrentSolution;
        var plan = await PrepareAsync(solution, request);
        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        plan.Request.ShouldBe(request);
        plan.Documents.ShouldBeEmpty("a rejected selection must not leak a partial diff");
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate);
        var blocked = plan.Sites.Where(site => site.Disposition == StaticDependencySiteDisposition.Blocked).ToArray();
        blocked.ShouldNotBeEmpty();
        foreach (var reason in reasons)
            blocked.ShouldContain(site => site.Message.Contains(reason, StringComparison.OrdinalIgnoreCase),
                $"the rejection must identify the independent cause '{reason}'");
        AssertDispositionIdentities(plan);
        AssertEmptyChanges(plan, solution);
        var repeat = await PrepareAsync(solution, request);
        repeat.Status.ShouldBe(StaticDependencyPlanStatus.Blocked);
        repeat.Sites.ShouldBe(plan.Sites);
        repeat.Documents.ShouldBeEmpty();
        AssertEmptyChanges(repeat, solution);
        solution.Workspace.CurrentSolution.ShouldBeSameAs(workspaceSnapshot);
        (await SolutionRevision.ComputeAsync(solution)).ShouldBe(before);
        return plan;
    }

    private static void AssertDispositionIdentities(StaticDependencyPlan plan)
    {
        plan.Sites.ShouldNotBeEmpty();
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
    }

    private static void AssertEmptyChanges(StaticDependencyPlan plan, Solution original)
    {
        plan.Changes.HasChanges.ShouldBeFalse();
        plan.Changes.AffectedPaths.ShouldBeEmpty();
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        candidate.GetChanges(original).GetProjectChanges().ShouldBeEmpty();
        candidate.GetChanges(original).GetAddedProjects().ShouldBeEmpty();
        candidate.GetChanges(original).GetRemovedProjects().ShouldBeEmpty();
        AssertNoFileEffects(plan.Changes);
    }

    private static void AssertNoFileEffects(PreparedChangeSet changes)
    {
        changes.NewFiles.ShouldBeEmpty();
        changes.DeletedFiles.ShouldBeEmpty();
        changes.MovedFiles.ShouldBeEmpty();
        changes.ProjectFileEdits.ShouldBeEmpty();
        changes.TextEdits.ShouldBeEmpty();
    }

    private static async Task AssertCandidateAsync(Fixture fixture, StaticDependencyPlan plan, IReadOnlyDictionary<DocumentId, string> expected)
    {
        plan.SourceFingerprint.ShouldBe(fixture.Request.ExpectedSourceFingerprint);
        plan.MappingFingerprint.ShouldNotBeNullOrWhiteSpace();
        plan.Changes.HasChanges.ShouldBeTrue();
        plan.Changes.OriginalSolution.ShouldBeSameAs(fixture.Solution);
        AssertNoFileEffects(plan.Changes);
        var candidate = plan.Changes.ChangedSolution.ShouldNotBeNull();
        await AssertCompilesAsync(candidate);
        var delta = candidate.GetChanges(fixture.Solution);
        delta.GetAddedProjects().ShouldBeEmpty();
        delta.GetRemovedProjects().ShouldBeEmpty();
        var changedIds = new List<DocumentId>();
        foreach (var projectChange in delta.GetProjectChanges())
        {
            projectChange.GetAddedDocuments().ShouldBeEmpty();
            projectChange.GetRemovedDocuments().ShouldBeEmpty();
            changedIds.AddRange(projectChange.GetChangedDocuments());
        }
        changedIds.OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .ShouldBe(expected.Keys.OrderBy(id => id.ToString(), StringComparer.Ordinal));
        plan.Documents.Count.ShouldBe(expected.Count);
        plan.Changes.AffectedPaths.Order(StringComparer.Ordinal)
            .ShouldBe(expected.Keys.Select(id => fixture.Solution.GetDocument(id)!.FilePath!).Order(StringComparer.Ordinal));
        foreach (var (id, text) in expected)
        {
            var original = fixture.Solution.GetDocument(id)!;
            var before = (await original.GetTextAsync()).ToString();
            (await candidate.GetDocument(id)!.GetTextAsync()).ToString().ShouldBe(text);
            plan.Documents.Single(change => change.Path == original.FilePath)
                .ShouldBe(new StaticDependencyDocumentChange(original.FilePath!, before, text));
        }
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.Blocked);
        await AssertWorkspaceUnchangedAsync(fixture);
    }

    private static async Task AssertWorkspaceUnchangedAsync(Fixture fixture)
    {
        fixture.Workspace.CurrentSolution.ShouldBeSameAs(fixture.Solution);
        (await fixture.Solution.GetDocument(fixture.DocumentId)!.GetTextAsync()).ToString().ShouldBe(fixture.Source);
        (await SolutionRevision.ComputeAsync(fixture.Solution)).ShouldBe(fixture.Request.ExpectedSourceFingerprint);
    }

    private static async Task AssertCompilesAsync(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = (await project.GetCompilationAsync()).ShouldNotBeNull();
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty("the independently controlled fixture/candidate must compile");
        }
    }

    private static IMethodSymbol ResolveMethod(Compilation compilation, string documentationId)
    {
        var method = DocumentationCommentId.GetSymbolsForDeclarationId(documentationId, compilation)
            .OfType<IMethodSymbol>().ShouldHaveSingleItem();
        method.GetDocumentationCommentId().ShouldBe(documentationId);
        return method;
    }

    private static async Task<Fixture> CreateAsync(string members, string readerDeclaration = Reader,
        string sourceId = SourceId, string targetId = TargetId, string? partialSource = null,
        bool includePeer = false, bool staticReceiver = false, bool attach = true, string? completeSource = null,
        bool peerUsesSelectedPath = false)
    {
        var source = completeSource ?? $$"""
            using System;
            using System.IO;
            using System.Text;
            using System.Linq.Expressions;
            namespace Fixture;
            {{readerDeclaration}}
            public readonly struct PathInput
            {
                public static implicit operator string(PathInput value) => "path";
            }
            public partial class Consumer
            {
                private {{(staticReceiver ? "static " : "readonly ")}}IReader _reader;
                public Consumer(IReader reader) { _reader = reader; }
                {{members}}
            }
            """;
        var workspace = new AdhocWorkspace();
        var projectId = AddProject(ProjectName, ProjectPath, source);
        var documentId = workspace.CurrentSolution.GetProject(projectId)!.DocumentIds.Single();
        if (partialSource is not null)
            AddDocument(projectId, "Second.cs", partialSource);
        if (includePeer)
            AddProject("Peer", peerUsesSelectedPath ? ProjectPath : Path.GetFullPath("/virtual/rt153-safety/Peer.csproj"), source);
        var solution = workspace.CurrentSolution;
        await AssertCompilesAsync(solution);
        if (attach)
        {
            await ExplicitWorkspaceAcquisition.AttachAsync(solution, includePeer ? [ProjectName, "Peer"] : [ProjectName]);
            var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
            if (peerUsesSelectedPath)
            {
                solution.Projects.Count(project => project.FilePath == ProjectPath).ShouldBe(2);
                completeness.State.ShouldBe(WorkspaceCompletenessState.Partial,
                    "duplicate physical project paths do not provide authoritative single-context acquisition");
                completeness.Diagnostics.ShouldNotBeEmpty();
            }
            else
            {
                completeness.State.ShouldBe(WorkspaceCompletenessState.Complete);
            }
        }
        var compilation = (await solution.GetProject(projectId)!.GetCompilationAsync()).ShouldNotBeNull();
        var sourceMethod = ResolveMethod(compilation, sourceId);
        sourceMethod.IsStatic.ShouldBeTrue();
        if (sourceId.StartsWith("M:System.IO.File.", StringComparison.Ordinal))
            sourceMethod.Locations.ShouldAllBe(location => location.IsInMetadata);
        var targetMethod = ResolveMethod(compilation, targetId);
        targetMethod.IsStatic.ShouldBeFalse();
        var mapping = new StaticDependencyMethodMapping("rt.static-dependency.mapping.v1",
            new StaticDependencySymbol(sourceId, sourceMethod.ContainingAssembly.Identity.ToString()),
            "F:Fixture.Consumer._reader",
            new StaticDependencySymbol(targetId, targetMethod.ContainingAssembly.Identity.ToString()),
            new StaticDependencyPreconditions(
                "Caller declares the existing field is assigned before the selected method executes.",
                "Caller declares receiver lifetime covers every selected invocation.",
                "Caller declares the receiver is non-null at every selected invocation.",
                "Caller declares adapter exception behavior matches the source overload.",
                "Caller declares adapter encoding behavior matches the source overload.",
                "Caller declares adapter cancellation behavior matches the source overload.",
                "Caller declares adapter resource ownership matches the source overload.",
                ["Controlled planner fixture: caller-supplied contract evidence; not a proof of arbitrary adapter equivalence."]));
        var request = new StaticDependencyMappingRequest(mapping, new StaticDependencySelection(ProjectPath, ConsumerId),
            await SolutionRevision.ComputeAsync(solution));
        return new Fixture(workspace, solution, projectId, documentId, source, compilation, request);

        ProjectId AddProject(string name, string path, string text)
        {
            var id = ProjectId.CreateNewId();
            var references = new[]
            {
                typeof(object).Assembly.Location,
                typeof(File).Assembly.Location,
                typeof(Encoding).Assembly.Location,
                typeof(System.Linq.Expressions.Expression).Assembly.Location,
                Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")
            }.Distinct(StringComparer.Ordinal).Select(referencePath => MetadataReference.CreateFromFile(referencePath));
            workspace.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: path, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp12), metadataReferences: references));
            AddDocument(id, name + ".cs", text);
            return id;
        }

        void AddDocument(ProjectId id, string name, string text)
        {
            workspace.AddDocument(DocumentInfo.Create(DocumentId.CreateNewId(id), name,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
                filePath: Path.GetFullPath("/virtual/rt153-safety/" + name)));
        }
    }

    private sealed record Fixture(AdhocWorkspace Workspace, Solution Solution, ProjectId ProjectId,
        DocumentId DocumentId, string Source, Compilation Compilation, StaticDependencyMappingRequest Request) : IDisposable
    {
        internal string DocumentPath => Solution.GetDocument(DocumentId)!.FilePath!;

        public void Dispose() => Workspace.Dispose();
    }
}
