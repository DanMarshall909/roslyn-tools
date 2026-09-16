using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using RoslynTools.Core;

using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class ReadOnlyWorkspaceSessionCacheTests
{
    [Fact]
    public async Task physical_fingerprint_tracks_relevant_untracked_inputs_and_ignores_unrelated_files()
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
        var ignoredDirectory = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "bin"));
        File.WriteAllText(System.IO.Path.Combine(ignoredDirectory.FullName, "Ignored.cs"), "ignored");

        var afterIgnoredChanges = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);
        afterIgnoredChanges.ShouldBe(original);

        var nested = Directory.CreateDirectory(System.IO.Path.Combine(root.Path, "Nested"));
        File.WriteAllText(System.IO.Path.Combine(nested.FullName, "Untracked.cs"), "class Added;");
        var afterRelevantChange = await fingerprint.CaptureAsync(
            identity,
            solution: null,
            CancellationToken.None);

        afterRelevantChange.Value.ShouldNotBe(original.Value);
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
    public async Task eviction_waits_for_a_cold_load_before_retiring_its_published_entry()
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
        loader.ReleaseBlockedLoad.SetResult();
        var firstLease = await firstTask;
        var secondLease = await secondTask;

        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(0);
        firstLease.Dispose();
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        secondLease.Dispose();
    }

    [Fact]
    public async Task eviction_waits_for_a_warm_acquisition_before_retiring_its_entry()
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
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(1);
        secondLease.Dispose();
    }

    [Fact]
    public async Task acquisition_queued_behind_eviction_retries_the_replacement_slot()
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

        loader.LoadCount.ShouldBe(3);
        warmLease.Dispose();
        evictionLease.Dispose();
        queuedLease.Dispose();
    }

    [Fact]
    public async Task eviction_waits_for_a_refresh_before_retiring_its_replacement_entry()
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
        telemetry.Events.Count(item => item.Kind == WorkspaceCacheEventKind.Dispose).ShouldBe(2);
        secondLease.Dispose();
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
