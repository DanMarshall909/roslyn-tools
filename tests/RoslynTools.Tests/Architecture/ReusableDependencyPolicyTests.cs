using System.Diagnostics;
using System.Xml.Linq;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class ReusableDependencyPolicyTests
{
    [Theory]
    [InlineData("RoslynTools", false)]
    [InlineData("RoslynTools", true)]
    [InlineData("RoslynTools.Hosting", false)]
    [InlineData("RoslynTools.Hosting", true)]
    public async Task rejects_third_party_packages_including_imported_references(string project, bool imported)
    {
        var result = await EvaluateAsync(project, new XElement("PackageReference",
            new XAttribute("Include", "Unexpected.ThirdParty")), imported);

        result.ExitCode.ShouldNotBe(0);
        result.Output.ShouldContain("RTDEP001");
        result.Output.ShouldContain(project);
        result.Output.ShouldContain("Unexpected.ThirdParty");
    }

    [Theory]
    [InlineData("RoslynTools")]
    [InlineData("RoslynTools.Hosting")]
    public async Task rejects_outward_project_references(string project)
    {
        var result = await EvaluateAsync(project, new XElement("ProjectReference",
            new XAttribute("Include", "../RoslynTools.Adapters/RoslynTools.Adapters.csproj")), imported: true);

        result.ExitCode.ShouldNotBe(0);
        result.Output.ShouldContain("RTDEP002");
        result.Output.ShouldContain("RoslynTools.Adapters");
    }

    [Fact]
    public async Task permits_microsoft_packages_build_analyzers_and_inward_hosting_reference()
    {
        var result = await EvaluateAsync("RoslynTools.Hosting", new XElement("ItemGroup",
            new XElement("PackageReference", new XAttribute("Include", "Microsoft.Extensions.DependencyInjection")),
            new XElement("PackageReference", new XAttribute("Include", "Roslynator.Analyzers"),
                new XElement("PrivateAssets", "all")),
            new XElement("ProjectReference", new XAttribute("Include", "../RoslynTools/RoslynTools.csproj"))),
            imported: false);

        result.ExitCode.ShouldBe(0, result.Output);
    }

    private static async Task<(int ExitCode, string Output)> EvaluateAsync(
        string project, XElement reference, bool imported)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RoslynTools.slnx")))
            root = root.Parent;
        root.ShouldNotBeNull();
        var policy = Path.Combine(root.FullName, "Directory.Build.targets");
        var temporary = Path.Combine(Path.GetTempPath(), $"rt-dependency-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var items = reference.Name == "ItemGroup" ? reference : new XElement("ItemGroup", reference);
            var document = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0")),
                new XElement("Import", new XAttribute("Project", policy),
                    new XAttribute("Condition", "Exists('" + policy + "')"))));
            if (imported)
            {
                new XDocument(new XElement("Project", items)).Save(Path.Combine(temporary, "external.props"));
                document.Root!.Add(new XElement("Import", new XAttribute("Project", "external.props")));
            }
            else
            {
                document.Root!.Add(items);
            }
            var path = Path.Combine(temporary, project + ".csproj");
            document.Save(path);
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = temporary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "msbuild", path, "-nologo", "-t:CollectPackageReferences", "-v:minimal" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
