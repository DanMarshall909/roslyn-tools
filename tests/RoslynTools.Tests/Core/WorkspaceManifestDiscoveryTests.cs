using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.WorkspaceAcquisition;

public sealed class WorkspaceManifestDiscoveryTests
{
    [Fact]
    public async Task slnx_retains_valid_projects_after_each_malformed_project_entry()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var path = Path.Combine(fixture.RootDir, "Selected.slnx");
        await File.WriteAllTextAsync(path, """
            <Solution>
              <Project Path="Before/Before.csproj" />
              <Project />
              <Project Path="Middle/Middle.csproj" />
              <Project Path=" " />
              <Folder Name="Nested">
                <Project Path="After/After.csproj" />
              </Folder>
            </Solution>
            """);

        var seed = await WorkspaceManifestDiscovery.SeedAsync(path);

        AssertProjects(seed, fixture.RootDir, "Before", "Middle", "After");
        seed.Diagnostics.Length.ShouldBe(2);
        seed.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Id == "manifest.project-discovery"
            && diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.Impact == CompletenessDiagnosticImpact.RequiredInputIncomplete);
        seed.Diagnostics.Select(diagnostic => diagnostic.InputId).ShouldBeUnique();
        seed.Diagnostics.ShouldAllBe(diagnostic => diagnostic.InputId != null);
        seed.Manifest.DiscoveryState.ShouldBe(ManifestDiscoveryState.Unknown);
    }

    [Fact]
    public async Task sln_retains_valid_projects_after_malformed_syntax_and_invalid_paths()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var path = Path.Combine(fixture.RootDir, "Selected.sln");
        var invalidPath = "Invalid" + '\0' + ".csproj";
        await File.WriteAllLinesAsync(path,
        [
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            ProjectLine("Before", "Before/Before.csproj"),
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = malformed",
            ProjectLine("Middle", "Middle/Middle.csproj"),
            ProjectLine("Invalid", invalidPath),
            ProjectLine("After", "After/After.csproj")
        ]);

        var seed = await WorkspaceManifestDiscovery.SeedAsync(path);

        AssertProjects(seed, fixture.RootDir, "Before", "Middle", "After");
        seed.Diagnostics.Length.ShouldBe(2);
        seed.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Impact == CompletenessDiagnosticImpact.RequiredInputIncomplete);
        seed.Diagnostics.Select(diagnostic => diagnostic.InputId).ShouldBeUnique();
        seed.Manifest.DiscoveryState.ShouldBe(ManifestDiscoveryState.Unknown);
    }

    [Fact]
    public async Task malformed_xml_remains_an_unclosed_discovery_failure()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var path = Path.Combine(fixture.RootDir, "Broken.slnx");
        await File.WriteAllTextAsync(path, "<Solution><Project Path=\"After.csproj\"></Solution>");

        var seed = await WorkspaceManifestDiscovery.SeedAsync(path);

        seed.Manifest.Inputs.ShouldNotContain(input => input.Kind == SemanticInputKind.Project);
        seed.Diagnostics.ShouldHaveSingleItem().Impact.ShouldBe(CompletenessDiagnosticImpact.RequiredInputIncomplete);
        seed.Manifest.DiscoveryState.ShouldBe(ManifestDiscoveryState.Unknown);
    }

    private static string ProjectLine(string name, string path) =>
        $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{path}\", \"{{C0E432AE-8119-40D0-BFCC-9701615A6387}}\"";

    private static void AssertProjects(WorkspaceManifestSeed seed, string directory, params string[] names)
    {
        var paths = seed.Manifest.Inputs.Where(input => input.Kind == SemanticInputKind.Project)
            .Select(input => input.ProjectPath);
        paths.ShouldBe(names.Select(name => Path.Combine(directory, name, name + ".csproj")), ignoreOrder: true);
    }
}
