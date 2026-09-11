using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class RepositoryTrustRegistryTests
{
    [Fact]
    public void trust_granted_from_one_worktree_authorizes_current_linked_worktrees()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        var added = registry.Add(repository.PrimaryPath);

        added.IsOk.ShouldBeTrue();
        registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeTrue();
        registry.IsTrustedFor(repository.LinkedSolutionPath).ShouldBeTrue();
        added.Match(
            status => status.RepositoryId,
            _ => string.Empty).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void a_removed_worktree_path_cannot_reuse_persistent_trust()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();

        repository.RemoveAndReplaceLinkedWorktreeWithPlainDirectory();

        registry.IsTrustedFor(repository.LinkedSolutionPath).ShouldBeFalse();
        registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeTrue();
    }

    [Fact]
    public void corrupt_registry_content_fails_closed_without_being_replaced()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        Directory.CreateDirectory(Path.GetDirectoryName(repository.RegistryPath)!);
        File.WriteAllText(repository.RegistryPath, "{ not valid json");
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeFalse();
        registry.Add(repository.PrimaryPath).Match(
                _ => throw new InvalidOperationException("Expected corrupt registry failure."),
                error => error)
            .ShouldSatisfyAllConditions(
                error => error.Kind.ShouldBe(RtErrorKind.InvalidInput),
                error => error.Message.ShouldStartWith(
                    "The repository trust registry could not be read or written and was treated as untrusted:"),
                error => error.Context!["registry"].ShouldBe(repository.RegistryPath));
        File.ReadAllText(repository.RegistryPath).ShouldBe("{ not valid json");
    }

    [Fact]
    public void unreadable_registry_fails_closed_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();
        File.SetUnixFileMode(repository.RegistryPath, UnixFileMode.None);
        try
        {
            registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeFalse();
        }
        finally
        {
            File.SetUnixFileMode(
                repository.RegistryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void registry_storage_uses_user_only_permissions_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();

        File.GetUnixFileMode(repository.RegistryPath).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode($"{repository.RegistryPath}.lock").ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode(Path.GetDirectoryName(repository.RegistryPath)!).ShouldBe(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void list_status_and_remove_use_the_same_common_directory_identity()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        var added = registry.Add(repository.PrimaryPath).Match(
            value => value,
            error => throw new InvalidOperationException(error.Message));

        var linkedStatus = registry.Status(repository.LinkedPath)
            .Match(value => value, error => throw new InvalidOperationException(error.Message));
        var listed = registry.List().Match(
            value => value,
            error => throw new InvalidOperationException(error.Message));
        var removed = registry.Remove(repository.LinkedPath)
            .Match(value => value, error => throw new InvalidOperationException(error.Message));

        linkedStatus.State.ShouldBe(RepositoryTrustState.Trusted);
        linkedStatus.RepositoryId.ShouldBe(added.RepositoryId);
        listed.ShouldHaveSingleItem().RepositoryId.ShouldBe(added.RepositoryId);
        removed.State.ShouldBe(RepositoryTrustState.Untrusted);
        registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeFalse();
    }

    [Fact]
    public void persistent_context_revalidates_each_requested_path()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();

        using (RepositoryTrustContext.UsePersistentTrust(registry))
        {
            RepositoryTrustContext.IsExplicitlyTrustedFor(repository.LinkedSolutionPath).ShouldBeTrue();
            repository.RemoveAndReplaceLinkedWorktreeWithPlainDirectory();
            RepositoryTrustContext.IsExplicitlyTrustedFor(repository.LinkedSolutionPath).ShouldBeFalse();
            RepositoryTrustContext.IsExplicitlyTrustedFor(repository.PrimarySolutionPath).ShouldBeTrue();
        }
    }

    [Fact]
    public void removing_the_granting_worktree_preserves_common_directory_trust()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.LinkedPath).IsOk.ShouldBeTrue();

        repository.RemoveAndReplaceLinkedWorktreeWithPlainDirectory();

        registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeTrue();
        registry.List().Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldHaveSingleItem().State.ShouldBe(RepositoryTrustState.Trusted);
    }

    [Fact]
    public void an_unrelated_repository_recreated_at_the_same_path_cannot_inherit_trust()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();

        repository.ReplaceEntirePrimaryRepository();

        registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeFalse();
        registry.Status(repository.PrimaryPath).Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldSatisfyAllConditions(
                status => status.State.ShouldBe(RepositoryTrustState.Stale),
                status => status.Reason.ShouldBe(
                    "The recorded Git object anchor is absent from the current repository."));
        registry.List().Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                status => status.State.ShouldBe(RepositoryTrustState.Stale),
                status => status.Reason.ShouldNotBeNullOrWhiteSpace());
    }

    [Fact]
    public void ordinary_head_advancement_preserves_the_granted_repository_lineage()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();

        repository.CommitAnotherChange();

        registry.IsTrustedFor(repository.PrimarySolutionPath).ShouldBeTrue();
    }

    [Fact]
    public void malformed_paths_return_typed_errors_instead_of_escaping()
    {
        var registry = new RepositoryTrustRegistry(Path.Combine(
            Path.GetTempPath(),
            $"rt-malformed-trust-{Guid.NewGuid():N}",
            "repository-trust.json"));

        registry.Add("invalid\0path").Match(
                _ => throw new InvalidOperationException("Expected malformed path failure."),
                error => error)
            .ShouldSatisfyAllConditions(
                error => error.Kind.ShouldBe(RtErrorKind.InvalidInput),
                error => error.Message.ShouldContain("invalid or unreadable"));
        registry.Status("invalid\0path").IsErr.ShouldBeTrue();
        registry.Remove("invalid\0path").IsErr.ShouldBeTrue();
        registry.IsTrustedFor("invalid\0path").ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void repository_identity_requires_both_the_key_and_common_directory(
        bool sameRepositoryId,
        bool sameCommonDirectory,
        bool expected)
    {
        var commonDirectory = Path.Combine(Path.GetTempPath(), "rt-identity-common");

        RepositoryTrustRegistry.MatchesRepositoryIdentity(
                "stored-id",
                commonDirectory,
                sameRepositoryId ? "stored-id" : "other-id",
                sameCommonDirectory ? commonDirectory : $"{commonDirectory}-other")
            .ShouldBe(expected);
    }

    [Fact]
    public void repository_identity_uses_case_sensitive_common_directories_on_linux()
    {
        var commonDirectory = Path.Combine(Path.GetTempPath(), "rt-case-sensitive-common");
        var differentlyCased = commonDirectory.ToUpperInvariant();

        RepositoryTrustRegistry.MatchesRepositoryIdentity(
                "same-id",
                commonDirectory,
                "same-id",
                differentlyCased)
            .ShouldBe(OperatingSystem.IsWindows());
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000", true)]
    [InlineData("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", true)]
    [InlineData("000000000000000000000000000000000000000", false)]
    [InlineData("00000000000000000000000000000000000000000", false)]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg", false)]
    [InlineData("000000000000000000000000000000000000000g", false)]
    public void git_object_ids_require_a_supported_length_and_only_hexadecimal_digits(
        string value,
        bool expected)
    {
        RepositoryTrustRegistry.IsObjectId(value).ShouldBe(expected);
    }

    [Fact]
    public void stored_records_require_every_identity_and_binding_field()
    {
        var commonDirectory = Path.Combine(Path.GetTempPath(), "rt-valid-common");
        var worktree = Path.Combine(Path.GetTempPath(), "rt-valid-worktree");
        var repositoryId = RepositoryTrustRegistry.ComputeRepositoryId(commonDirectory);
        const string anchor = "0000000000000000000000000000000000000000";

        RepositoryTrustRegistry.IsValidRecord(repositoryId, commonDirectory, worktree, anchor).ShouldBeTrue();
        RepositoryTrustRegistry.IsValidRecord("", commonDirectory, worktree, anchor).ShouldBeFalse();
        RepositoryTrustRegistry.IsValidRecord("other", commonDirectory, worktree, anchor).ShouldBeFalse();
        RepositoryTrustRegistry.IsValidRecord(repositoryId, "", worktree, anchor).ShouldBeFalse();
        RepositoryTrustRegistry.IsValidRecord(repositoryId, "relative-common", worktree, anchor).ShouldBeFalse();
        RepositoryTrustRegistry.IsValidRecord(repositoryId, commonDirectory, "", anchor).ShouldBeFalse();
        RepositoryTrustRegistry.IsValidRecord(repositoryId, commonDirectory, "relative-worktree", anchor).ShouldBeFalse();
        RepositoryTrustRegistry.IsValidRecord(repositoryId, commonDirectory, worktree, "not-an-object-id").ShouldBeFalse();
    }

    [Fact]
    public void duplicate_repository_ids_are_rejected_independently_of_unique_records()
    {
        RepositoryTrustRegistry.HasDuplicateRepositoryIds([]).ShouldBeFalse();
        RepositoryTrustRegistry.HasDuplicateRepositoryIds(["a", "b"]).ShouldBeFalse();
        RepositoryTrustRegistry.HasDuplicateRepositoryIds(["a", "a"]).ShouldBeTrue();
        RepositoryTrustRegistry.HasDuplicateRepositoryIds(["a", "a", "b"]).ShouldBeTrue();
    }

    [Fact]
    public void any_trusted_status_enables_repository_write_discovery()
    {
        var untrusted = StatusWithState(RepositoryTrustState.Untrusted);
        var stale = StatusWithState(RepositoryTrustState.Stale);
        var trusted = StatusWithState(RepositoryTrustState.Trusted);

        RepositoryTrustRegistry.HasTrustedStatus([]).ShouldBeFalse();
        RepositoryTrustRegistry.HasTrustedStatus([untrusted, stale]).ShouldBeFalse();
        RepositoryTrustRegistry.HasTrustedStatus([untrusted, trusted, stale]).ShouldBeTrue();
    }

    [Fact]
    public void containment_accepts_only_the_root_and_its_descendants()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"rt-containment-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "repo");

        RepositoryTrustRegistry.IsContained(root, root).ShouldBeTrue();
        RepositoryTrustRegistry.IsContained(root, Path.Combine(root, "src", "App.cs")).ShouldBeTrue();
        RepositoryTrustRegistry.IsContained(root, parent).ShouldBeFalse();
        RepositoryTrustRegistry.IsContained(root, Path.Combine(parent, "sibling")).ShouldBeFalse();
        RepositoryTrustRegistry.IsContained(root, $"{root}-prefix").ShouldBeFalse();
    }

    [Fact]
    public void status_and_remove_report_an_unrecorded_repository_as_untrusted()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        registry.Status(repository.PrimaryPath).Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldSatisfyAllConditions(
                status => status.State.ShouldBe(RepositoryTrustState.Untrusted),
                status => status.Reason.ShouldBe("No persistent trust record exists for this repository."));
        registry.Remove(repository.PrimaryPath).Match(
                value => value.State,
                error => throw new InvalidOperationException(error.Message))
            .ShouldBe(RepositoryTrustState.Untrusted);
        File.Exists(repository.RegistryPath).ShouldBeFalse();
    }

    [Fact]
    public void adding_the_same_repository_twice_replaces_the_existing_record()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();
        registry.Add(repository.LinkedPath).IsOk.ShouldBeTrue();

        registry.List().Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .Count.ShouldBe(1);
    }

    [Fact]
    public void registry_file_is_indented_and_stores_records_in_repository_id_order()
    {
        using var first = TestGitRepository.CreateWithLinkedWorktree();
        using var second = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(first.RegistryPath);
        var repositories = new[] { first, second };
        foreach (var repository in repositories.OrderByDescending(item => item.RepositoryId))
            registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();

        var content = File.ReadAllText(first.RegistryPath);
        content.ShouldContain($"{Environment.NewLine}  \"schemaVersion\": 1");
        var storedIds = JsonNode.Parse(content)!["records"]!.AsArray()
            .Select(record => record!["repositoryId"]!.GetValue<string>())
            .ToArray();
        storedIds.ShouldBe(repositories.Select(repository => repository.RepositoryId)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    [Fact]
    public void list_orders_multiple_repositories_by_repository_id()
    {
        using var first = TestGitRepository.CreateWithLinkedWorktree();
        using var second = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(first.RegistryPath);

        var firstAdded = registry.Add(first.PrimaryPath).Match(
            value => value,
            error => throw new InvalidOperationException(error.Message));
        var secondAdded = registry.Add(second.PrimaryPath).Match(
            value => value,
            error => throw new InvalidOperationException(error.Message));

        var expected = new[] { firstAdded.RepositoryId, secondAdded.RepositoryId }
            .Order(StringComparer.Ordinal)
            .ToArray();
        registry.List().Match(
                value => value.Select(status => status.RepositoryId).ToArray(),
                error => throw new InvalidOperationException(error.Message))
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":2,\"records\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"records\":null}")]
    [InlineData("{\"schemaVersion\":1,\"records\":[null]}")]
    public void structurally_invalid_stores_fail_closed(string content)
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        WriteRegistry(repository.RegistryPath, content);
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        registry.List().Match(
                _ => throw new InvalidOperationException("Expected structurally invalid registry failure."),
                error => error)
            .ShouldSatisfyAllConditions(
                error => error.Kind.ShouldBe(RtErrorKind.InvalidInput),
                error => error.Message.ShouldBe(
                    $"The repository trust registry is invalid and was treated as untrusted: '{repository.RegistryPath}'."));
        registry.IsTrustedFor(repository.PrimaryPath).ShouldBeFalse();
    }

    [Fact]
    public void one_invalid_record_makes_an_otherwise_valid_store_fail_closed()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();
        var root = JsonNode.Parse(File.ReadAllText(repository.RegistryPath))!.AsObject();
        var records = root["records"]!.AsArray();
        var invalid = records[0]!.DeepClone().AsObject();
        invalid["anchorCommit"] = "not-an-object-id";
        records.Add(invalid);
        File.WriteAllText(repository.RegistryPath, root.ToJsonString());

        registry.List().IsErr.ShouldBeTrue();
        registry.IsTrustedFor(repository.PrimaryPath).ShouldBeFalse();
    }

    [Fact]
    public void duplicate_records_fail_closed_even_when_another_repository_is_unique()
    {
        using var first = TestGitRepository.CreateWithLinkedWorktree();
        using var second = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(first.RegistryPath);
        registry.Add(first.PrimaryPath).IsOk.ShouldBeTrue();
        registry.Add(second.PrimaryPath).IsOk.ShouldBeTrue();
        var root = JsonNode.Parse(File.ReadAllText(first.RegistryPath))!.AsObject();
        var records = root["records"]!.AsArray();
        records.Add(records[0]!.DeepClone());
        File.WriteAllText(first.RegistryPath, root.ToJsonString());

        registry.List().IsErr.ShouldBeTrue();
        registry.HasAnyTrustedRepository().ShouldBeFalse();
    }

    [Fact]
    public void a_stale_record_can_be_removed_by_its_stored_path()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();
        repository.ReplaceEntirePrimaryRepository();

        registry.Remove(repository.PrimaryPath).Match(
                value => value.State,
                error => throw new InvalidOperationException(error.Message))
            .ShouldBe(RepositoryTrustState.Untrusted);
        registry.List().Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldBeEmpty();
    }

    [Fact]
    public void a_stale_record_can_be_removed_by_its_repository_id()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        var repositoryId = registry.Add(repository.PrimaryPath).Match(
            value => value.RepositoryId,
            error => throw new InvalidOperationException(error.Message));

        registry.Remove(repositoryId).Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldSatisfyAllConditions(
                status => status.State.ShouldBe(RepositoryTrustState.Untrusted),
                status => status.Reason.ShouldBe("Persistent repository trust was removed."));
        registry.List().Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldBeEmpty();
    }

    [Fact]
    public void missing_paths_remain_typed_not_found_errors()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        var missing = Path.Combine(repository.PrimaryPath, "missing");

        var statusError = registry.Status(missing).Match(
                _ => throw new InvalidOperationException("Expected status failure."),
                error => error);
        statusError.Kind.ShouldBe(RtErrorKind.NotFound);
        statusError.Message.ShouldBe($"The repository path does not exist: '{missing}'.");
        var removeError = registry.Remove(missing).Match(
                _ => throw new InvalidOperationException("Expected remove failure."),
                error => error);
        removeError.Kind.ShouldBe(RtErrorKind.NotFound);
        removeError.Message.ShouldBe($"The repository path does not exist: '{missing}'.");
    }

    [Fact]
    public void status_reports_a_deleted_granting_worktree_as_stale()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.LinkedPath).IsOk.ShouldBeTrue();

        repository.RemoveAndReplaceLinkedWorktreeWithPlainDirectory();

        registry.Status(repository.LinkedPath).Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldSatisfyAllConditions(
                status => status.State.ShouldBe(RepositoryTrustState.Stale),
                status => status.Reason.ShouldBe(
                    "The recorded repository path is missing or is no longer a registered Git worktree."));
    }

    [Fact]
    public void multiple_root_commits_use_the_lexicographically_first_lineage_anchor()
    {
        using var repository = TestGitRepository.CreateWithMultipleRoots();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();

        var storedAnchor = JsonNode.Parse(File.ReadAllText(repository.RegistryPath))!["records"]![0]!["anchorCommit"]!
            .GetValue<string>();
        storedAnchor.ShouldBe(repository.RootCommits.Order(StringComparer.Ordinal).First());
    }

    [Fact]
    public void empty_anchor_output_is_a_typed_external_tool_failure()
    {
        var error = RepositoryTrustRegistry.SelectAnchorCommit("").Match(
                _ => throw new InvalidOperationException("Expected anchor selection failure."),
                value => value);
        error.Kind.ShouldBe(RtErrorKind.ExternalToolFailure);
        error.Message.ShouldBe("Git did not return a valid root commit for the repository trust binding.");
        RepositoryTrustRegistry.SelectAnchorCommit("not-an-object-id").IsErr.ShouldBeTrue();
        RepositoryTrustRegistry.SelectAnchorCommit(
                $"{new string('b', 40)}\n{new string('a', 40)}\n").Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldBe(new string('a', 40));
    }

    [Fact]
    public void unborn_repository_preserves_the_actionable_git_failure()
    {
        using var repository = TestGitRepository.CreateUnborn();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        registry.Add(repository.PrimaryPath).Match(
                _ => throw new InvalidOperationException("Expected unborn repository failure."),
                error => error)
            .ShouldSatisfyAllConditions(
                error => error.Kind.ShouldBe(RtErrorKind.ExternalToolFailure),
                error => error.Message.ShouldContain("Git could not resolve repository trust"));
    }

    [Fact]
    public void default_registry_path_uses_the_roslyn_tools_profile_filename()
    {
        RepositoryTrustRegistry.DefaultRegistryPath.ShouldSatisfyAllConditions(
            path => Path.IsPathFullyQualified(path).ShouldBeTrue(),
            path => Path.GetFileName(path).ShouldBe("repository-trust.json"),
            path => Path.GetFileName(Path.GetDirectoryName(path)).ShouldBe("RoslynTools"));
    }

    [Fact]
    public void worktree_porcelain_parser_ignores_non_worktree_fields()
    {
        var first = Path.Combine(Path.GetTempPath(), "rt-porcelain-first");
        var second = Path.Combine(Path.GetTempPath(), "rt-porcelain-second");
        var porcelain = $"worktree {first}\0HEAD abc123\0branch refs/heads/main\0\0" +
                        $"worktree {second}\0HEAD def456\0detached\0";

        RepositoryTrustRegistry.ParseWorktreeRoots(porcelain).ShouldBe([first, second]);
    }

    [Fact]
    public void local_application_data_resolution_prefers_local_then_absolute_xdg_then_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rt-profile-{Guid.NewGuid():N}");
        var local = Path.Combine(root, "local");
        var xdg = Path.Combine(root, "xdg");
        var profile = Path.Combine(root, "profile");

        RepositoryTrustRegistry.ResolveLocalApplicationData(local, xdg, profile).ShouldBe(local);
        RepositoryTrustRegistry.ResolveLocalApplicationData("", xdg, profile).ShouldBe(xdg);
        RepositoryTrustRegistry.ResolveLocalApplicationData("", "relative-xdg", profile)
            .ShouldBe(Path.Combine(profile, ".local", "share"));
        Should.Throw<InvalidOperationException>(() =>
                RepositoryTrustRegistry.ResolveLocalApplicationData("", "", ""))
            .Message.ShouldBe("The current user's local application-data directory is unavailable.");
    }

    [Fact]
    public void exception_classification_keeps_expected_failures_typed_and_fail_closed()
    {
        var io = new IOException();
        var unauthorized = new UnauthorizedAccessException();
        var argument = new ArgumentException();
        var unsupported = new NotSupportedException();
        var json = new JsonException();
        var invalidOperation = new InvalidOperationException();
        var launchFailure = new Win32Exception();
        var unexpected = new ArithmeticException();

        RepositoryTrustRegistry.IsStorageException(io).ShouldBeTrue();
        RepositoryTrustRegistry.IsStorageException(unauthorized).ShouldBeTrue();
        RepositoryTrustRegistry.IsStorageException(argument).ShouldBeFalse();

        foreach (var expected in new Exception[] { argument, io, unauthorized, unsupported })
            RepositoryTrustRegistry.IsBindingException(expected).ShouldBeTrue();
        RepositoryTrustRegistry.IsBindingException(unexpected).ShouldBeFalse();

        foreach (var expected in new Exception[] { json, io, unauthorized, argument, unsupported })
            RepositoryTrustRegistry.IsStoreReadException(expected).ShouldBeTrue();
        RepositoryTrustRegistry.IsStoreReadException(unexpected).ShouldBeFalse();

        foreach (var expected in new Exception[] { invalidOperation, io, unauthorized, launchFailure })
            RepositoryTrustRegistry.IsGitLaunchException(expected).ShouldBeTrue();
        RepositoryTrustRegistry.IsGitLaunchException(unexpected).ShouldBeFalse();
    }

    [Fact]
    public void lock_retry_stops_at_the_timeout_boundary()
    {
        RepositoryTrustRegistry.ShouldRetryLock(TimeSpan.Zero).ShouldBeTrue();
        RepositoryTrustRegistry.ShouldRetryLock(TimeSpan.FromSeconds(4.999)).ShouldBeTrue();
        RepositoryTrustRegistry.ShouldRetryLock(TimeSpan.FromSeconds(5)).ShouldBeFalse();
        RepositoryTrustRegistry.ShouldRetryLock(TimeSpan.FromSeconds(5.001)).ShouldBeFalse();
    }

    [Fact]
    public void repository_id_uses_the_platform_canonical_path_identity()
    {
        var path = Path.Combine(Path.GetTempPath(), "Rt-Mixed-Case-Identity");
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var identity = OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical;
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();

        RepositoryTrustRegistry.ComputeRepositoryId(path).ShouldBe(expected);
    }

    [Fact]
    public void failed_atomic_move_removes_the_temporary_file_and_returns_storage_context()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        Directory.CreateDirectory(repository.RegistryPath);
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);

        var error = registry.Add(repository.PrimaryPath).Match(
            _ => throw new InvalidOperationException("Expected registry write failure."),
            value => value);

        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("could not be read or written");
        error.Context.ShouldNotBeNull();
        error.Context["registry"].ShouldBe(repository.RegistryPath);
        Directory.GetFiles(Path.GetDirectoryName(repository.RegistryPath)!, ".repository-trust.json.*.tmp")
            .ShouldBeEmpty();
    }

    [Fact]
    public void missing_git_common_directory_is_reported_as_stale()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();
        repository.DeleteGitCommonDirectory();

        registry.List().Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                status => status.State.ShouldBe(RepositoryTrustState.Stale),
                status => status.Reason.ShouldBe("The recorded Git common directory no longer exists."));
    }

    [Fact]
    public void unreadable_git_common_directory_is_reported_as_stale()
    {
        using var repository = TestGitRepository.CreateWithLinkedWorktree();
        var registry = new RepositoryTrustRegistry(repository.RegistryPath);
        registry.Add(repository.PrimaryPath).IsOk.ShouldBeTrue();
        repository.InvalidateGitCommonDirectory();

        registry.List().Match(
                value => value,
                error => throw new InvalidOperationException(error.Message))
            .ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                status => status.State.ShouldBe(RepositoryTrustState.Stale),
                status => status.Reason.ShouldBe("The recorded Git common directory is no longer readable."));
    }

    private static RepositoryTrustStatus StatusWithState(RepositoryTrustState state) => new(
        "id",
        Path.GetTempPath(),
        Path.GetTempPath(),
        state,
        null,
        null);

    private static void WriteRegistry(string registryPath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
        File.WriteAllText(registryPath, content);
    }

    private sealed class TestGitRepository : IDisposable
    {
        private readonly string _parentPath;

        private TestGitRepository(string parentPath)
        {
            _parentPath = parentPath;
            PrimaryPath = Path.Combine(parentPath, "primary");
            LinkedPath = Path.Combine(parentPath, "linked");
            RegistryPath = Path.Combine(parentPath, "profile", "repository-trust.json");
        }

        internal string PrimaryPath { get; }
        internal string LinkedPath { get; }
        internal string RegistryPath { get; }
        internal string RepositoryId => RepositoryTrustRegistry.ComputeRepositoryId(
            Path.Combine(PrimaryPath, ".git"));
        internal string PrimarySolutionPath => Path.Combine(PrimaryPath, "App.slnx");
        internal string LinkedSolutionPath => Path.Combine(LinkedPath, "App.slnx");
        internal string[] RootCommits => RunGitWithOutput(
                PrimaryPath,
                "rev-list",
                "--max-parents=0",
                "HEAD")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        internal static TestGitRepository CreateWithLinkedWorktree()
        {
            var repository = new TestGitRepository(
                Path.Combine(Path.GetTempPath(), $"rt-trust-registry-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(repository.PrimaryPath);
            File.WriteAllText(repository.PrimarySolutionPath, "<Solution />");
            RunGit(repository.PrimaryPath, "init", "--initial-branch=main");
            RunGit(repository.PrimaryPath, "config", "user.email", "tests@roslyn-tools.local");
            RunGit(repository.PrimaryPath, "config", "user.name", "Roslyn Tools Tests");
            RunGit(repository.PrimaryPath, "add", "App.slnx");
            RunGit(repository.PrimaryPath, "commit", "-m", "Initial");
            RunGit(repository.PrimaryPath, "worktree", "add", "-b", "feat/linked", repository.LinkedPath);
            return repository;
        }

        internal static TestGitRepository CreateWithMultipleRoots()
        {
            var repository = new TestGitRepository(
                Path.Combine(Path.GetTempPath(), $"rt-trust-roots-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(repository.PrimaryPath);
            File.WriteAllText(repository.PrimarySolutionPath, "<Solution />");
            RunGit(repository.PrimaryPath, "init", "--initial-branch=main");
            RunGit(repository.PrimaryPath, "config", "user.email", "tests@roslyn-tools.local");
            RunGit(repository.PrimaryPath, "config", "user.name", "Roslyn Tools Tests");
            RunGit(repository.PrimaryPath, "add", "App.slnx");
            RunGit(repository.PrimaryPath, "commit", "-m", "First root");
            RunGit(repository.PrimaryPath, "checkout", "--orphan", "other");
            File.WriteAllText(Path.Combine(repository.PrimaryPath, "Other.txt"), "Other root");
            RunGit(repository.PrimaryPath, "add", "Other.txt");
            RunGit(repository.PrimaryPath, "commit", "-m", "Second root");
            RunGit(repository.PrimaryPath, "checkout", "main");
            RunGit(repository.PrimaryPath, "merge", "other", "--allow-unrelated-histories", "-m", "Merge roots");
            return repository;
        }

        internal static TestGitRepository CreateUnborn()
        {
            var repository = new TestGitRepository(
                Path.Combine(Path.GetTempPath(), $"rt-trust-unborn-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(repository.PrimaryPath);
            File.WriteAllText(repository.PrimarySolutionPath, "<Solution />");
            RunGit(repository.PrimaryPath, "init", "--initial-branch=main");
            return repository;
        }

        internal void RemoveAndReplaceLinkedWorktreeWithPlainDirectory()
        {
            RunGit(PrimaryPath, "worktree", "remove", LinkedPath);
            Directory.CreateDirectory(LinkedPath);
            File.WriteAllText(LinkedSolutionPath, "<Solution />");
        }

        internal void ReplaceEntirePrimaryRepository()
        {
            Directory.Delete(PrimaryPath, recursive: true);
            Directory.CreateDirectory(PrimaryPath);
            File.WriteAllText(PrimarySolutionPath, "<Solution Name=\"Replacement\" />");
            RunGit(PrimaryPath, "init", "--initial-branch=main");
            RunGit(PrimaryPath, "config", "user.email", "tests@roslyn-tools.local");
            RunGit(PrimaryPath, "config", "user.name", "Roslyn Tools Tests");
            RunGit(PrimaryPath, "add", "App.slnx");
            RunGit(PrimaryPath, "commit", "-m", "Replacement");
        }

        internal void CommitAnotherChange()
        {
            File.WriteAllText(Path.Combine(PrimaryPath, "README.md"), "Next commit");
            RunGit(PrimaryPath, "add", "README.md");
            RunGit(PrimaryPath, "commit", "-m", "Next");
        }

        internal void DeleteGitCommonDirectory()
        {
            RunGit(PrimaryPath, "worktree", "remove", LinkedPath);
            Directory.Delete(Path.Combine(PrimaryPath, ".git"), recursive: true);
        }

        internal void InvalidateGitCommonDirectory()
        {
            RunGit(PrimaryPath, "worktree", "remove", LinkedPath);
            var commonDirectory = Path.Combine(PrimaryPath, ".git");
            Directory.Delete(commonDirectory, recursive: true);
            Directory.CreateDirectory(commonDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_parentPath))
                Directory.Delete(_parentPath, recursive: true);
        }

        private static void RunGit(string workingDirectory, params string[] arguments)
        {
            RunGitWithOutput(workingDirectory, arguments);
        }

        private static string RunGitWithOutput(string workingDirectory, params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}{stdout}");
            return stdout;
        }
    }
}
