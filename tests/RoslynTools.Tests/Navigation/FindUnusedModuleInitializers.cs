using Microsoft.CodeAnalysis;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindUnusedModuleInitializers
{
    [Fact]
    public async Task excludes_initializer_and_container_but_keeps_ordinary_sibling_methods()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("MsBuildFixture.cs", """
            using System.Runtime.CompilerServices;
            internal static class MsBuildFixture
            {
                [ModuleInitializer]
                internal static void Initialize() { }
                internal static void Orphan() { }
            }
            """);
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var compilation = (await solution.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
            compilation.GetDiagnostics().ShouldNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

            var findings = await new FindUnusedService().AnalyzeAsync(solution);

            findings.ShouldNotContain(finding => finding.Name == "Initialize");
            findings.ShouldNotContain(finding => finding.Name == "MsBuildFixture");
            findings.ShouldContain(finding => finding.Name == "Orphan");
        }
    }

    [Fact]
    public async Task same_named_custom_attribute_does_not_establish_module_activation()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Custom.cs", """
            namespace Custom;
            public sealed class ModuleInitializerAttribute : System.Attribute { }
            internal static class Ordinary
            {
                [ModuleInitializer]
                internal static void Initialize() { }
            }
            """);
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var findings = await new FindUnusedService().AnalyzeAsync(solution);

            findings.ShouldContain(finding => finding.Name == "Initialize");
            findings.ShouldContain(finding => finding.Name == "Ordinary");
        }
    }
}
