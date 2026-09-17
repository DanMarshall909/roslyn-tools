using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using RoslynTools.Core;

using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class ReadOnlyWorkspaceSessionCacheTests
{
    [Fact]
    public async Task physical_fingerprint_tracks_potential_sources_and_ignores_unrelated_extensions()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        WorkspaceInputFingerprintResult original;
        await using (var heldSolution = new FileStream(
                         solutionPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete))
        {
            original = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        }
        File.WriteAllText(System.IO.Path.Combine(root.Path, "notes.txt"), "unrelated");
        var afterUnrelatedChange = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        afterUnrelatedChange.ShouldBe(original);

        var potentialDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "bin"));
        File.WriteAllText(System.IO.Path.Combine(potentialDirectory.FullName, "Potential.cs"), "class Potential;");

        var afterPotentialSource = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        afterPotentialSource.Value.ShouldNotBe(original.Value);

        var nested = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "Nested"));
        File.WriteAllText(System.IO.Path.Combine(nested.FullName, "Untracked.cs"), "class Added;");
        var afterRelevantChange = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);

        afterRelevantChange.Value.ShouldNotBe(afterPotentialSource.Value);
    }

    [Fact]
    public async Task newly_applicable_project_assembly_candidate_changes_the_fingerprint()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var externalAssembly = System.IO.Path.Combine(external.Path, "Dependency.dll");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <Reference Include="Dependency"><HintPath>{externalAssembly}</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(externalAssembly, "original-reference");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "Dependency.dll"),
            "new-higher-precedence-candidate");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task explicit_external_none_assembly_candidate_changes_the_fingerprint()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var externalAssembly = System.IO.Path.Combine(external.Path, "Dependency.dll");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><ItemGroup><None Include=\"{externalAssembly}\" /></ItemGroup></Project>");
        File.WriteAllText(externalAssembly, "original-reference");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(externalAssembly, "changed-reference");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Theory]
    [InlineData("AssemblySearchPaths")]
    [InlineData("DesignTimeAssemblySearchPaths")]
    public async Task custom_assembly_search_path_is_not_claimed_reusable(string propertyName)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup><{propertyName}>/external</{propertyName}></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task external_ruleset_configuration_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var includedRuleset = System.IO.Path.Combine(external.Path, "Included.ruleset");
        var ruleset = System.IO.Path.Combine(external.Path, "App.ruleset");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup><CodeAnalysisRuleSet>{ruleset}</CodeAnalysisRuleSet></PropertyGroup></Project>");
        File.WriteAllText(
            ruleset,
            $"<RuleSet><Include Path=\"{includedRuleset}\" /></RuleSet>");
        File.WriteAllText(includedRuleset, "<RuleSet />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ResolvedCodeAnalysisRuleSet")]
    [InlineData("AllowedReferenceAssemblyFileExtensions")]
    public async Task resolved_ruleset_and_custom_reference_extensions_are_not_claimed_reusable(
        string propertyName)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup><{propertyName}>/external/value</{propertyName}></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("SignAssembly")]
    [InlineData("AssemblyOriginatorKeyFile")]
    [InlineData("KeyOriginatorFile")]
    [InlineData("KeyContainerName")]
    [InlineData("CryptoKeyFile")]
    [InlineData("CryptoKeyContainer")]
    [InlineData("DelaySign")]
    [InlineData("PublicSign")]
    public async Task authored_signing_configuration_is_not_claimed_reusable(string propertyName)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup><{propertyName}>external-key</{propertyName}></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task git_head_ref_change_changes_the_fingerprint_without_an_authored_file_change()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var gitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, ".git"));
        Directory.CreateDirectory(System.IO.Path.Combine(gitDirectory.FullName, "objects"));
        var refDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            gitDirectory.FullName,
            "refs",
            "heads"));
        var refPath = System.IO.Path.Combine(refDirectory.FullName, "main");
        File.WriteAllText(System.IO.Path.Combine(gitDirectory.FullName, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(refPath, new string('1', 40) + "\n");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(refPath, new string('2', 40) + "\n");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task linked_worktree_head_uses_its_private_git_directory_and_common_refs()
    {
        using var root = new TemporaryDirectory();
        using var common = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var privateGitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            common.Path,
            "worktrees",
            "linked"));
        Directory.CreateDirectory(System.IO.Path.Combine(common.Path, "objects"));
        var refDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            common.Path,
            "refs",
            "heads"));
        var refPath = System.IO.Path.Combine(refDirectory.FullName, "linked");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, ".git"),
            $"gitdir: {privateGitDirectory.FullName}\n");
        File.WriteAllText(
            System.IO.Path.Combine(privateGitDirectory.FullName, "commondir"),
            "../..\n");
        File.WriteAllText(
            System.IO.Path.Combine(privateGitDirectory.FullName, "HEAD"),
            "ref: refs/heads/linked\n");
        File.WriteAllText(refPath, new string('3', 40) + "\n");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(refPath, new string('4', 40) + "\n");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task git_repository_config_change_changes_the_fingerprint()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var gitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, ".git"));
        Directory.CreateDirectory(System.IO.Path.Combine(gitDirectory.FullName, "objects"));
        var refDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            gitDirectory.FullName,
            "refs",
            "heads"));
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "HEAD"),
            "ref: refs/heads/main\n");
        File.WriteAllText(
            System.IO.Path.Combine(refDirectory.FullName, "main"),
            new string('1', 40) + "\n");
        var configPath = System.IO.Path.Combine(gitDirectory.FullName, "config");
        File.WriteAllText(
            configPath,
            "[remote \"origin\"]\n\turl = https://example.test/one.git\n");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            configPath,
            "[remote \"origin\"]\n\turl = https://example.test/two.git\n");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task included_git_config_change_changes_the_fingerprint()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var gitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, ".git"));
        Directory.CreateDirectory(System.IO.Path.Combine(gitDirectory.FullName, "objects"));
        var refDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            gitDirectory.FullName,
            "refs",
            "heads"));
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "HEAD"),
            "ref: refs/heads/main\n");
        File.WriteAllText(
            System.IO.Path.Combine(refDirectory.FullName, "main"),
            new string('1', 40) + "\n");
        var includedConfigPath = System.IO.Path.Combine(external.Path, "included.gitconfig");
        File.WriteAllText(
            includedConfigPath,
            "[remote \"origin\"]\n\turl = https://example.test/one.git\n");
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "config"),
            $"[include]\n\tpath = {includedConfigPath.Replace("\\", "/", StringComparison.Ordinal)}\n");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            includedConfigPath,
            "[remote \"origin\"]\n\turl = https://example.test/two.git\n");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task invalid_git_marker_does_not_hide_a_valid_ancestor_repository()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var nestedRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(repositoryRoot, "nested")).FullName;
        var solutionPath = System.IO.Path.Combine(nestedRoot, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        Directory.CreateDirectory(System.IO.Path.Combine(nestedRoot, ".git"));
        var gitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(repositoryRoot, ".git"));
        Directory.CreateDirectory(System.IO.Path.Combine(gitDirectory.FullName, "objects"));
        var refDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            gitDirectory.FullName,
            "refs",
            "heads"));
        var refPath = System.IO.Path.Combine(refDirectory.FullName, "main");
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "HEAD"),
            "ref: refs/heads/main\n");
        File.WriteAllText(refPath, new string('1', 40) + "\n");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", nestedRoot);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(refPath, new string('2', 40) + "\n");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task minimal_source_link_repository_metadata_tracks_head_and_config_without_objects()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var gitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, ".git"));
        var headPath = System.IO.Path.Combine(gitDirectory.FullName, "HEAD");
        var configPath = System.IO.Path.Combine(gitDirectory.FullName, "config");
        File.WriteAllText(headPath, new string('1', 40) + "\n");
        File.WriteAllText(
            configPath,
            "[remote \"origin\"]\n\turl = https://example.test/one.git\n");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(headPath, new string('2', 40) + "\n");
        var afterHead = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            configPath,
            "[remote \"origin\"]\n\turl = https://example.test/two.git\n");
        var afterConfig = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        afterHead.Reusable.ShouldBeTrue();
        afterConfig.Reusable.ShouldBeTrue();
        afterHead.Value.ShouldNotBe(before.Value);
        afterConfig.Value.ShouldNotBe(afterHead.Value);
    }

    [Fact]
    public async Task source_link_user_profile_config_change_changes_the_fingerprint()
    {
        using var root = new TemporaryDirectory();
        using var profile = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var gitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, ".git"));
        Directory.CreateDirectory(System.IO.Path.Combine(gitDirectory.FullName, "objects"));
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "HEAD"),
            new string('1', 40) + "\n");
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "config"),
            "[core]\n\trepositoryformatversion = 0\n");
        var profileConfigPath = System.IO.Path.Combine(profile.Path, ".gitconfig");
        File.WriteAllText(
            profileConfigPath,
            "[remote \"origin\"]\n\turl = https://example.test/one.git\n");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            loadConfiguration: null,
            getEnvironmentVariable: static _ => null,
            getRegisteredMsBuildPath: static () => null,
            enumerateEnvironmentVariables: static () => [],
            getUserProfileDirectory: () => profile.Path);
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            profileConfigPath,
            "[remote \"origin\"]\n\turl = https://example.test/two.git\n");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task git_config_override_disables_reuse_when_source_link_parity_is_unproven()
    {
        using var root = new TemporaryDirectory();
        using var profile = new TemporaryDirectory();
        using var redirected = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var gitDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, ".git"));
        Directory.CreateDirectory(System.IO.Path.Combine(gitDirectory.FullName, "objects"));
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "HEAD"),
            new string('1', 40) + "\n");
        File.WriteAllText(
            System.IO.Path.Combine(gitDirectory.FullName, "config"),
            "[core]\n\trepositoryformatversion = 0\n");
        var overridePath = System.IO.Path.Combine(redirected.Path, "override.gitconfig");
        File.WriteAllText(overridePath, "[remote \"origin\"]\n\turl = https://override.test/repo.git\n");
        var variables = new Dictionary<string, string?>
        {
            ["GIT_CONFIG_GLOBAL"] = overridePath
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            loadConfiguration: null,
            getEnvironmentVariable: variables.GetValueOrDefault,
            getRegisteredMsBuildPath: static () => null,
            enumerateEnvironmentVariables: () => variables,
            getUserProfileDirectory: () => profile.Path);
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task implicit_razor_view_addition_changes_fingerprint()
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Razor\" />");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var views = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "Views"));
        File.WriteAllText(
            System.IO.Path.Combine(views.FullName, "Index.cshtml"),
            "@model string");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Theory]
    [InlineData("MSBuildUserExtensionsPath", true)]
    [InlineData("MSBuildExtensionsPath", false)]
    public async Task default_msbuild_hook_addition_changes_fingerprint(
        string activeRootVariable,
        bool remainsReusable)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var activeRoot = variables[activeRootVariable]!;
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            activeRoot,
            "Current",
            "Imports",
            "Microsoft.Common.props",
            "ImportBefore"));
        File.WriteAllText(
            System.IO.Path.Combine(hookDirectory.FullName, "Feature.props"),
            "<Project><PropertyGroup><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBe(remainsReusable);
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task default_msbuild_hook_dependencies_are_fingerprinted()
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var dependencyPath = System.IO.Path.Combine(external.Path, "Feature.props");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            userExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        File.WriteAllText(
            System.IO.Path.Combine(hookDirectory.FullName, "Feature.targets"),
            $"<Project><Import Project=\"{dependencyPath}\" /></Project>");
        File.WriteAllText(
            dependencyPath,
            "<Project><PropertyGroup><DefineConstants>ONE</DefineConstants></PropertyGroup></Project>");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            dependencyPath,
            "<Project><PropertyGroup><DefineConstants>TWO</DefineConstants></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task late_msbuild_registration_establishes_one_stable_system_hook_baseline()
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path
        };
        string? registeredMsBuildPath = null;
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            new StubLoadConfiguration(null),
            name => variables.GetValueOrDefault(name),
            () => registeredMsBuildPath);
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var beforeRegistration = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        registeredMsBuildPath = systemExtensions.Path;
        var afterRegistration = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        var stable = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);

        beforeRegistration.Reusable.ShouldBeTrue();
        afterRegistration.Reusable.ShouldBeTrue();
        stable.ShouldBe(afterRegistration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task registered_system_hook_change_or_removal_is_not_claimed_reusable(
        bool remove)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        var hookPath = System.IO.Path.Combine(hookDirectory.FullName, "Feature.targets");
        File.WriteAllText(hookPath, "<Project><PropertyGroup><Flag>ONE</Flag></PropertyGroup></Project>");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        if (remove)
            File.Delete(hookPath);
        else
            File.WriteAllText(hookPath, "<Project><PropertyGroup><Flag>TWO</Flag></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeFalse();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task registered_system_hook_literal_dependency_is_fingerprinted()
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var dependencyPath = System.IO.Path.Combine(external.Path, "Feature.props");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        File.WriteAllText(
            System.IO.Path.Combine(hookDirectory.FullName, "Feature.targets"),
            $"<Project><Import Project=\"{dependencyPath}\" /></Project>");
        File.WriteAllText(
            dependencyPath,
            "<Project><PropertyGroup><DefineConstants>ONE</DefineConstants></PropertyGroup></Project>");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            dependencyPath,
            "<Project><PropertyGroup><DefineConstants>TWO</DefineConstants></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Theory]
    [InlineData("<PropertyGroup><DefineConstants>$([System.IO.File]::ReadAllText('/external/flags.txt'))</DefineConstants></PropertyGroup>")]
    [InlineData("<Target Name=\"ReadFlags\"><ReadLinesFromFile File=\"/external/flags.txt\" /></Target>")]
    [InlineData("<ItemGroup><Compile Include=\"../Generated.cs\" /></ItemGroup>")]
    [InlineData("<PropertyGroup><VSTestTargets>/external/Feature.targets</VSTestTargets></PropertyGroup>")]
    [InlineData("<PropertyGroup><BaseOutputPath>out</BaseOutputPath></PropertyGroup>")]
    [InlineData("<PropertyGroup><ArtifactsPath>artifacts</ArtifactsPath></PropertyGroup>")]
    [InlineData("<PropertyGroup><MSBuildProjectExtensionsPath>generated</MSBuildProjectExtensionsPath></PropertyGroup>")]
    [InlineData("<PropertyGroup><ProjectAssetsFile>/external/project.assets.json</ProjectAssetsFile></PropertyGroup>")]
    [InlineData("<PropertyGroup><NuGetPackageRoot>/external/packages</NuGetPackageRoot></PropertyGroup>")]
    [InlineData("<PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup>")]
    [InlineData("<UsingTask TaskName=\"CustomTask\" AssemblyFile=\"/external/Tasks.dll\" />")]
    [InlineData("<Import Project=\"Sdk.props\" Sdk=\"Company.Sdk\" />")]
    [InlineData("<PropertyGroup><CompilePrerequisite>VSTest</CompilePrerequisite><CoreCompileDependsOn>$(CompilePrerequisite);$(CoreCompileDependsOn)</CoreCompileDependsOn></PropertyGroup>")]
    public async Task unsafe_registered_system_hook_body_is_not_claimed_reusable(
        string hookBody)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        File.WriteAllText(
            System.IO.Path.Combine(hookDirectory.FullName, "Feature.targets"),
            $"<Project>{hookBody}</Project>");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("<Import Project=\"Sdk.props\" Sdk=\"Company.Sdk\" />")]
    [InlineData("<PropertyGroup><CompilePrerequisite>VSTest</CompilePrerequisite><CoreCompileDependsOn>$(CompilePrerequisite);$(CoreCompileDependsOn)</CoreCompileDependsOn></PropertyGroup>")]
    public async Task unsafe_registered_system_hook_dependency_is_not_claimed_reusable(
        string dependencyBody)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var dependencyPath = System.IO.Path.Combine(external.Path, "Dependency.targets");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        File.WriteAllText(
            System.IO.Path.Combine(hookDirectory.FullName, "Feature.targets"),
            $"<Project><Import Project=\"{dependencyPath}\" /></Project>");
        File.WriteAllText(dependencyPath, $"<Project>{dependencyBody}</Project>");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task imported_legacy_target_framework_assignment_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var importedPath = System.IO.Path.Combine(external.Path, "Legacy.props");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><Import Project=\"{importedPath}\" /></Project>");
        File.WriteAllText(
            importedPath,
            "<Project><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task unsafe_registered_sdk_selector_dependency_is_not_claimed_reusable(
        bool transitive,
        bool usingTask)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        File.WriteAllText(
            System.IO.Path.Combine(
                hookDirectory.FullName,
                "Microsoft.TestPlatform.ImportAfter.targets"),
            """
            <Project>
              <PropertyGroup>
                <VSTestTargets Condition="'$(VSTestTargets)'==''">$(MSBuildExtensionsPath)\Microsoft.TestPlatform.targets</VSTestTargets>
              </PropertyGroup>
              <Import Condition="Exists('$(VSTestTargets)')" Project="$(VSTestTargets)" />
            </Project>
            """);
        var selectorPath = System.IO.Path.Combine(
            systemExtensions.Path,
            "Microsoft.TestPlatform.targets");
        var unsafeBody = usingTask
            ? "<UsingTask TaskName=\"CustomTask\" AssemblyFile=\"/external/Tasks.dll\" />"
            : "<PropertyGroup><DefineConstants>$([System.IO.File]::ReadAllText('/external/flags.txt'))</DefineConstants></PropertyGroup>";
        if (transitive)
        {
            File.WriteAllText(
                selectorPath,
                "<Project><Import Project=\"Injected.targets\" /></Project>");
            File.WriteAllText(
                System.IO.Path.Combine(systemExtensions.Path, "Injected.targets"),
                $"<Project>{unsafeBody}</Project>");
        }
        else
        {
            File.WriteAllText(selectorPath, $"<Project>{unsafeBody}</Project>");
        }
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task unknown_sdk_declaration_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project Sdk=\"Company.Sdk/1.0.0\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Company.Sdk")]
    [InlineData("Microsoft.NET.Sdk")]
    public async Task sdk_qualified_import_without_a_proven_location_is_not_claimed_reusable(
        string sdk)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><Import Project=\"Sdk.props\" Sdk=\"{sdk}\" /><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("VSTest")]
    [InlineData("$(FirstTarget)")]
    public async Task initial_targets_are_not_claimed_reusable(string initialTargets)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\" InitialTargets=\"{initialTargets}\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task registered_system_hook_initial_targets_are_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        File.WriteAllText(
            System.IO.Path.Combine(hookDirectory.FullName, "Feature.targets"),
            "<Project InitialTargets=\"$(FirstTarget)\"><PropertyGroup><FirstTarget>VSTest</FirstTarget></PropertyGroup></Project>");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task test_platform_task_override_and_compile_scheduling_are_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var taskAssembly = System.IO.Path.Combine(external.Path, "Tasks.dll");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(taskAssembly, "custom task implementation");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net10.0</TargetFramework>
                 <VSTestTaskAssemblyFile>{taskAssembly}</VSTestTaskAssemblyFile>
                 <CoreCompileDependsOn>$(CoreCompileDependsOn);VSTest</CoreCompileDependsOn>
                 <VSTestNoBuild>true</VSTestNoBuild>
               </PropertyGroup>
             </Project>
             """);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task indirect_compile_scheduling_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <CompilePrerequisite>VSTest</CompilePrerequisite>
                <CoreCompileDependsOn>$(CompilePrerequisite);$(CoreCompileDependsOn)</CoreCompileDependsOn>
                <VSTestNoBuild>true</VSTestNoBuild>
              </PropertyGroup>
            </Project>
            """);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>",
        "Microsoft.NET.Build.Extensions.NETFramework.targets",
        "<Project><Target Name=\"Expand\"><PropertyGroup><NETStandardInbox Condition=\"Exists('%(_InboxNETStandardFolders.Identity)\\netstandard.dll')\">true</NETStandardInbox></PropertyGroup></Target></Project>")]
    [InlineData(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net48</TargetFramework><MicrosoftNETBuildExtensionsTasksAssembly>/external/Tasks.dll</MicrosoftNETBuildExtensionsTasksAssembly></PropertyGroup></Project>",
        "Microsoft.NET.Build.Extensions.NETFramework.targets",
        "<Project><UsingTask TaskName=\"GetDependsOnNETStandard\" AssemblyFile=\"$(MicrosoftNETBuildExtensionsTasksAssembly)\" /><Target Name=\"Expand\"><GetDependsOnNETStandard References=\"@(Reference)\" /></Target></Project>")]
    [InlineData(
        "<Project><Import Project=\"Microsoft.Common.targets\" /><ItemGroup><PackageConflictPlatformManifests Include=\"/external/manifest.json\" /></ItemGroup></Project>",
        "Microsoft.NET.Build.Extensions.ConflictResolution.targets",
        "<Project><Target Name=\"Resolve\"><ResolvePackageFileConflicts PackageConflictPlatformManifests=\"@(PackageConflictPlatformManifests)\" /></Target></Project>")]
    public async Task unproven_stock_sdk_task_inputs_are_not_claimed_reusable(
        string projectXml,
        string importedFileName,
        string importedXml)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), projectXml);
        var hookDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            "Microsoft.Common.targets",
            "ImportAfter"));
        File.WriteAllText(
            System.IO.Path.Combine(
                hookDirectory.FullName,
                "Microsoft.NET.Build.Extensions.targets"),
            """
            <Project>
              <PropertyGroup>
                <MicrosoftNETBuildExtensionsTargets Condition="'$(MicrosoftNETBuildExtensionsTargets)' == ''">$(MSBuildExtensionsPath)\Microsoft\Microsoft.NET.Build.Extensions\Microsoft.NET.Build.Extensions.targets</MicrosoftNETBuildExtensionsTargets>
              </PropertyGroup>
              <Import Condition="Exists('$(MicrosoftNETBuildExtensionsTargets)')" Project="$(MicrosoftNETBuildExtensionsTargets)" />
            </Project>
            """);
        var extensionDirectory = Directory.CreateDirectory(System.IO.Path.Combine(
            systemExtensions.Path,
            "Microsoft",
            "Microsoft.NET.Build.Extensions"));
        var condition = importedFileName.Contains("NETFramework", StringComparison.Ordinal)
            ? "'$(TargetFrameworkIdentifier)' == '.NETFramework'"
            : "'$(ResolveAssemblyConflicts)' == 'true' AND '$(UsingMicrosoftNETSdk)' != 'true'";
        File.WriteAllText(
            System.IO.Path.Combine(extensionDirectory.FullName, "Microsoft.NET.Build.Extensions.targets"),
            $"<Project><Import Project=\"{importedFileName}\" Condition=\"{condition}\" /></Project>");
        File.WriteAllText(
            System.IO.Path.Combine(extensionDirectory.FullName, importedFileName),
            importedXml);
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("MSBuildUserExtensionsPath")]
    [InlineData("MSBuildExtensionsPath")]
    [InlineData("MSBuildToolsVersion")]
    [InlineData("VSTestTargets")]
    [InlineData("MicrosoftNETBuildExtensionsTargets")]
    public async Task build_defined_hook_root_or_selector_is_not_claimed_reusable(
        string propertyName)
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "Directory.Build.props"),
            $"<Project><PropertyGroup><{propertyName}>{external.Path}</{propertyName}></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Microsoft.Common.CrossTargeting.targets/ImportBefore/Feature.targets")]
    [InlineData("Microsoft.VisualStudioVersion.v99.Common.props")]
    public async Task omitted_standard_system_hook_addition_is_detected(string relativePath)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var hookPath = System.IO.Path.Combine(
            systemExtensions.Path,
            "Current",
            relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(hookPath)!);
        File.WriteAllText(hookPath, "<Project />");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeFalse();
        after.Value.ShouldNotBe(before.Value);
    }

    [Theory]
    [InlineData("Custom.After.Microsoft.Common.targets")]
    [InlineData("Custom.Before.Microsoft.CSharp.targets")]
    [InlineData("Custom.After.Microsoft.CSharp.targets")]
    [InlineData("Custom.Before.Microsoft.VisualBasic.targets")]
    [InlineData("Custom.After.Microsoft.VisualBasic.targets")]
    public async Task default_custom_language_hook_addition_is_detected(string fileName)
    {
        using var root = new TemporaryDirectory();
        using var userExtensions = new TemporaryDirectory();
        using var systemExtensions = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var variables = new Dictionary<string, string?>
        {
            ["MSBuildToolsVersion"] = "Current",
            ["MSBuildUserExtensionsPath"] = userExtensions.Path,
            ["MSBuildExtensionsPath"] = systemExtensions.Path
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var customDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(systemExtensions.Path, "vCurrent"));
        File.WriteAllText(
            System.IO.Path.Combine(customDirectory.FullName, fileName),
            "<Project><PropertyGroup><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeFalse();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public void cache_telemetry_serializes_bounded_events_and_swallows_trace_sink_failures()
    {
        string? recordedPath = null;
        string? recordedLine = null;
        var telemetry = (IWorkspaceCacheTelemetry)new WorkspaceCacheTelemetry(
            () => "trace.jsonl",
            (path, line) =>
            {
                recordedPath = path;
                recordedLine = line;
            });
        var cacheEvent = new WorkspaceCacheEvent(
            WorkspaceCacheEventKind.Hit,
            "identity",
            "revision",
            3);

        telemetry.Record(cacheEvent);

        recordedPath.ShouldBe("trace.jsonl");
        var line = recordedLine.ShouldNotBeNull();
        line.ShouldContain("\"Kind\":\"Hit\"");
        line.ShouldContain("\"IdentityHash\":\"identity\"");
        foreach (var exception in new Exception[]
                 {
                     new IOException("io"),
                     new UnauthorizedAccessException("denied"),
                     new ArgumentException("argument"),
                     new NotSupportedException("unsupported")
                 })
        {
            Exception? reported = null;
            var failing = (IWorkspaceCacheTelemetry)new WorkspaceCacheTelemetry(
                () => "trace.jsonl",
                (_, _) => throw exception,
                failure => reported = failure);
            Should.NotThrow(() => failing.Record(cacheEvent));
            reported.ShouldBeSameAs(exception);
        }
    }

    [Fact]
    public void environment_fingerprint_changes_with_the_registered_sdk_identity()
    {
        var environment = new MutableEnvironmentIdentity("sdk-one");
        var variables = new Dictionary<string, string?>
        {
            ["Configuration"] = "Debug"
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            environment,
            name => variables.GetValueOrDefault(name));

        var first = fingerprint.CaptureEnvironmentIdentity();
        environment.Value = "sdk-two";
        var second = fingerprint.CaptureEnvironmentIdentity();
        variables["Configuration"] = "Release";
        var third = fingerprint.CaptureEnvironmentIdentity();

        second.ShouldNotBe(first);
        third.ShouldNotBe(second);
    }

    [Theory]
    [InlineData("ImportByWildcardBeforeMicrosoftCSharpTargets")]
    [InlineData("ImportUserLocationsByWildcardAfterMicrosoftVisualBasicTargets")]
    [InlineData("ImportByWildcardBeforeMicrosoftNETFrameworkProps")]
    [InlineData("ImportUserLocationsByWildcardAfterMicrosoftNETFrameworkTargets")]
    [InlineData("CustomBeforeMicrosoftCSharpTargets")]
    [InlineData("DirectoryBuildPropsPath")]
    [InlineData("ProjectAssetsFile")]
    public void environment_fingerprint_includes_standard_hook_controls(string variable)
    {
        var variables = new Dictionary<string, string?>();
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));

        var before = fingerprint.CaptureEnvironmentIdentity();
        variables[variable] = "false";
        var after = fingerprint.CaptureEnvironmentIdentity();

        after.ShouldNotBe(before);
    }

    [Theory]
    [InlineData("CustomBeforeMicrosoftCSharpTargets")]
    [InlineData("DirectoryBuildPropsPath")]
    [InlineData("BaseOutputPath")]
    [InlineData("ProjectAssetsFile")]
    [InlineData("MicrosoftNETBuildExtensionsTasksAssembly")]
    [InlineData("VSTestTaskAssemblyFile")]
    [InlineData("MicrosoftBuildTasksGitAssemblyFile")]
    [InlineData("AssemblySearchPaths")]
    [InlineData("DesignTimeAssemblySearchPaths")]
    [InlineData("CodeAnalysisRuleSet")]
    [InlineData("CodeAnalysisRuleSetDirectories")]
    [InlineData("ResolvedCodeAnalysisRuleSet")]
    [InlineData("AllowedReferenceAssemblyFileExtensions")]
    [InlineData("SignAssembly")]
    [InlineData("AssemblyOriginatorKeyFile")]
    [InlineData("KeyOriginatorFile")]
    [InlineData("KeyContainerName")]
    [InlineData("CryptoKeyFile")]
    [InlineData("CryptoKeyContainer")]
    [InlineData("DelaySign")]
    [InlineData("PublicSign")]
    [InlineData("CoreCompileDependsOn")]
    [InlineData("GIT_DIR")]
    public async Task environment_path_override_is_not_claimed_reusable(string variable)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var variables = new Dictionary<string, string?>
        {
            [variable] = "/external/value"
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            name => variables.GetValueOrDefault(name));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task environment_property_names_use_msbuild_case_insensitive_semantics()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["baseoutputpath"] = "/external/value"
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            loadConfiguration: null,
            name => variables.GetValueOrDefault(name),
            getRegisteredMsBuildPath: static () => null,
            () => variables);
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = fingerprint.CaptureEnvironmentIdentity();
        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        variables["baseoutputpath"] = "/external/changed";
        var after = fingerprint.CaptureEnvironmentIdentity();

        result.Reusable.ShouldBeFalse();
        after.ShouldNotBe(before);
    }

    [Fact]
    public async Task ambiguous_environment_property_case_variants_are_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["BaseOutputPath"] = "/external/one",
            ["baseoutputpath"] = "/external/two"
        };
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            loadConfiguration: null,
            name => variables.GetValueOrDefault(name),
            getRegisteredMsBuildPath: static () => null,
            () => variables);
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task ancestor_build_configuration_literal_import_change_changes_the_fingerprint()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var sharedProps = System.IO.Path.Combine(outer.Path, "shared.props");
        File.WriteAllText(solutionPath, "<Solution />");
        File.WriteAllText(
            System.IO.Path.Combine(outer.Path, "Directory.Build.props"),
            "<Project><Import Project=\"shared.props\" /></Project>");
        File.WriteAllText(
            sharedProps,
            "<Project><PropertyGroup><DefineConstants>ONE</DefineConstants></PropertyGroup></Project>");
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            sharedProps,
            "<Project><PropertyGroup><DefineConstants>TWO</DefineConstants></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        after.Value.ShouldNotBe(before.Value);
        after.Reusable.ShouldBeTrue();
    }

    [Fact]
    public async Task dynamic_ancestor_build_configuration_is_not_reusable()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        File.WriteAllText(
            System.IO.Path.Combine(outer.Path, "Directory.Build.props"),
            "<Project><Import Project=\"$(UnprovenRoot)/shared.props\" /></Project>");
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task declared_external_inputs_and_missing_imports_are_tracked()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var projectPath = System.IO.Path.Combine(repositoryRoot, "App.csproj");
        File.WriteAllText(solutionPath, "<Solution />");
        File.WriteAllText(
            projectPath,
            """
            <Project>
              <Import Project="; ../missing.props ;" />
              <ItemGroup>
                <Analyzer Include="../analyzer.bin" />
                <AdditionalFiles Include="../additional.txt" />
                <EditorConfigFiles Include="../external.editorconfig" />
                <Compile Include="../external.cs" />
                <Reference Include="External"><HintPath>../reference.dll</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        string[] externalInputs =
        [
            "analyzer.bin",
            "additional.txt",
            "external.editorconfig",
            "external.cs",
            "reference.dll"
        ];
        foreach (var input in externalInputs)
            File.WriteAllText(System.IO.Path.Combine(outer.Path, input), input);
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());

        var previous = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var missingImport = System.IO.Path.Combine(outer.Path, "missing.props");
        File.WriteAllText(
            missingImport,
            "<Project><Import Project=\"repository/App.csproj\" /></Project>");
        var afterImport = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        afterImport.Value.ShouldNotBe(previous.Value);
        afterImport.Reusable.ShouldBeTrue();
        previous = afterImport;

        foreach (var input in externalInputs)
        {
            await File.AppendAllTextAsync(System.IO.Path.Combine(outer.Path, input), "-changed");
            var changed = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
            changed.Value.ShouldNotBe(previous.Value, input);
            previous = changed;
        }
    }

    [Theory]
    [InlineData("$(Undefined)/shared.props")]
    [InlineData("*.props")]
    public async Task dynamic_or_wildcard_imports_make_the_fingerprint_non_reusable(string import)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><Import Project=\"{import}\" /></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("$(Unproven)/App.csproj")]
    [InlineData("*.csproj")]
    [InlineData("?pp.csproj")]
    public async Task dynamic_or_wildcard_solution_projects_are_not_reusable(string projectPath)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(
            solutionPath,
            $"<Solution><Project Path=\"{projectPath}\" /></Solution>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task project_directory_property_in_an_imported_file_is_not_claimed_reusable()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var sharedRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "shared", "nested")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution />");
        File.WriteAllText(
            System.IO.Path.Combine(repositoryRoot, "App.csproj"),
            "<Project><Import Project=\"../shared/nested/Build.props\" /></Project>");
        File.WriteAllText(
            System.IO.Path.Combine(sharedRoot, "Build.props"),
            "<Project><Import Project=\"$(MSBuildProjectDirectory)/../flags/Flags.props\" /></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task imported_file_with_an_unsupported_extension_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><Import Project=\"shared/build.xml\" /></Project>");
        var shared = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "shared"));
        File.WriteAllText(
            System.IO.Path.Combine(shared.FullName, "build.xml"),
            "<Project><Import Project=\"nested.props\" /></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task generated_obj_import_graphs_are_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var obj = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "obj"));
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><Import Project=\"obj/generated.props\" /></Project>");
        File.WriteAllText(
            System.IO.Path.Combine(obj.FullName, "generated.props"),
            "<Project />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task implicit_nuget_restore_and_generated_import_inputs_are_fingerprinted()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var packageRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "packages")).FullName;
        var packageBuildRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(packageRoot, "sample", "1.0.0", "build")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var projectPath = System.IO.Path.Combine(repositoryRoot, "App.csproj");
        var obj = Directory.CreateDirectory(System.IO.Path.Combine(repositoryRoot, "obj"));
        var assetsPath = System.IO.Path.Combine(obj.FullName, "project.assets.json");
        var generatedPropsPath = System.IO.Path.Combine(obj.FullName, "App.csproj.nuget.g.props");
        var generatedTargetsPath = System.IO.Path.Combine(obj.FullName, "App.csproj.nuget.g.targets");
        var packageTargetDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(packageBuildRoot, "net10.0"));
        var packageTargetsPath = System.IO.Path.Combine(packageTargetDirectory.FullName, "Package.targets");
        var siblingTargetsPath = System.IO.Path.Combine(packageBuildRoot, "Sibling.targets");
        var externalPropsPath = System.IO.Path.Combine(outer.Path, "External.props");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(assetsPath, "{\"version\":1}");
        File.WriteAllText(
            generatedPropsPath,
            $"<Project><PropertyGroup><NuGetPackageRoot>{packageRoot}</NuGetPackageRoot></PropertyGroup></Project>");
        File.WriteAllText(
            generatedTargetsPath,
            "<Project><Import Project=\"$(NuGetPackageRoot)sample/1.0.0/build/net10.0/Package.targets\" Condition=\"Exists('$(NuGetPackageRoot)sample/1.0.0/build/net10.0/Package.targets')\" /></Project>");
        File.WriteAllText(
            packageTargetsPath,
            $"<Project><Import Project=\"{externalPropsPath}\" /><PropertyGroup><Flag>one</Flag></PropertyGroup></Project>");
        File.WriteAllText(siblingTargetsPath, "<Project><PropertyGroup><Sibling>one</Sibling></PropertyGroup></Project>");
        File.WriteAllText(externalPropsPath, "<Project><PropertyGroup><External>one</External></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);

        var original = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(assetsPath, "{\"version\":2}");
        var afterAssets = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            packageTargetsPath,
            $"<Project><Import Project=\"{externalPropsPath}\" /><PropertyGroup><Flag>two</Flag></PropertyGroup></Project>");
        var afterPackageTargets = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        File.WriteAllText(siblingTargetsPath, "<Project><PropertyGroup><Sibling>two</Sibling></PropertyGroup></Project>");
        var afterSiblingTargets = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        File.WriteAllText(externalPropsPath, "<Project><PropertyGroup><External>two</External></PropertyGroup></Project>");
        var afterExternalProps = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);

        original.Reusable.ShouldBeTrue();
        afterAssets.Reusable.ShouldBeTrue();
        afterPackageTargets.Reusable.ShouldBeTrue();
        afterSiblingTargets.Reusable.ShouldBeTrue();
        afterExternalProps.Reusable.ShouldBeTrue();
        afterAssets.Value.ShouldNotBe(original.Value);
        afterPackageTargets.Value.ShouldNotBe(afterAssets.Value);
        afterSiblingTargets.Value.ShouldNotBe(afterPackageTargets.Value);
        afterExternalProps.Value.ShouldNotBe(afterSiblingTargets.Value);
    }

    [Fact]
    public async Task imported_nuget_package_root_override_is_not_claimed_reusable()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var packageRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "packages-a")).FullName;
        var fallbackRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "packages-b")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var obj = Directory.CreateDirectory(System.IO.Path.Combine(repositoryRoot, "obj"));
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(repositoryRoot, "App.csproj"), "<Project />");
        File.WriteAllText(
            System.IO.Path.Combine(repositoryRoot, "Directory.Build.props"),
            $"<Project><PropertyGroup><NuGetPackageRoot>{packageRoot}</NuGetPackageRoot></PropertyGroup></Project>");
        File.WriteAllText(
            System.IO.Path.Combine(obj.FullName, "App.csproj.nuget.g.props"),
            $"<Project><PropertyGroup><NuGetPackageRoot Condition=\"'$(NuGetPackageRoot)' == ''\">{fallbackRoot}</NuGetPackageRoot></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task loaded_project_implicit_restore_inputs_are_fingerprinted_when_not_declared_by_the_solution()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var projectPath = System.IO.Path.Combine(root.Path, "Loaded.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var obj = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "obj"));
        var assetsPath = System.IO.Path.Combine(obj.FullName, "project.assets.json");
        File.WriteAllText(assetsPath, "{\"version\":1}");
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "Loaded",
            "Loaded",
            LanguageNames.CSharp,
            filePath: projectPath));
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var original = await fingerprint.CaptureAsync(identity, solution, CancellationToken.None);
        File.WriteAllText(assetsPath, "{\"version\":2}");
        var changed = await fingerprint.CaptureAsync(identity, solution, CancellationToken.None);

        original.Reusable.ShouldBeTrue();
        changed.Reusable.ShouldBeTrue();
        changed.Value.ShouldNotBe(original.Value);
    }

    [Theory]
    [InlineData("key-file")]
    [InlineData("key-container")]
    [InlineData("public-key")]
    [InlineData("delay-sign")]
    [InlineData("public-sign")]
    public async Task loaded_compiler_signing_options_are_not_claimed_reusable(string setting)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        compilationOptions = setting switch
        {
            "key-file" => compilationOptions.WithCryptoKeyFile(
                System.IO.Path.Combine(root.Path, "Key.snk")),
            "key-container" => compilationOptions.WithCryptoKeyContainer("external-key"),
            "public-key" => compilationOptions.WithCryptoPublicKey(ImmutableArray.Create<byte>(1)),
            "delay-sign" => compilationOptions.WithDelaySign(true),
            "public-sign" => compilationOptions.WithPublicSign(true),
            _ => throw new InvalidOperationException($"Unexpected signing setting: {setting}")
        };
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Loaded",
            "Loaded",
            LanguageNames.CSharp,
            filePath: System.IO.Path.Combine(root.Path, "Loaded.csproj"),
            compilationOptions: compilationOptions));
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("AssemblyKey\\u0046ileAttribute")]
    [InlineData("AssemblyKeyNameAttribute")]
    public async Task source_signing_attributes_are_not_claimed_reusable(string attributeName)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var projectPath = System.IO.Path.Combine(root.Path, "Loaded.csproj");
        var sourcePath = System.IO.Path.Combine(root.Path, "Signing.cs");
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Loaded",
                "Loaded",
                LanguageNames.CSharp,
                filePath: projectPath))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Signing.cs",
                SourceText.From($"[assembly: System.Reflection.{attributeName}(\"external-key\")]"),
                filePath: sourcePath)
            .AddMetadataReference(
                projectId,
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task signing_attribute_name_in_a_string_does_not_disable_reuse()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var projectPath = System.IO.Path.Combine(root.Path, "Loaded.csproj");
        var sourcePath = System.IO.Path.Combine(root.Path, "Literal.cs");
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Loaded",
                "Loaded",
                LanguageNames.CSharp,
                filePath: projectPath))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Literal.cs",
                SourceText.From("class Literal { const string Value = \"AssemblyKeyFile\"; }"),
                filePath: sourcePath);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution, CancellationToken.None);

        result.Reusable.ShouldBeTrue();
    }

    [Fact]
    public async Task generated_source_signing_attribute_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var projectPath = System.IO.Path.Combine(root.Path, "Loaded.csproj");
        var sourcePath = System.IO.Path.Combine(root.Path, "Marker.cs");
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var baselineSolution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Loaded",
                "Loaded",
                LanguageNames.CSharp,
                filePath: projectPath))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Marker.cs",
                SourceText.From("class Marker;"),
                filePath: sourcePath)
            .AddMetadataReference(
                projectId,
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        var generatedSolution = baselineSolution.AddAnalyzerReference(
            projectId,
            new FixedSourceGeneratorReference(
                "[assembly: System.Reflection.AssemblyKeyNameAttribute(\"external-key\")]")
        );
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var baseline = await fingerprint.CaptureAsync(
            identity,
            baselineSolution,
            CancellationToken.None);
        var result = await fingerprint.CaptureAsync(
            identity,
            generatedSolution,
            CancellationToken.None);

        baseline.Reusable.ShouldBeTrue();
        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task literal_filesystem_condition_inputs_are_fingerprinted()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var conditionPath = System.IO.Path.Combine(root.Path, "feature.flag");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><PropertyGroup Condition=\"Exists('$(MSBuildProjectDirectory)/feature.flag')\"><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var missing = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(conditionPath, "enabled");
        var present = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        missing.Reusable.ShouldBeTrue();
        present.Reusable.ShouldBeTrue();
        present.Value.ShouldNotBe(missing.Value);
    }

    [Fact]
    public async Task literal_directory_existence_condition_distinguishes_missing_and_directory_states()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var conditionPath = System.IO.Path.Combine(root.Path, "feature-directory");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><PropertyGroup Condition=\"Exists('feature-directory')\"><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var missing = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        Directory.CreateDirectory(conditionPath);
        var present = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        missing.Reusable.ShouldBeTrue();
        present.Reusable.ShouldBeTrue();
        present.Value.ShouldNotBe(missing.Value);
    }

    [Fact]
    public async Task imported_relative_exists_condition_is_not_claimed_reusable_without_project_context()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><Import Project=\"build/Imported.props\" /></Project>");
        var buildDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "build"));
        File.WriteAllText(
            System.IO.Path.Combine(buildDirectory.FullName, "Imported.props"),
            "<Project><PropertyGroup Condition=\"Exists('feature.flag')\"><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task literal_custom_project_extensions_path_tracks_restore_inputs()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><PropertyGroup><MSBuildProjectExtensionsPath>.build/cache</MSBuildProjectExtensionsPath></PropertyGroup></Project>");
        var extensions = Directory.CreateDirectory(
            System.IO.Path.Combine(root.Path, ".build", "cache"));
        var assetsPath = System.IO.Path.Combine(extensions.FullName, "project.assets.json");
        File.WriteAllText(assetsPath, "{\"version\":1}");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var original = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(assetsPath, "{\"version\":2}");
        var changed = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        original.Reusable.ShouldBeTrue();
        changed.Reusable.ShouldBeTrue();
        changed.Value.ShouldNotBe(original.Value);
    }

    [Fact]
    public async Task imported_project_extensions_path_is_not_guessed_from_the_conventional_obj_directory()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "Directory.Build.props"),
            "<Project><PropertyGroup><MSBuildProjectExtensionsPath>../external-obj</MSBuildProjectExtensionsPath></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task loader_redirected_restore_path_is_not_claimed_reusable_from_project_obj()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var redirectedObj = Directory.CreateDirectory(
            System.IO.Path.Combine(root.Path, "redirected-obj")).FullName;
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity(),
            new StubLoadConfiguration(redirectedObj));
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task dynamic_project_extensions_path_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><PropertyGroup><BaseIntermediateOutputPath>$(UnprovenRoot)/obj</BaseIntermediateOutputPath></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("<MSBuildProjectExtensionsPath Condition=\"'$(Configuration)' == 'Debug'\">.build/cache</MSBuildProjectExtensionsPath>")]
    [InlineData("<MSBuildProjectExtensionsPath>*cache</MSBuildProjectExtensionsPath>")]
    [InlineData("<MSBuildProjectExtensionsPath>cache%20space</MSBuildProjectExtensionsPath>")]
    [InlineData("<baseintermediateoutputpath>$(UnprovenRoot)/obj</baseintermediateoutputpath>")]
    [InlineData("<ProjectAssetsFile>$(RestoreRoot)/project.assets.json</ProjectAssetsFile>")]
    [InlineData("<ProjectAssetsFile>cache%20space/project.assets.json</ProjectAssetsFile>")]
    public async Task conditional_or_dynamic_restore_path_is_not_claimed_reusable(
        string property)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup>{property}</PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task literal_project_assets_file_override_is_fingerprinted()
    {
        using var root = new TemporaryDirectory();
        using var restore = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var assetsPath = System.IO.Path.Combine(restore.Path, "project.assets.json");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup><ProjectAssetsFile>{assetsPath}</ProjectAssetsFile></PropertyGroup></Project>");
        File.WriteAllText(assetsPath, "{ \"version\": 1 }");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(assetsPath, "{ \"version\": 2 }");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task generated_nuget_assets_assignment_remains_reusable_and_fingerprinted()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var projectPath = System.IO.Path.Combine(root.Path, "App.csproj");
        var obj = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "obj")).FullName;
        var assetsPath = System.IO.Path.Combine(obj, "project.assets.json");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(projectPath, "<Project />");
        File.WriteAllText(
            System.IO.Path.Combine(obj, "App.csproj.nuget.g.props"),
            """
            <Project>
              <PropertyGroup>
                <ProjectAssetsFile Condition="'$(ProjectAssetsFile)' == ''">$(MSBuildThisFileDirectory)project.assets.json</ProjectAssetsFile>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(assetsPath, "{ \"version\": 1 }");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(assetsPath, "{ \"version\": 2 }");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Theory]
    [InlineData("Condition", "<PropertyGroup Condition=\"Exists('../feature.flag')\"><DefineConstants>FEATURE</DefineConstants></PropertyGroup>")]
    [InlineData("Item", "<ItemGroup><Compile Include=\"../Generated.cs\" /></ItemGroup>")]
    public async Task generated_nuget_relative_project_context_is_not_claimed_reusable(
        string _,
        string generatedBody)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var obj = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "obj")).FullName;
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        File.WriteAllText(
            System.IO.Path.Combine(obj, "App.csproj.nuget.g.props"),
            $"<Project>{generatedBody}</Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ProjectAssetsFile")]
    [InlineData("MSBuildProjectExtensionsPath")]
    public async Task conditionally_ancestral_restore_assignment_is_not_claimed_reusable(
        string propertyName)
    {
        using var root = new TemporaryDirectory();
        using var restore = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var activePath = System.IO.Path.Combine(restore.Path, "active");
        var inactivePath = System.IO.Path.Combine(restore.Path, "inactive");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"""
            <Project>
              <PropertyGroup><{propertyName}>{activePath}</{propertyName}></PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' == 'Never'">
                <{propertyName}>{inactivePath}</{propertyName}>
              </PropertyGroup>
            </Project>
            """);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ProjectAssetsFile")]
    [InlineData("MSBuildProjectExtensionsPath")]
    public async Task target_scoped_restore_assignment_is_not_claimed_reusable(
        string propertyName)
    {
        using var root = new TemporaryDirectory();
        using var restore = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var activePath = System.IO.Path.Combine(restore.Path, "active");
        var inactivePath = System.IO.Path.Combine(restore.Path, "inactive");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"""
            <Project>
              <PropertyGroup><{propertyName}>{activePath}</{propertyName}></PropertyGroup>
              <Target Name="NeverRuns">
                <PropertyGroup><{propertyName}>{inactivePath}</{propertyName}></PropertyGroup>
              </Target>
            </Project>
            """);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("artifacts")]
    [InlineData("examples/bin")]
    [InlineData("examples/obj")]
    [InlineData("node_modules")]
    [InlineData("TestResults")]
    public async Task source_addition_in_non_universal_exclusion_changes_fingerprint(
        string directoryName)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var addedDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(root.Path, directoryName)).FullName;
        File.WriteAllText(System.IO.Path.Combine(addedDirectory, "NewType.cs"), "class NewType;");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    public async Task generated_file_in_default_project_output_directory_does_not_change_fingerprint(
        string directoryName)
    {
        using var root = new TemporaryDirectory();
        var projectDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(root.Path, "src", "App")).FullName;
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(
            solutionPath,
            "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(projectDirectory, "App.csproj"), "<Project />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var generatedDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(projectDirectory, directoryName, "Debug", "net10.0")).FullName;
        File.WriteAllText(
            System.IO.Path.Combine(generatedDirectory, "Generated.cs"),
            "class Generated;");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.ShouldBe(before);
    }

    [Theory]
    [InlineData("BaseOutputPath")]
    [InlineData("OutputPath")]
    [InlineData("BaseIntermediateOutputPath")]
    [InlineData("MSBuildProjectExtensionsPath")]
    [InlineData("DefaultItemExcludes")]
    [InlineData("DefaultExcludesInProjectFolder")]
    [InlineData("EnableDefaultItems")]
    [InlineData("EnableDefaultCompileItems")]
    [InlineData("defaultitemexcludes")]
    public async Task imported_default_output_customization_is_not_claimed_reusable(
        string propertyName)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "Directory.Build.props"),
            $"<Project><PropertyGroup><{propertyName}>custom</{propertyName}></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task nested_project_output_visible_to_ancestor_project_changes_fingerprint()
    {
        using var root = new TemporaryDirectory();
        var innerDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(root.Path, "Inner")).FullName;
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(
            solutionPath,
            "<Solution><Project Path=\"Outer.csproj\" /><Project Path=\"Inner/Inner.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "Outer.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><Compile Remove=\"Inner/obj/**/*.cs\" /></ItemGroup></Project>");
        File.WriteAllText(
            System.IO.Path.Combine(innerDirectory, "Inner.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var generatedDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(innerDirectory, "bin")).FullName;
        File.WriteAllText(
            System.IO.Path.Combine(generatedDirectory, "NewType.cs"),
            "class NewType;");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task imported_artifacts_output_configuration_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "Directory.Build.props"),
            $"<Project><PropertyGroup><ArtifactsPath>{external.Path}</ArtifactsPath></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("DirectoryBuildPropsPath")]
    [InlineData("DirectoryBuildTargetsPath")]
    [InlineData("CustomBeforeDirectoryBuildProps")]
    [InlineData("CustomAfterDirectoryBuildProps")]
    [InlineData("CustomBeforeDirectoryBuildTargets")]
    [InlineData("CustomAfterDirectoryBuildTargets")]
    [InlineData("CustomBeforeMicrosoftCommonProps")]
    [InlineData("CustomAfterMicrosoftCommonProps")]
    [InlineData("CustomBeforeMicrosoftCommonTargets")]
    [InlineData("CustomAfterMicrosoftCommonTargets")]
    [InlineData("directorybuildtargetspath")]
    public async Task sdk_implicit_import_path_override_is_not_claimed_reusable(
        string propertyName)
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var externalBuildInput = System.IO.Path.Combine(external.Path, "Build.targets");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(externalBuildInput, "<Project />");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><{propertyName}>{externalBuildInput}</{propertyName}></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("@(SharedSources)")]
    [InlineData("%(SharedSources.Identity)")]
    public async Task msbuild_item_or_metadata_expression_is_not_claimed_reusable(
        string expression)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><ItemGroup><Compile Include=\"{expression}\" /></ItemGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task msbuild_percent_escaped_import_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><Import Project=\"Build%20Flags.props\" /></Project>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "Build Flags.props"), "<Project />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("compile", "Source.cs")]
    [InlineData("import", "Build.props")]
    public async Task semantic_item_and_import_names_are_case_insensitive(
        string elementName,
        string externalFileName)
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var externalPath = System.IO.Path.Combine(external.Path, externalFileName);
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            elementName == "import"
                ? $"<Project><{elementName} Project=\"{externalPath}\" /></Project>"
                : $"<Project><ItemGroup><{elementName} Include=\"{externalPath}\" /></ItemGroup></Project>");
        File.WriteAllText(
            externalPath,
            elementName == "import"
                ? "<Project><PropertyGroup><DefineConstants>ONE</DefineConstants></PropertyGroup></Project>"
                : "class Source;");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            externalPath,
            elementName == "import"
                ? "<Project><PropertyGroup><DefineConstants>TWO</DefineConstants></PropertyGroup></Project>"
                : "class Changed;");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task explicit_project_extensions_path_precedes_base_intermediate_output_path()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var implicitProps = System.IO.Path.Combine(external.Path, "App.csproj.Feature.props");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup><MSBuildProjectExtensionsPath>{external.Path}</MSBuildProjectExtensionsPath><BaseIntermediateOutputPath>obj</BaseIntermediateOutputPath></PropertyGroup></Project>");
        File.WriteAllText(implicitProps, "<Project><PropertyGroup><DefineConstants>ONE</DefineConstants></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            implicitProps,
            "<Project><PropertyGroup><DefineConstants>TWO</DefineConstants></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        before.Reusable.ShouldBeTrue();
        after.Reusable.ShouldBeTrue();
        after.Value.ShouldNotBe(before.Value);
    }

    [Theory]
    [InlineData("Compile", "Source.cs")]
    [InlineData("ProjectReference", "Referenced.csproj")]
    [InlineData("Analyzer", "Analyzer.dll")]
    public async Task relative_item_in_import_without_project_context_is_not_claimed_reusable(
        string itemName,
        string relativePath)
    {
        using var root = new TemporaryDirectory();
        var buildDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(root.Path, "build")).FullName;
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><Import Project=\"build/Shared.props\" /></Project>");
        File.WriteAllText(
            System.IO.Path.Combine(buildDirectory, "Shared.props"),
            $"<Project><ItemGroup><{itemName} Include=\"{relativePath}\" /></ItemGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task implicit_project_extension_import_addition_changes_fingerprint()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(root.Path, "App.csproj"), "<Project />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        var obj = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "obj")).FullName;
        File.WriteAllText(
            System.IO.Path.Combine(obj, "App.csproj.Feature.props"),
            "<Project><PropertyGroup><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        after.Reusable.ShouldBeFalse();
        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task worktree_globalconfig_addition_changes_fingerprint()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, ".globalconfig"),
            "is_global = true");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task ancestor_editorconfig_addition_changes_fingerprint()
    {
        using var outer = new TemporaryDirectory();
        var repository = Directory.CreateDirectory(System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var solutionPath = System.IO.Path.Combine(repository, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(System.IO.Path.Combine(repository, "App.csproj"), "<Project />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repository);

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(
            System.IO.Path.Combine(outer.Path, ".editorconfig"),
            "root = true");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task authored_target_task_with_unenumerated_input_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        var flagsPath = System.IO.Path.Combine(external.Path, "flags.txt");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><Import Project=\"Build.targets\" /></Project>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "Build.targets"),
            $"""
            <Project>
              <Target Name="ReadFlags" BeforeTargets="CoreCompile">
                <ReadLinesFromFile File="{flagsPath}">
                  <Output TaskParameter="Lines" PropertyName="DefineConstants" />
                </ReadLinesFromFile>
              </Target>
            </Project>
            """);
        File.WriteAllText(flagsPath, "FEATURE");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task authored_source_link_task_assembly_override_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(root.Path, "App.slnx");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"App.csproj\" /></Solution>");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <MicrosoftBuildTasksGitAssemblyFile>/external/Tasks.dll</MicrosoftBuildTasksGitAssemblyFile>
              </PropertyGroup>
            </Project>
            """);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task dynamic_filesystem_condition_is_not_claimed_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            "<Project><PropertyGroup Condition=\"Exists('$(UnprovenRoot)/feature.flag')\"><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("$([System.IO.File]::Exists('feature.flag'))")]
    [InlineData("'$([MSBuild]::GetPathOfFileAbove(Directory.Build.props))' != ''")]
    [InlineData("Exists($(UnquotedPath))")]
    public async Task unprovable_filesystem_condition_forms_are_not_claimed_reusable(string condition)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup Condition=\"{condition}\"><DefineConstants>FEATURE</DefineConstants></PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Theory]
    [InlineData("<DefineConstants>$([System.IO.File]::ReadAllText('/shared/flags.txt'))</DefineConstants>")]
    [InlineData("<ReadLinesFromFile File=\"$([MSBuild]::GetPathOfFileAbove('flags.txt'))\" />")]
    [InlineData("<DefineConstants>$([MSBuild]::GetDirectoryNameOfFileAbove('$(MSBuildThisFileDirectory)', 'feature.flag'))</DefineConstants>")]
    public async Task filesystem_dependent_values_outside_conditions_are_not_claimed_reusable(
        string expression)
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        File.WriteAllText(
            System.IO.Path.Combine(root.Path, "App.csproj"),
            $"<Project><PropertyGroup>{expression}</PropertyGroup></Project>");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        var result = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        result.Reusable.ShouldBeFalse();
    }

    [Fact]
    public async Task fingerprint_reads_allow_concurrent_readers_and_reject_invalid_build_xml()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var projectPath = System.IO.Path.Combine(root.Path, "App.csproj");
        File.WriteAllText(projectPath, "<Project />");
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);
        await using (var held = new FileStream(
                         projectPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete))
        {
            await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        }

        File.WriteAllText(projectPath, "<Project>");
        var exception = await Should.ThrowAsync<IOException>(
            () => fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None));
        exception.Message.ShouldContain("could not be parsed consistently");
    }

    [Fact]
    public void stable_file_check_rejects_each_unprovable_post_read_state()
    {
        var timestamp = DateTime.UtcNow;
        Should.NotThrow(() => WorkspaceInputFingerprint.EnsureFileUnchanged(
            true, 10, 10, timestamp, timestamp, "input.cs"));

        Should.Throw<IOException>(() => WorkspaceInputFingerprint.EnsureFileUnchanged(
            false, 10, 10, timestamp, timestamp, "input.cs"));
        Should.Throw<IOException>(() => WorkspaceInputFingerprint.EnsureFileUnchanged(
            true, 10, 11, timestamp, timestamp, "input.cs"));
        Should.Throw<IOException>(() => WorkspaceInputFingerprint.EnsureFileUnchanged(
            true, 10, 10, timestamp, timestamp.AddTicks(1), "input.cs"));
    }

    [Fact]
    public async Task physical_fingerprint_includes_loaded_solution_inputs_outside_the_worktree()
    {
        using var root = new TemporaryDirectory();
        using var external = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        var externalSource = System.IO.Path.Combine(external.Path, "Generated.cs");
        var externalProject = System.IO.Path.Combine(external.Path, "App.csproj");
        var analyzerPath = System.IO.Path.Combine(external.Path, "Analyzer.dll");
        var metadataPath = System.IO.Path.Combine(external.Path, "Reference.dll");
        File.WriteAllText(externalSource, "class Generated;");
        File.WriteAllText(externalProject, "<Project />");
        File.Copy(typeof(ReadOnlyWorkspaceSessionCacheTests).Assembly.Location, analyzerPath);
        File.Copy(typeof(Enumerable).Assembly.Location, metadataPath);
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "App",
                "App",
                LanguageNames.CSharp,
                filePath: externalProject))
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "Generated.cs",
                SourceText.From("class Generated;"),
                filePath: externalSource)
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(metadataPath))
            .AddAnalyzerReference(
                projectId,
                new AnalyzerFileReference(analyzerPath, new StubAnalyzerAssemblyLoader()));
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", root.Path);

        WorkspaceInputFingerprintResult original;
        await using (var heldSource = new FileStream(
                         externalSource,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete))
        {
            original = await fingerprint.CaptureAsync(identity, solution, CancellationToken.None);
        }
        original.SnapshotConsistent.ShouldBeTrue();
        File.WriteAllText(externalProject, "<Project><PropertyGroup /></Project>");
        var afterProjectChange = await fingerprint.CaptureAsync(
            identity,
            solution,
            CancellationToken.None);
        afterProjectChange.Value.ShouldNotBe(original.Value);

        File.WriteAllText(externalSource, "class Generated { int Value; }");
        var afterSourceChange = await fingerprint.CaptureAsync(
            identity,
            solution,
            CancellationToken.None);
        afterSourceChange.Value.ShouldNotBe(afterProjectChange.Value);
        afterSourceChange.SnapshotConsistent.ShouldBeFalse();

        await File.AppendAllTextAsync(metadataPath, "metadata-change");
        var afterMetadataChange = await fingerprint.CaptureAsync(
            identity,
            solution,
            CancellationToken.None);
        afterMetadataChange.Value.ShouldNotBe(afterSourceChange.Value);

        await File.AppendAllTextAsync(analyzerPath, "analyzer-change");
        var afterAnalyzerChange = await fingerprint.CaptureAsync(
            identity,
            solution,
            CancellationToken.None);

        afterAnalyzerChange.Value.ShouldNotBe(afterMetadataChange.Value);
    }

    [Fact]
    public async Task solution_declared_external_project_and_import_are_tracked_before_load()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var externalRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "external")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var projectPath = System.IO.Path.Combine(externalRoot, "App.csproj");
        var sharedProps = System.IO.Path.Combine(externalRoot, "shared.props");
        File.WriteAllText(
            solutionPath,
            "<Solution><Project Path=\"../external/App.csproj\" /></Solution>");
        File.WriteAllText(projectPath, "<Project><Import Project=\"shared.props\" /></Project>");
        File.WriteAllText(sharedProps, "<Project><PropertyGroup><Value>one</Value></PropertyGroup></Project>");
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());

        var original = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(sharedProps, "<Project><PropertyGroup><Value>two</Value></PropertyGroup></Project>");
        var afterImportChange = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        File.WriteAllText(projectPath, "<Project><PropertyGroup><Value>three</Value></PropertyGroup></Project>");
        var afterProjectChange = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);

        afterImportChange.Value.ShouldNotBe(original.Value);
        afterProjectChange.Value.ShouldNotBe(afterImportChange.Value);
    }

    [Fact]
    public async Task legacy_solution_declared_external_project_is_tracked_before_load()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var externalRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "external")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.sln");
        var projectPath = System.IO.Path.Combine(externalRoot, "App.csproj");
        File.WriteAllText(
            solutionPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "..\external\App.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Global
            EndGlobal
            """);
        File.WriteAllText(projectPath, "<Project />");
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(projectPath, "<Project><PropertyGroup /></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        after.Value.ShouldNotBe(before.Value);
    }

    [Fact]
    public async Task external_literal_import_change_refreshes_the_semantic_snapshot()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var projectPath = System.IO.Path.Combine(repositoryRoot, "App.csproj");
        var sharedProps = System.IO.Path.Combine(outer.Path, "shared.props");
        File.WriteAllText(solutionPath, "<Solution />");
        File.WriteAllText(
            projectPath,
            "<Project><Import Project=\"../shared.props\" /></Project>");
        File.WriteAllText(sharedProps, "<Project><PropertyGroup><Value>one</Value></PropertyGroup></Project>");
        using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
        var loader = new ExternalImportLoader(sharedProps);
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new WorkspaceInputFingerprint(new StubEnvironmentIdentity()),
            new RecordingTelemetry());

        string firstRevision;
        using (var first = await cache.AcquireAsync(solutionPath))
            firstRevision = first.SourceRevision;
        using (await cache.AcquireAsync(solutionPath))
        {
        }
        loader.LoadCount.ShouldBe(1);

        File.WriteAllText(sharedProps, "<Project><PropertyGroup><Value>two</Value></PropertyGroup></Project>");
        using var refreshed = await cache.AcquireAsync(solutionPath);

        loader.LoadCount.ShouldBe(2);
        refreshed.SourceRevision.ShouldNotBe(firstRevision);
    }

    [Fact]
    public async Task backslash_msbuild_import_tracks_the_physical_file_on_every_platform()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var sharedRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "shared")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var projectPath = System.IO.Path.Combine(repositoryRoot, "App.csproj");
        var sharedProps = System.IO.Path.Combine(sharedRoot, "Flags.props");
        File.WriteAllText(solutionPath, "<Solution />");
        File.WriteAllText(
            projectPath,
            "<Project><Import Project=\"..\\shared\\Flags.props\" /></Project>");
        File.WriteAllText(sharedProps, "<Project><PropertyGroup><Value>one</Value></PropertyGroup></Project>");
        var identity = new TrustedWorkspaceIdentity(solutionPath, "repository", repositoryRoot);
        var fingerprint = (IWorkspaceInputFingerprint)new WorkspaceInputFingerprint(
            new StubEnvironmentIdentity());

        var before = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);
        File.WriteAllText(sharedProps, "<Project><PropertyGroup><Value>two</Value></PropertyGroup></Project>");
        var after = await fingerprint.CaptureAsync(identity, solution: null, CancellationToken.None);

        after.Value.ShouldNotBe(before.Value);
        after.Reusable.ShouldBeTrue();
    }

    [Fact]
    public async Task source_change_between_post_load_and_full_fingerprint_retries_before_publish()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var fingerprint = new StubFingerprint();
        foreach (var value in new[] { "before", "before", "after", "after", "after", "after", "after" })
            fingerprint.CaptureResults.Enqueue(value);
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry());

        using (await cache.AcquireAsync(solutionPath))
        {
        }

        loader.LoadCount.ShouldBe(2);
    }

    [Fact]
    public async Task external_loaded_document_change_during_load_retries_before_publish()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var externalRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "external")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var projectPath = System.IO.Path.Combine(externalRoot, "App.csproj");
        var sourcePath = System.IO.Path.Combine(externalRoot, "External.cs");
        File.WriteAllText(
            solutionPath,
            "<Solution><Project Path=\"../external/App.csproj\" /></Solution>");
        File.WriteAllText(projectPath, "<Project />");
        File.WriteAllText(sourcePath, "class External { public int Old; }");
        using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
        var loader = new StaleExternalDocumentLoader(sourcePath, projectPath);
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new WorkspaceInputFingerprint(new StubEnvironmentIdentity()),
            new RecordingTelemetry());

        using var lease = await cache.AcquireAsync(solutionPath);
        var loadedText = await lease.Solution.Projects.Single().Documents.Single().GetTextAsync();

        loader.LoadCount.ShouldBe(2);
        loadedText.ToString().ShouldBe("class External { public int Current; }");
    }

    [Fact]
    public async Task external_project_with_unproven_default_compile_glob_reloads_for_new_source()
    {
        using var outer = new TemporaryDirectory();
        var repositoryRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "repository")).FullName;
        var externalRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(outer.Path, "external")).FullName;
        var solutionPath = System.IO.Path.Combine(repositoryRoot, "App.slnx");
        var projectPath = System.IO.Path.Combine(externalRoot, "App.csproj");
        File.WriteAllText(
            solutionPath,
            "<Solution><Project Path=\"../external/App.csproj\" /></Solution>");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(System.IO.Path.Combine(externalRoot, "Existing.cs"), "class Existing;");
        using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
        var loader = new ExternalSdkProjectLoader(externalRoot, projectPath);
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new WorkspaceInputFingerprint(new StubEnvironmentIdentity()),
            new RecordingTelemetry());

        using (await cache.AcquireAsync(solutionPath))
        {
        }
        File.WriteAllText(System.IO.Path.Combine(externalRoot, "NewType.cs"), "class NewType;");
        using var refreshed = await cache.AcquireAsync(solutionPath);

        loader.LoadCount.ShouldBe(2);
        refreshed.Solution.Projects.Single().Documents
            .Select(document => document.Name)
            .ShouldContain("NewType.cs");
    }

    [Fact]
    public async Task source_change_between_pre_load_and_post_load_fingerprint_retries_before_publish()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var fingerprint = new StubFingerprint();
        foreach (var value in new[] { "before", "after", "after", "after", "after" })
            fingerprint.CaptureResults.Enqueue(value);
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry());

        using (await cache.AcquireAsync(solutionPath))
        {
        }

        loader.LoadCount.ShouldBe(2);
    }

    [Fact]
    public async Task any_unprovable_cold_fingerprint_capture_makes_the_entry_non_reusable()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var fingerprint = new StubFingerprint();
        fingerprint.ReusableResults.Enqueue(false);
        fingerprint.ReusableResults.Enqueue(true);
        fingerprint.ReusableResults.Enqueue(true);
        fingerprint.ReusableResults.Enqueue(true);
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry());

        using (await cache.AcquireAsync(solutionPath))
        {
        }
        using (await cache.AcquireAsync(solutionPath))
        {
        }

        loader.LoadCount.ShouldBe(2);
    }

    [Fact]
    public async Task failed_post_load_freshness_capture_disposes_the_unpublished_owner()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new TrackingOwnerLoader();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new PostLoadThrowingFingerprint(),
            new RecordingTelemetry());

        await Should.ThrowAsync<IOException>(() => cache.AcquireAsync(solutionPath));

        loader.Owner.ShouldNotBeNull();
        loader.Owner.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void cache_key_requires_solution_repository_worktree_and_environment_identity()
    {
        var key = new ReadOnlyWorkspaceSessionCache.WorkspaceCacheKey(
            "/repo/App.slnx",
            "repository",
            "/repo",
            "environment");
        var identity = new TrustedWorkspaceIdentity(
            "/repo/App.slnx",
            "repository",
            "/repo");

        key.Matches(identity, "environment").ShouldBeTrue();
        key.Matches(identity with { SolutionPath = "/repo/Other.slnx" }, "environment")
            .ShouldBeFalse();
        key.Matches(identity with { RepositoryId = "other-repository" }, "environment")
            .ShouldBeFalse();
        key.Matches(identity with { WorktreeRoot = "/other-worktree" }, "environment")
            .ShouldBeFalse();
        key.Matches(identity, "other-environment").ShouldBeFalse();
    }

    [Fact]
    public void missing_workspace_environment_identity_is_explicit_and_distinct()
    {
        IWorkspaceEnvironmentIdentity missing = new WorkspaceEnvironmentIdentity(() => null);
        IWorkspaceEnvironmentIdentity registered = new WorkspaceEnvironmentIdentity(() => "sdk");

        missing.Capture().ShouldBe("unregistered");
        registered.Capture().ShouldBe("sdk");
    }

    [Fact]
    public async Task eviction_retires_but_does_not_dispose_an_entry_with_an_active_lease()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new StubFingerprint(),
            telemetry,
            capacity: 1);

        var firstLease = await cache.AcquireAsync(firstPath);
        var secondLease = await cache.AcquireAsync(secondPath);

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Evict).ShouldBe(1);
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(0);

        firstLease.Dispose();

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        secondLease.Dispose();
    }

    [Fact]
    public async Task reaching_capacity_does_not_evict_a_reusable_entry()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new StubFingerprint(),
            telemetry,
            capacity: 2);

        using (await cache.AcquireAsync(firstPath))
        {
        }

        using (await cache.AcquireAsync(secondPath))
        {
        }

        using (await cache.AcquireAsync(firstPath))
        {
        }

        loader.LoadCount.ShouldBe(2);
        telemetry.Events.ShouldNotContain(
            cacheEvent => cacheEvent.Kind == WorkspaceCacheEventKind.Evict);
    }

    [Fact]
    public async Task successful_eviction_releases_the_candidate_for_later_retirement()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        var thirdPath = root.CreateSolution("Third.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            new StubLoader(),
            new StubFingerprint(),
            telemetry,
            capacity: 1);

        using (await cache.AcquireAsync(firstPath))
        {
        }

        using (await cache.AcquireAsync(secondPath))
        {
        }

        using (await cache.AcquireAsync(thirdPath))
        {
        }

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Evict).ShouldBe(2);
    }

    [Fact]
    public async Task successful_eviction_releases_waiters_already_queued_on_the_retired_slot()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        using var candidateHeld = new ManualResetEventSlim();
        using var continueRetirement = new ManualResetEventSlim();
        using var queuedWaitStarted = new ManualResetEventSlim();
        var blockRetirement = 0;
        var hooks = new ReadOnlyWorkspaceSessionCache.ReadOnlyWorkspaceCacheTestHooks
        {
            BeforeSlotWait = () =>
            {
                if (candidateHeld.IsSet)
                    queuedWaitStarted.Set();
            },
            AfterRetirementCandidateWait = () =>
            {
                if (Volatile.Read(ref blockRetirement) == 0)
                    return;

                candidateHeld.Set();
                continueRetirement.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            }
        };
        using var cache = new ReadOnlyWorkspaceSessionCache(
            new StubLoader(),
            new StubFingerprint(),
            new RecordingTelemetry(),
            capacity: 1,
            testHooks: hooks);
        using (await cache.AcquireAsync(firstPath))
        {
        }

        Volatile.Write(ref blockRetirement, 1);
        var evictionTask = Task.Run(() => cache.AcquireAsync(secondPath));
        candidateHeld.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        using var cancellation = new CancellationTokenSource();
        var queuedTask = Task.Run(() => cache.AcquireAsync(firstPath, cancellation.Token));
        queuedWaitStarted.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        continueRetirement.Set();
        using var evictionLease = await evictionTask.WaitAsync(TimeSpan.FromSeconds(5));

        ReadOnlyWorkspaceLease queuedLease;
        try
        {
            queuedLease = await queuedTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch
        {
            cancellation.Cancel();
            throw;
        }

        queuedLease.Dispose();
    }

    [Fact]
    public async Task completed_acquisition_does_not_wait_for_another_identity_cold_load()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new CoordinatedLoader(firstPath);
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new StubFingerprint(),
            telemetry,
            capacity: 1);

        var firstTask = cache.AcquireAsync(firstPath);
        await loader.BlockedLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = cache.AcquireAsync(secondPath);
        await loader.UnblockedLoadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ReadOnlyWorkspaceLease? secondLease = null;
        Exception? completionFailure = null;
        try
        {
            secondLease = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }
        loader.ReleaseBlockedLoad.SetResult();
        var firstLease = await firstTask;
        secondLease ??= await secondTask;

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(0);
        secondLease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        firstLease.Dispose();
        completionFailure.ShouldBeNull();
    }

    [Fact]
    public async Task cancellation_during_trim_releases_the_unreturned_lease()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        using var trimAdmitted = new ManualResetEventSlim();
        using var continueTrim = new ManualResetEventSlim();
        var blockTrim = 0;
        var telemetry = new RecordingTelemetry();
        var hooks = new ReadOnlyWorkspaceSessionCache.ReadOnlyWorkspaceCacheTestHooks
        {
            BeforeRetirementWait = () =>
            {
                if (Volatile.Read(ref blockTrim) == 0)
                    return;

                trimAdmitted.Set();
                continueTrim.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            }
        };
        var cache = new ReadOnlyWorkspaceSessionCache(
            new StubLoader(),
            new StubFingerprint(),
            telemetry,
            capacity: 1,
            testHooks: hooks);
        using (await cache.AcquireAsync(firstPath))
        {
        }
        using var cancellation = new CancellationTokenSource();
        Volatile.Write(ref blockTrim, 1);

        var secondTask = Task.Run(() => cache.AcquireAsync(secondPath, cancellation.Token));
        trimAdmitted.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        cancellation.Cancel();
        continueTrim.Set();

        ReadOnlyWorkspaceLease? returnedLease = null;
        Exception? observed = null;
        try
        {
            returnedLease = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            observed = exception;
        }

        returnedLease?.Dispose();
        ((IDisposable)cache).Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(2);
        observed.ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task busy_warm_entry_is_skipped_and_idle_overflow_entry_is_retired_later()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new CoordinatedLoader();
        var fingerprint = new BlockingWarmFingerprint();
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            telemetry,
            capacity: 1);
        using (await cache.AcquireAsync(firstPath))
        {
        }
        fingerprint.BlockNextWarmCapture = true;

        var warmTask = cache.AcquireAsync(firstPath);
        await fingerprint.WarmCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = cache.AcquireAsync(secondPath);
        await loader.SecondLoadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fingerprint.ReleaseWarmCapture.SetResult();
        var warmLease = await warmTask;
        var secondLease = await secondTask;

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(0);
        warmLease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(0);
        secondLease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
    }

    [Fact]
    public async Task busy_oldest_entry_does_not_prevent_eviction_of_an_idle_candidate()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        var thirdPath = root.CreateSolution("Third.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var fingerprint = new BlockingWarmFingerprint();
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            new StubLoader(),
            fingerprint,
            telemetry,
            capacity: 1);
        using (await cache.AcquireAsync(firstPath))
        {
        }
        fingerprint.BlockNextWarmCapture = true;
        var busyTask = cache.AcquireAsync(firstPath);
        await fingerprint.WarmCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondLease = await cache.AcquireAsync(secondPath);

        var thirdLease = await cache.AcquireAsync(thirdPath);

        var evictionCountBeforeRelease = telemetry.Events.Count(
            item => item.Kind == WorkspaceCacheEventKind.Evict);
        fingerprint.ReleaseWarmCapture.SetResult();
        var busyLease = await busyTask.WaitAsync(TimeSpan.FromSeconds(5));
        busyLease.Dispose();
        secondLease.Dispose();
        thirdLease.Dispose();
        evictionCountBeforeRelease.ShouldBe(1);
    }

    [Fact]
    public async Task failed_cold_publication_does_not_consume_retained_capacity()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var failingPath = root.CreateSolution("Failing.slnx");
        var thirdPath = root.CreateSolution("Third.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            new PathFailingLoader(failingPath),
            new StubFingerprint(),
            telemetry,
            capacity: 1);
        using (await cache.AcquireAsync(firstPath))
        {
        }

        await Should.ThrowAsync<IOException>(() => cache.AcquireAsync(failingPath));
        using (await cache.AcquireAsync(thirdPath))
        {
        }

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Evict).ShouldBe(1);
        telemetry.Events.ShouldNotContain(item => item.Kind == WorkspaceCacheEventKind.Evict
                                                  && item.SourceRevision == null);
    }

    [Fact]
    public async Task acquisition_queued_behind_a_busy_entry_reuses_its_published_snapshot()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new CoordinatedLoader();
        var fingerprint = new BlockingWarmFingerprint();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry(),
            capacity: 1);
        using (await cache.AcquireAsync(firstPath))
        {
        }
        fingerprint.BlockNextWarmCapture = true;

        var warmTask = cache.AcquireAsync(firstPath);
        await fingerprint.WarmCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var evictionTask = cache.AcquireAsync(secondPath);
        await loader.SecondLoadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedTask = cache.AcquireAsync(firstPath);
        fingerprint.ReleaseWarmCapture.SetResult();
        var warmLease = await warmTask;
        var evictionLease = await evictionTask;
        var queuedLease = await queuedTask;

        loader.LoadCount.ShouldBe(2);
        warmLease.Dispose();
        evictionLease.Dispose();
        queuedLease.Dispose();
    }

    [Fact]
    public async Task busy_refresh_is_skipped_and_its_replacement_remains_current()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new RefreshBlockingLoader(firstPath);
        var fingerprint = new OneStaleWarmFingerprint();
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            telemetry,
            capacity: 1);
        using (await cache.AcquireAsync(firstPath))
        {
        }
        fingerprint.StaleNextWarmCapture = true;

        var refreshTask = cache.AcquireAsync(firstPath);
        await loader.RefreshLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = cache.AcquireAsync(secondPath);
        await loader.OtherLoadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        loader.ReleaseRefreshLoad.SetResult();
        var refreshLease = await refreshTask;
        var secondLease = await secondTask;

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        refreshLease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        secondLease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(2);
    }

    [Fact]
    public async Task shutdown_waits_for_a_cold_load_and_retires_the_published_entry()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new CoordinatedLoader(solutionPath);
        var telemetry = new RecordingTelemetry();
        var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            new StubFingerprint(),
            telemetry);

        var acquireTask = cache.AcquireAsync(solutionPath);
        await loader.BlockedLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposeTask = Task.Run(() => ((IDisposable)cache).Dispose());
        loader.ReleaseBlockedLoad.SetResult();
        var lease = await acquireTask;
        await disposeTask;

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(0);
        lease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
    }

    [Fact]
    public async Task queued_acquisition_during_shutdown_cannot_orphan_the_published_entry()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new TrackingOwnerLoader();
        var fingerprint = new BlockingWarmFingerprint();
        var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry());
        using (await cache.AcquireAsync(solutionPath))
        {
        }
        fingerprint.BlockNextWarmCapture = true;

        var warmTask = cache.AcquireAsync(solutionPath);
        await fingerprint.WarmCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedTask = cache.AcquireAsync(solutionPath);
        var disposeTask = Task.Run(() => ((IDisposable)cache).Dispose());
        SpinWait.SpinUntil(() => cache.IsDisposed, TimeSpan.FromSeconds(5)).ShouldBeTrue();
        fingerprint.ReleaseWarmCapture.SetResult();
        var warmLease = await warmTask;
        await Should.ThrowAsync<ObjectDisposedException>(async () => await queuedTask);
        await disposeTask;

        loader.Owner.ShouldNotBeNull().DisposeCount.ShouldBe(0);
        warmLease.Dispose();
        loader.Owner.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task shutdown_releases_a_removed_slot_for_an_already_admitted_acquisition()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var fingerprint = new BlockingWarmFingerprint();
        using var admitted = new ManualResetEventSlim();
        using var continueAdmission = new ManualResetEventSlim();
        var blockAdmission = 0;
        var hooks = new ReadOnlyWorkspaceSessionCache.ReadOnlyWorkspaceCacheTestHooks
        {
            BeforeSlotWait = () =>
            {
                if (Volatile.Read(ref blockAdmission) == 0)
                    return;

                admitted.Set();
                continueAdmission.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            }
        };
        var cache = new ReadOnlyWorkspaceSessionCache(
            new StubLoader(),
            fingerprint,
            new RecordingTelemetry(),
            testHooks: hooks);
        using (await cache.AcquireAsync(solutionPath))
        {
        }
        fingerprint.BlockNextWarmCapture = true;

        var holdingTask = cache.AcquireAsync(solutionPath);
        await fingerprint.WarmCaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Write(ref blockAdmission, 1);
        var admittedTask = Task.Run(() => cache.AcquireAsync(solutionPath));
        admitted.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        var disposeTask = Task.Run(() => ((IDisposable)cache).Dispose());
        fingerprint.ReleaseWarmCapture.SetResult();
        var holdingLease = await holdingTask;
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        continueAdmission.Set();
        await Should.ThrowAsync<ObjectDisposedException>(async () =>
            await admittedTask.WaitAsync(TimeSpan.FromSeconds(5)));
        holdingLease.Dispose();
    }

    [Fact]
    public async Task shutdown_releases_the_retirement_gate_for_an_already_admitted_trim()
    {
        using var root = new TemporaryDirectory();
        var firstPath = root.CreateSolution("First.slnx");
        var secondPath = root.CreateSolution("Second.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        using var trimAdmitted = new ManualResetEventSlim();
        using var continueTrim = new ManualResetEventSlim();
        var blockTrim = 0;
        var hooks = new ReadOnlyWorkspaceSessionCache.ReadOnlyWorkspaceCacheTestHooks
        {
            BeforeRetirementWait = () =>
            {
                if (Volatile.Read(ref blockTrim) == 0)
                    return;

                trimAdmitted.Set();
                continueTrim.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            }
        };
        var cache = new ReadOnlyWorkspaceSessionCache(
            new StubLoader(),
            new StubFingerprint(),
            new RecordingTelemetry(),
            capacity: 1,
            testHooks: hooks);
        using (await cache.AcquireAsync(firstPath))
        {
        }

        Volatile.Write(ref blockTrim, 1);
        var secondTask = Task.Run(() => cache.AcquireAsync(secondPath));
        trimAdmitted.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
        var disposeTask = Task.Run(() => ((IDisposable)cache).Dispose());
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        continueTrim.Set();
        var secondLease = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        secondLease.Dispose();
    }

    [Fact]
    public async Task unprovable_warm_freshness_reloads_instead_of_serving_the_cached_snapshot()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var fingerprint = new StubFingerprint();
        var telemetry = new RecordingTelemetry();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            telemetry);

        using (await cache.AcquireAsync(solutionPath))
        {
        }

        fingerprint.FailNextCapture = true;
        using (await cache.AcquireAsync(solutionPath))
        {
        }

        loader.LoadCount.ShouldBe(2);
        telemetry.Events.Select(item => item.Kind).ToArray().ShouldBe(
            [
                WorkspaceCacheEventKind.Load,
                WorkspaceCacheEventKind.Dispose,
                WorkspaceCacheEventKind.Refresh
            ]);
    }

    [Fact]
    public async Task inputs_that_keep_changing_during_load_are_retried_once_then_rejected()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var fingerprint = new StubFingerprint();
        fingerprint.CaptureResults.Enqueue("before");
        fingerprint.CaptureResults.Enqueue("after-first-load");
        fingerprint.CaptureResults.Enqueue("after-second-load");
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry());

        var exception = await Should.ThrowAsync<IOException>(
            () => cache.AcquireAsync(solutionPath));

        exception.Message.ShouldContain("changed repeatedly during load");
        loader.LoadCount.ShouldBe(2);
    }

    [Fact]
    public async Task cache_disposal_waits_for_the_last_lease_and_is_idempotent()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var telemetry = new RecordingTelemetry();
        var cache = new ReadOnlyWorkspaceSessionCache(
            new StubLoader(),
            new StubFingerprint(),
            telemetry);
        var lease = await cache.AcquireAsync(solutionPath);

        ((IDisposable)cache).Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(0);

        lease.Dispose();
        lease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        ((IDisposable)cache).Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        await Should.ThrowAsync<ObjectDisposedException>(() => cache.AcquireAsync(solutionPath));
    }

    [Fact]
    public async Task environment_change_during_admission_is_rejected_before_loading()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var fingerprint = new StubFingerprint();
        fingerprint.EnvironmentIdentities.Enqueue("initial");
        fingerprint.EnvironmentIdentities.Enqueue("changed");
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry());

        var exception = await Should.ThrowAsync<IOException>(
            () => cache.AcquireAsync(solutionPath));

        exception.Message.ShouldContain("semantic environment changed");
        loader.LoadCount.ShouldBe(0);
    }

    [Fact]
    public async Task semantic_environment_change_uses_a_new_cache_identity()
    {
        using var root = new TemporaryDirectory();
        var solutionPath = root.CreateSolution("App.slnx");
        using var trust = RepositoryTrustContext.UseExplicitTrust(root.Path);
        var loader = new StubLoader();
        var fingerprint = new StubFingerprint();
        using var cache = new ReadOnlyWorkspaceSessionCache(
            loader,
            fingerprint,
            new RecordingTelemetry());

        using (await cache.AcquireAsync(solutionPath))
        {
        }

        fingerprint.EnvironmentIdentity = "changed-environment";
        using (await cache.AcquireAsync(solutionPath))
        {
        }

        loader.LoadCount.ShouldBe(2);
    }

    private sealed class StubLoader : IWorkspaceSnapshotLoader
    {
        private int _loadCount;

        internal int LoadCount => Volatile.Read(ref _loadCount);

        public Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            return Task.FromResult(new OwnedWorkspaceSnapshot(workspace, workspace.CurrentSolution));
        }
    }

    private sealed class PathFailingLoader(string failingPath) : IWorkspaceSnapshotLoader
    {
        public Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(solutionPath, failingPath, StringComparison.Ordinal))
                throw new IOException("load failed");

            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            return Task.FromResult(new OwnedWorkspaceSnapshot(workspace, workspace.CurrentSolution));
        }
    }

    private sealed class ExternalImportLoader(string importedPath) : IWorkspaceSnapshotLoader
    {
        private int _loadCount;

        internal int LoadCount => Volatile.Read(ref _loadCount);

        public Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            var projectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    "App",
                    "App",
                    LanguageNames.CSharp))
                .AddDocument(
                    DocumentId.CreateNewId(projectId),
                    "ImportedValue.cs",
                    SourceText.From(File.ReadAllText(importedPath)));
            return Task.FromResult(new OwnedWorkspaceSnapshot(workspace, solution));
        }
    }

    private sealed class StaleExternalDocumentLoader(
        string sourcePath,
        string projectPath) : IWorkspaceSnapshotLoader
    {
        private int _loadCount;

        internal int LoadCount => Volatile.Read(ref _loadCount);

        public Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loadedText = File.ReadAllText(sourcePath);
            if (Interlocked.Increment(ref _loadCount) == 1)
                File.WriteAllText(sourcePath, "class External { public int Current; }");

            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            var projectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    "App",
                    "App",
                    LanguageNames.CSharp,
                    filePath: projectPath))
                .AddDocument(
                    DocumentId.CreateNewId(projectId),
                    "External.cs",
                    SourceText.From(loadedText),
                    filePath: sourcePath);
            return Task.FromResult(new OwnedWorkspaceSnapshot(workspace, solution));
        }
    }

    private sealed class ExternalSdkProjectLoader(
        string projectRoot,
        string projectPath) : IWorkspaceSnapshotLoader
    {
        private int _loadCount;

        internal int LoadCount => Volatile.Read(ref _loadCount);

        public Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            var projectId = ProjectId.CreateNewId();
            var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "App",
                "App",
                LanguageNames.CSharp,
                filePath: projectPath));
            foreach (var sourcePath in Directory.EnumerateFiles(projectRoot, "*.cs"))
            {
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(projectId),
                    System.IO.Path.GetFileName(sourcePath),
                    SourceText.From(File.ReadAllText(sourcePath)),
                    filePath: sourcePath);
            }

            return Task.FromResult(new OwnedWorkspaceSnapshot(workspace, solution));
        }
    }

    private sealed class CoordinatedLoader(string? blockedPath = null) : IWorkspaceSnapshotLoader
    {
        private int _loadCount;

        internal int LoadCount => Volatile.Read(ref _loadCount);

        internal TaskCompletionSource BlockedLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseBlockedLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource UnblockedLoadCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource SecondLoadCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loadCount = Interlocked.Increment(ref _loadCount);
            if (string.Equals(solutionPath, blockedPath, StringComparison.Ordinal))
            {
                BlockedLoadStarted.TrySetResult();
                await ReleaseBlockedLoad.Task.WaitAsync(cancellationToken);
            }
            else
            {
                UnblockedLoadCompleted.TrySetResult();
            }

            if (loadCount >= 2)
                SecondLoadCompleted.TrySetResult();
            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            return new OwnedWorkspaceSnapshot(workspace, workspace.CurrentSolution);
        }
    }

    private sealed class StubFingerprint : IWorkspaceInputFingerprint
    {
        internal bool FailNextCapture { get; set; }
        internal string EnvironmentIdentity { get; set; } = "environment";
        internal Queue<string> CaptureResults { get; } = new();
        internal Queue<bool> ReusableResults { get; } = new();
        internal Queue<string> EnvironmentIdentities { get; } = new();

        public string CaptureEnvironmentIdentity() => EnvironmentIdentities.TryDequeue(out var identity)
            ? identity
            : EnvironmentIdentity;

        public Task<WorkspaceInputFingerprintResult> CaptureAsync(
            TrustedWorkspaceIdentity identity,
            Solution? solution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextCapture)
            {
                FailNextCapture = false;
                throw new IOException("The fingerprint could not be proven.");
            }

            var value = CaptureResults.TryDequeue(out var result)
                ? result
                : identity.SolutionPath;
            var reusable = !ReusableResults.TryDequeue(out var nextReusable) || nextReusable;
            return Task.FromResult(new WorkspaceInputFingerprintResult(value, reusable));
        }
    }

    private sealed class TrackingOwnerLoader : IWorkspaceSnapshotLoader
    {
        internal TrackingOwner? Owner { get; private set; }

        public Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: solutionPath));
            Owner = new TrackingOwner(workspace);
            return Task.FromResult(new OwnedWorkspaceSnapshot(Owner, workspace.CurrentSolution));
        }
    }

    private sealed class TrackingOwner(Workspace workspace) : IDisposable
    {
        private int _disposeCount;

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            workspace.Dispose();
            Interlocked.Increment(ref _disposeCount);
        }
    }

    private sealed class PostLoadThrowingFingerprint : IWorkspaceInputFingerprint
    {
        private int _captureCount;

        public string CaptureEnvironmentIdentity() => "environment";

        public Task<WorkspaceInputFingerprintResult> CaptureAsync(
            TrustedWorkspaceIdentity identity,
            Solution? solution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _captureCount) == 2)
                throw new IOException("post-load fingerprint failed");
            return Task.FromResult(new WorkspaceInputFingerprintResult(identity.SolutionPath));
        }
    }

    private sealed class RefreshBlockingLoader(string refreshPath) : IWorkspaceSnapshotLoader
    {
        private readonly ConcurrentDictionary<string, int> _loadsByPath = new();

        internal TaskCompletionSource RefreshLoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseRefreshLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource OtherLoadCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
            string? solutionPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = solutionPath!;
            var pathLoadCount = _loadsByPath.AddOrUpdate(path, 1, static (_, count) => count + 1);
            if (string.Equals(path, refreshPath, StringComparison.Ordinal) && pathLoadCount == 2)
            {
                RefreshLoadStarted.TrySetResult();
                await ReleaseRefreshLoad.Task.WaitAsync(cancellationToken);
            }
            else if (!string.Equals(path, refreshPath, StringComparison.Ordinal))
            {
                OtherLoadCompleted.TrySetResult();
            }

            var workspace = new AdhocWorkspace();
            workspace.AddSolution(SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Create(),
                filePath: path));
            return new OwnedWorkspaceSnapshot(workspace, workspace.CurrentSolution);
        }
    }

    private sealed class OneStaleWarmFingerprint : IWorkspaceInputFingerprint
    {
        private int _staleNextWarmCapture;

        internal bool StaleNextWarmCapture
        {
            set => Volatile.Write(ref _staleNextWarmCapture, value ? 1 : 0);
        }

        public string CaptureEnvironmentIdentity() => "environment";

        public Task<WorkspaceInputFingerprintResult> CaptureAsync(
            TrustedWorkspaceIdentity identity,
            Solution? solution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = solution is not null
                        && Interlocked.Exchange(ref _staleNextWarmCapture, 0) != 0
                ? "stale"
                : identity.SolutionPath;
            return Task.FromResult(new WorkspaceInputFingerprintResult(value));
        }
    }

    private sealed class BlockingWarmFingerprint : IWorkspaceInputFingerprint
    {
        private int _blockNextWarmCapture;

        internal bool BlockNextWarmCapture
        {
            set => Volatile.Write(ref _blockNextWarmCapture, value ? 1 : 0);
        }

        internal TaskCompletionSource WarmCaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseWarmCapture { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string CaptureEnvironmentIdentity() => "environment";

        public async Task<WorkspaceInputFingerprintResult> CaptureAsync(
            TrustedWorkspaceIdentity identity,
            Solution? solution,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (solution is not null
                && Interlocked.Exchange(ref _blockNextWarmCapture, 0) != 0)
            {
                WarmCaptureStarted.TrySetResult();
                await ReleaseWarmCapture.Task.WaitAsync(cancellationToken);
            }

            return new WorkspaceInputFingerprintResult(identity.SolutionPath);
        }
    }

    private sealed class StubEnvironmentIdentity : IWorkspaceEnvironmentIdentity
    {
        public string Capture() => "sdk";
    }

    private sealed class StubLoadConfiguration(string? baseIntermediateOutputPath)
        : IWorkspaceLoadConfiguration
    {
        public string? BaseIntermediateOutputPath => baseIntermediateOutputPath;
    }

    private sealed class MutableEnvironmentIdentity(string value) : IWorkspaceEnvironmentIdentity
    {
        internal string Value { get; set; } = value;

        public string Capture() => Value;
    }

    private sealed class StubAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }

    private sealed class FixedSourceGeneratorReference(string source) : AnalyzerReference
    {
        private readonly ImmutableArray<ISourceGenerator> _generators =
            [new FixedSourceGenerator(source)];

        public override string FullPath => string.Empty;

        public override object Id { get; } = new();

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language) =>
            language == LanguageNames.CSharp ? _generators : [];

        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages() => _generators;
    }

#pragma warning disable RS1042 // Test-only in-memory generator; it is never distributed as an analyzer.
    private sealed class FixedSourceGenerator(string source) : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context) =>
            context.AddSource("Signing.g.cs", source);
    }
#pragma warning restore RS1042

    private sealed class RecordingTelemetry : IWorkspaceCacheTelemetry
    {
        private readonly ConcurrentQueue<WorkspaceCacheEvent> _events = new();

        internal IReadOnlyList<WorkspaceCacheEvent> Events => _events.ToArray();

        public void Record(WorkspaceCacheEvent cacheEvent) => _events.Enqueue(cacheEvent);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"rt-cache-unit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string CreateSolution(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, "<Solution />");
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
