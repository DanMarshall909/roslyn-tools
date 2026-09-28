using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynTools.Core;
using RoslynTools.Core.CommandSurface;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Architecture;

public sealed class ReusableProjectCommandLineBoundary
{
    [Fact]
    public void Reusable_project_has_no_CommandLine_package_or_assembly_reference()
    {
        var project = XDocument.Load(Path.Combine(
            RtCliHost.RepoRoot, "src", "RoslynTools", "RoslynTools.csproj"));
        var packages = project.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value);

        Assert.DoesNotContain("System.CommandLine", packages);
        Assert.DoesNotContain(
            typeof(RtCommandInfo).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "System.CommandLine");
    }

    [Fact]
    public void Reusable_source_has_no_CommandLine_imports()
    {
        var sourceRoot = Path.Combine(RtCliHost.RepoRoot, "src", "RoslynTools");
        var offenders = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(sourceRoot, path)))
            .Where(entry => !entry.Relative.Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .Where(entry => Regex.IsMatch(
                File.ReadAllText(entry.Path),
                @"(?m)^\s*(?:global\s+)?using\s+System\.CommandLine(?:\.|;)",
                RegexOptions.CultureInvariant))
            .Select(entry => entry.Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Cli_assembly_owns_command_markers_and_surface_projection()
    {
        var cliAssembly = typeof(App).Assembly;

        Assert.Same(cliAssembly, typeof(IRtCommand).Assembly);
        Assert.Same(cliAssembly, typeof(IInspectCommand).Assembly);
        Assert.Same(cliAssembly, typeof(IAnalyzeCommand).Assembly);
        Assert.Same(cliAssembly, typeof(IRefactorCommand).Assembly);
        Assert.Same(cliAssembly, typeof(CommandExtensions).Assembly);
        Assert.Same(cliAssembly, typeof(CommandSurfaceBuilder).Assembly);
        Assert.Same(cliAssembly, typeof(CommandSurfaceModel).Assembly);
    }
}
