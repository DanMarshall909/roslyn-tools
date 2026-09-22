using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class WorkspaceObservedInputVerifierTests
{
    [Theory]
    [InlineData("Project")]
    [InlineData("CompilationContext")]
    [InlineData("SourceDocument")]
    [InlineData("AdditionalDocument")]
    [InlineData("AnalyzerConfigDocument")]
    [InlineData("GeneratedDocument")]
    [InlineData("ProjectReference")]
    [InlineData("MetadataReference")]
    [InlineData("AnalyzerReference")]
    public async Task every_observed_input_category_must_be_declared_in_closed_manifest(string kindName)
    {
        using var builder = new AdhocProjectBuilder("A");
        builder.AddSource("A.cs", "public class A { }").AddAdditionalDocument("inputs.txt", "known");
        builder.AddProject("A").AddGeneratedSource("Known.g.cs", "public class Generated { }").ReferenceProject("B");
        builder.AddProject("B").AddSource("B.cs", "public class B { }");
        var (workspace, original) = builder.Build();
        using (workspace)
        {
            var projectId = original.Projects.Single(project => project.Name == "A").Id;
            var solution = original.AddAnalyzerConfigDocument(DocumentId.CreateNewId(projectId), ".editorconfig",
                SourceText.From("root = true"), filePath: Path.Combine(builder.ProjectDir, ".editorconfig"));
            await ExplicitWorkspaceAcquisition.AttachAsync(solution, ["A", "B"],
                new Dictionary<string, string[]> { ["A"] = ["Known.g.cs"] });
            var receipt = WorkspaceAcquisitionRegistry.Get(workspace).Receipt.ShouldNotBeNull();
            var complete = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
            complete.State.ShouldBe(WorkspaceCompletenessState.Complete, string.Join(Environment.NewLine, complete.Diagnostics));
            var kind = Enum.Parse<SemanticInputKind>(kindName);
            var manifest = new SemanticInputManifest(null, receipt.Manifest.Inputs.Where(input => input.Kind != kind), ManifestDiscoveryState.Closed);
            var outcomes = receipt.Outcomes.Where(outcome => manifest.Inputs.Any(input => input.Id == outcome.InputId)).ToArray();
            var diagnostics = await WorkspaceObservedInputVerifier.VerifyAsync(solution, manifest, outcomes, CancellationToken.None);
            diagnostics.ShouldContain(diagnostic => diagnostic.Id == "acquisition.undeclared-observed-input");
            WorkspaceCompletenessPolicy.Evaluate(manifest, outcomes, diagnostics.ToArray(), true, true)
                .State.ShouldNotBe(WorkspaceCompletenessState.Complete);
        }
    }
}
