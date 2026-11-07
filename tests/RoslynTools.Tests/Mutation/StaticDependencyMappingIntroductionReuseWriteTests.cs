using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;
using static RoslynTools.Tests.Mutation.StaticDependencyReuseFixture;

namespace RoslynTools.Tests.Mutation;

public sealed class StaticDependencyMappingIntroductionReuseWriteTests
{
    [Theory]
    [InlineData("increment")]
    [InlineData("ref")]
    [InlineData("out")]
    public async Task competing_receiver_writes_after_initial_assignment_refuse_every_selected_call(string writeKind)
    {
        // A class receiver allows an actual user-defined increment operation while retaining
        // the fixture's exact receiver/target identities and ordinary first assignment.
        const string contracts = """
            namespace Fixture;
            internal class IReader
            {
                public string ReadAllText(string path) => System.IO.File.ReadAllText(path);
                public static IReader operator ++(IReader value) => new BclReader();
            }
            internal sealed class BclReader : IReader { }
            """;
        var write = writeKind == "increment" ? "this._existing++;" : $"Replace({writeKind} this._existing);";
        var consumer = Consumer.Replace(Assignment, Assignment + "\n        " + write, StringComparison.Ordinal);
        if (writeKind != "increment")
            consumer = consumer.Replace("internal string Read",
                $"private static void Replace({writeKind} IReader value) {{ value = new BclReader(); }}\n    internal string Read",
                StringComparison.Ordinal);
        using var fixture = await CreateAsync(consumer, contracts);
        var original = fixture.Solution;
        var compilation = await AssertCompilesAsync(original);
        await AssertCompleteAsync(original);
        var field = DocumentationCommentId.GetSymbolsForDeclarationId(FieldId, compilation)
            .ShouldHaveSingleItem().ShouldBeAssignableTo<IFieldSymbol>()!;
        field.IsReadOnly.ShouldBeTrue();
        field.IsStatic.ShouldBeFalse();
        field.ContainingType.GetDocumentationCommentId().ShouldBe(ConsumerId);
        field.Type.TypeKind.ShouldBe(TypeKind.Class);
        field.Type.GetDocumentationCommentId().ShouldBe(ReceiverId);
        var target = DocumentationCommentId.GetSymbolsForDeclarationId(TargetId, compilation)
            .ShouldHaveSingleItem().ShouldBeAssignableTo<IMethodSymbol>()!;
        target.ContainingType.ShouldBe(field.Type);
        var document = Document(original, "Consumer.cs");
        var root = (await document.GetSyntaxRootAsync()).ShouldNotBeNull();
        var model = (await document.GetSemanticModelAsync()).ShouldNotBeNull();
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ShouldHaveSingleItem();
        constructor.Initializer.ShouldBeNull();
        var statements = constructor.Body.ShouldNotBeNull().Statements;
        var initial = model.GetOperation(statements[0]).ShouldBeAssignableTo<IExpressionStatementOperation>()!
            .Operation.ShouldBeAssignableTo<ISimpleAssignmentOperation>()!;
        initial.Target.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.ShouldBe(field);
        var parameter = initial.Value.ShouldBeAssignableTo<IParameterReferenceOperation>()!.Parameter;
        parameter.Type.ShouldBe(field.Type);
        parameter.RefKind.ShouldBe(RefKind.None);
        parameter.IsOptional.ShouldBeFalse();
        parameter.IsParams.ShouldBeFalse();
        parameter.ContainingSymbol.ShouldBe(model.GetDeclaredSymbol(constructor));
        var competing = model.GetOperation(statements[1]).ShouldBeAssignableTo<IExpressionStatementOperation>()!.Operation;
        if (writeKind == "increment")
        {
            var increment = competing.ShouldBeAssignableTo<IIncrementOrDecrementOperation>()!;
            increment.Target.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.ShouldBe(field);
            increment.OperatorMethod.ShouldNotBeNull().ContainingType.ShouldBe(field.Type);
        }
        else
        {
            var argument = competing.ShouldBeAssignableTo<IInvocationOperation>()!.Arguments.ShouldHaveSingleItem();
            argument.Parameter.ShouldNotBeNull().RefKind.ShouldBe(writeKind == "ref" ? RefKind.Ref : RefKind.Out);
            argument.Value.ShouldBeAssignableTo<IFieldReferenceOperation>()!.Field.ShouldBe(field);
        }
        var selected = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => model.GetOperation(call) is IInvocationOperation operation
                && operation.TargetMethod.GetDocumentationCommentId() == SourceId).ToArray();
        selected.Select(call => call.ToString()).ShouldBe([ConstructorCall, MethodCall]);
        statements[1].Span.End.ShouldBeLessThan(selected[0].SpanStart);
        fixture.Request.Callers.ShouldBeEmpty();

        var plan = await new StaticDependencyIntroductionService().PrepareAsync(original, fixture.Request);

        plan.Status.ShouldBe(StaticDependencyPlanStatus.Blocked,
            "a competing write to the explicitly selected receiver must atomically refuse reuse");
        foreach (var call in selected)
        {
            var site = plan.Sites.Single(site => site.Path == document.FilePath && site.SpanStart == call.SpanStart);
            site.SpanLength.ShouldBe(call.Span.Length);
            site.Disposition.ShouldBe(StaticDependencySiteDisposition.Blocked);
            site.Message.ShouldContain(FieldId);
            site.Message.ShouldContain("competing write or reference escape");
        }
        plan.Sites.Where(site => site.TypeDocumentationId != ConsumerId).ShouldHaveSingleItem()
            .Disposition.ShouldBe(StaticDependencySiteDisposition.Skipped);
        plan.Sites.ShouldNotContain(site => site.Disposition == StaticDependencySiteDisposition.ConvertedInCandidate);
        plan.Documents.ShouldBeEmpty();
        plan.Closure.ShouldBeEmpty();
        plan.Resolution.ShouldBeNull();
        plan.Changes.OriginalSolution.ShouldBeSameAs(original);
        plan.Changes.ChangedSolution.ShouldBeSameAs(original);
        plan.Changes.HasChanges.ShouldBeFalse();
        plan.Changes.AffectedPaths.ShouldBeEmpty();
        plan.Changes.NewFiles.ShouldBeEmpty();
        plan.Changes.DeletedFiles.ShouldBeEmpty();
        plan.Changes.MovedFiles.ShouldBeEmpty();
        plan.Changes.ProjectFileEdits.ShouldBeEmpty();
        plan.Changes.TextEdits.ShouldBeEmpty();
        await fixture.AssertUntouchedAsync();
    }
}
