using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

/// <summary>
/// US-139: what the probe *concludes* is separable from how it observes. ProbeDiService.ClassifyAsync
/// reads a ServiceCollection someone else produced, so these drive the real decision logic — is this
/// implementation invisible to static analysis? — without loading an assembly or executing user code.
///
/// The subjects below are ordinary runtime types; the ad-hoc solution declares source types with the
/// same fully-qualified names, which is what the classifier looks up. That mirror is the whole trick.
/// </summary>
public class ProbeDiClassification
{
    private const string SubjectNamespace = "RoslynTools.Tests.Analysis";

    /// <summary>
    /// Source mirroring the runtime subject types. Whether a subject is referenced, annotated, or
    /// bare is what each test varies.
    /// </summary>
    private static string SourceFor(string body) => $$"""
                                                      namespace {{SubjectNamespace}};

                                                      {{body}}
                                                      """;

    private static async Task<IReadOnlyList<ProbeDiRegistration>> ClassifyAsync(
        AdhocProjectBuilder project,
        ServiceCollection services)
    {
        var (workspace, solution) = project.Build();
        using (workspace)
            return await ProbeDiService.ClassifyAsync(solution, services);
    }

    [Fact(DisplayName = "probe-di reports an implementation with no source reference as invisible")]
    public async Task reports_an_unreferenced_implementation_as_invisible()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Subjects.cs", SourceFor("public sealed class InvisibleSubject { }"));

        var services = new ServiceCollection();
        services.AddSingleton<ISubject, InvisibleSubject>();

        var findings = await ClassifyAsync(project, services);

        var finding = findings.ShouldHaveSingleItem();
        finding.Status.ShouldBe(ProbeDiStatus.Invisible,
            "the container constructs this type at runtime and nothing in source names it — that is the whole point of the probe");
        finding.ImplementationType.ShouldBe($"{SubjectNamespace}.InvisibleSubject");
    }

    [Fact(DisplayName = "probe-di stays quiet about an implementation that source already references")]
    public async Task ignores_an_implementation_with_a_static_reference()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Subjects.cs", SourceFor("""
                                                   public sealed class InvisibleSubject { }

                                                   public sealed class Caller
                                                   {
                                                       public InvisibleSubject Make() => new InvisibleSubject();
                                                   }
                                                   """));

        var services = new ServiceCollection();
        services.AddSingleton<ISubject, InvisibleSubject>();

        var findings = await ClassifyAsync(project, services);

        findings.ShouldBeEmpty(
            "static analysis can already see this type, so there is nothing to make visible");
    }

    [Fact(DisplayName = "probe-di stays quiet about an implementation annotated as reflectively used")]
    public async Task ignores_an_implementation_carrying_a_reflective_use_annotation()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Subjects.cs", SourceFor("""
                                                   using System;

                                                   [AttributeUsage(AttributeTargets.Class)]
                                                   public sealed class UsedImplicitlyAttribute : Attribute { }

                                                   [UsedImplicitly]
                                                   public sealed class InvisibleSubject { }
                                                   """));

        var services = new ServiceCollection();
        services.AddSingleton<ISubject, InvisibleSubject>();

        var findings = await ClassifyAsync(project, services);

        findings.ShouldBeEmpty(
            "the annotation is the fix this command exists to suggest, so a type that has it is already done");
    }

    [Fact(DisplayName = "probe-di stays quiet about an implementation named in an ILLink descriptor")]
    public async Task ignores_an_implementation_preserved_by_an_il_link_descriptor()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Subjects.cs", SourceFor("public sealed class InvisibleSubject { }"));
        project.AddAdditionalDocument("ILLink.Descriptors.xml", $"""
                                                                 <linker>
                                                                   <assembly fullname="TestProject">
                                                                     <type fullname="{SubjectNamespace}.InvisibleSubject" preserve="all" />
                                                                   </assembly>
                                                                 </linker>
                                                                 """);

        var services = new ServiceCollection();
        services.AddSingleton<ISubject, InvisibleSubject>();

        var findings = await ClassifyAsync(project, services);

        findings.ShouldBeEmpty(
            "a descriptor already declares this type reflectively used, which is the same guarantee by another means");
    }

    [Fact(DisplayName = "probe-di ignores a non-xml additional document that happens to name the type")]
    public async Task ignores_a_non_descriptor_additional_document_mentioning_the_type()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Subjects.cs", SourceFor("public sealed class InvisibleSubject { }"));
        project.AddAdditionalDocument("notes.txt", $"We should keep {SubjectNamespace}.InvisibleSubject around.");

        var services = new ServiceCollection();
        services.AddSingleton<ISubject, InvisibleSubject>();

        var findings = await ClassifyAsync(project, services);

        findings.ShouldHaveSingleItem().Status.ShouldBe(ProbeDiStatus.Invisible,
            "prose that mentions a type preserves nothing; only a linker descriptor does");
    }

    [Fact(DisplayName = "probe-di reports a factory registration as unprobeable")]
    public async Task reports_a_factory_registration_as_unprobeable()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Subjects.cs", SourceFor("public sealed class InvisibleSubject { }"));

        var services = new ServiceCollection();
        services.AddSingleton<ISubject>(_ => new InvisibleSubject());

        var findings = await ClassifyAsync(project, services);

        var finding = findings.ShouldHaveSingleItem();
        finding.Status.ShouldBe(ProbeDiStatus.Unprobeable,
            "a factory hides its implementation type, and staying silent would read as 'nothing to see here'");
        finding.ImplementationType.ShouldBeNull();
    }

    [Fact(DisplayName = "probe-di stays quiet about an instance registration")]
    public async Task ignores_an_instance_registration()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Subjects.cs", SourceFor("public sealed class InvisibleSubject { }"));

        var services = new ServiceCollection();
        services.AddSingleton<ISubject>(new InvisibleSubject());

        var findings = await ClassifyAsync(project, services);

        findings.ShouldBeEmpty(
            "the caller constructed the instance itself, so the source that did so already references the type");
    }

    [Fact(DisplayName = "probe-di reports an implementation that comes from a referenced assembly")]
    public async Task reports_an_implementation_resolved_from_referenced_metadata()
    {
        using var project = new AdhocProjectBuilder();
        // No source declares the subject here — it resolves from the referenced assembly instead.
        project.AddSource("Subjects.cs", SourceFor("public sealed class Unrelated { }"));

        var services = new ServiceCollection();
        services.AddSingleton<ISubject, InvisibleSubject>();

        var findings = await ClassifyAsync(project, services);

        findings.ShouldHaveSingleItem().Status.ShouldBe(ProbeDiStatus.Invisible,
            "the fix writes typeof(X) at the composition root, which binds to a type from a referenced assembly just as well as to one in source — so a scanned package type is still worth reporting");
    }
}

/// <summary>The service a subject is registered against. Nothing about it is interesting.</summary>
public interface ISubject;

/// <summary>
/// Runtime stand-in for a registered implementation. Its fully-qualified name is what the ad-hoc
/// source above mirrors, which is how the classifier resolves it to a symbol.
/// </summary>
public sealed class InvisibleSubject : ISubject;
