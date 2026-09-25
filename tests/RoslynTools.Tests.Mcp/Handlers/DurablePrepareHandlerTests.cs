using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class DurablePrepareHandlerTests
{
    [Fact]
    public void durable_prepare_is_one_typed_local_state_write_contract_for_mcp()
    {
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(Directory.GetCurrentDirectory()));

        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>().All
            .Single(candidate => candidate.ToolName == DurablePrepareCommandDefinition.Definition.Name);
        var tool = provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == DurablePrepareCommandDefinition.Definition.Name);

        descriptor.Port.ShouldBeOfType<DurablePrepareOperation>();
        descriptor.Definition.ShouldBeSameAs(DurablePrepareCommandDefinition.Definition);
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.LocalWrite);
        descriptor.Effects.ShouldBe(CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite);
        tool.annotations.readOnlyHint.ShouldBeFalse();
        tool.annotations.destructiveHint.ShouldBeTrue();
        tool.annotations.idempotentHint.ShouldBeFalse();
        tool.metadata["rt/workspaceRequirement"].ShouldBe("None");
        var properties = tool.inputSchema["properties"]
            .ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();
        properties.Keys.ShouldBe([
            "repositoryId",
            "planId",
            "planHash",
            "expectedRevision",
            "operationEpoch",
            "operationNonce",
            "maximumOperationCount",
            "maximumRequestBytes",
            "operationEpochNotAfterUtc",
            "dispatchLeaseId",
            "dispatchLeaseExpiresAtUtc",
            "intakeAttemptId",
            "intakeAttemptExpiresAtUtc"
        ], ignoreOrder: true);
        var required = tool.inputSchema["required"].ShouldBeOfType<string[]>();
        required.ShouldBe(properties.Keys, ignoreOrder: true);
        Assert.All(properties.Values, schema => schema.ShouldContainKey("description"));
    }

    [Fact]
    public async Task mcp_preserves_the_shared_typed_prepare_error()
    {
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(Directory.GetCurrentDirectory()));
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            repositoryId = "repository-001",
            planId = "11111111-2222-3333-4444-555555555555",
            planHash = "not-a-sha256",
            expectedRevision = new string('a', 40),
            operationEpoch = 42,
            operationNonce = "99999999-8888-7777-6666-555555555555",
            maximumOperationCount = 8,
            maximumRequestBytes = 1024 * 1024,
            operationEpochNotAfterUtc = "2030-01-02T04:04:05Z",
            dispatchLeaseId = "dispatch-1",
            dispatchLeaseExpiresAtUtc = "2030-01-02T03:14:05Z",
            intakeAttemptId = "attempt-1",
            intakeAttemptExpiresAtUtc = "2030-01-02T03:09:05Z"
        }));

        var result = await provider.GetRequiredService<ToolRegistry>().InvokeAsync(
            DurablePrepareCommandDefinition.Definition.Name,
            arguments.RootElement);

        result.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(result.content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
        error.RootElement.GetProperty("message").GetString()!.ShouldContain("SHA-256");
    }

    [Fact]
    public async Task mcp_binding_failure_is_a_typed_invalid_input_error()
    {
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(Directory.GetCurrentDirectory()));
        using var arguments = JsonDocument.Parse("""
                                                 {
                                                   "repositoryId": "repository-001",
                                                   "planId": "not-a-guid",
                                                   "planHash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                                                   "expectedRevision": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                                                   "operationEpoch": 42,
                                                   "operationNonce": "99999999-8888-7777-6666-555555555555",
                                                   "maximumOperationCount": 8,
                                                   "maximumRequestBytes": 1048576,
                                                   "operationEpochNotAfterUtc": "2030-01-02T04:04:05Z",
                                                   "dispatchLeaseId": "dispatch-1",
                                                   "dispatchLeaseExpiresAtUtc": "2030-01-02T03:14:05Z",
                                                   "intakeAttemptId": "attempt-1",
                                                   "intakeAttemptExpiresAtUtc": "2030-01-02T03:09:05Z"
                                                 }
                                                 """);

        var result = await provider.GetRequiredService<ToolRegistry>().InvokeAsync(
            DurablePrepareCommandDefinition.Definition.Name,
            arguments.RootElement);

        result.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(result.content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
    }

    [Fact]
    public async Task mcp_reaches_the_same_typed_prepared_result_without_repository_effects()
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime();
        using var fixture = new DurablePrepareOperationTests.HostFixture(now);
        var before = fixture.SnapshotRepository();
        using var provider = McpTestHost.CreateProvider(
            new McpHostOptions(fixture.RepositoryRoot),
            fixture.RegisterProductionComposition);
        fixture.SeedPlan(provider);
        var epoch = LocalDurableOperationAuthority.CurrentEpoch(now);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            repositoryId = "repository-001",
            planId = fixture.PlanId,
            planHash = fixture.PlanHash,
            expectedRevision = fixture.ExpectedRevision,
            operationEpoch = epoch,
            operationNonce = "99999999-8888-7777-6666-555555555555",
            maximumOperationCount = 8,
            maximumRequestBytes = 16 * 1024 * 1024,
            operationEpochNotAfterUtc = LocalDurableOperationAuthority.EpochNotAfter(epoch),
            dispatchLeaseId = "dispatch-1",
            dispatchLeaseExpiresAtUtc = now.AddMinutes(10),
            intakeAttemptId = "attempt-1",
            intakeAttemptExpiresAtUtc = now.AddMinutes(5)
        }));

        var result = await provider.GetRequiredService<ToolRegistry>().InvokeAsync(
            DurablePrepareCommandDefinition.Definition.Name,
            arguments.RootElement);

        result.isError.ShouldBeFalse();
        result.structuredContent.ShouldNotBeNull();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.GetProperty("disposition").GetString().ShouldBe("Prepared");
        payload.RootElement.GetProperty("state").GetString().ShouldBe("Prepared");
        fixture.SnapshotRepository().ShouldBe(before);
    }

    [Fact]
    public async Task a_fresh_mcp_host_reconciles_exact_prepare_as_replay()
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime();
        using var fixture = new DurablePrepareOperationTests.HostFixture(now);
        var epoch = LocalDurableOperationAuthority.CurrentEpoch(now);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            repositoryId = "repository-001",
            planId = fixture.PlanId,
            planHash = fixture.PlanHash,
            expectedRevision = fixture.ExpectedRevision,
            operationEpoch = epoch,
            operationNonce = "99999999-8888-7777-6666-555555555555",
            maximumOperationCount = 8,
            maximumRequestBytes = 16 * 1024 * 1024,
            operationEpochNotAfterUtc = LocalDurableOperationAuthority.EpochNotAfter(epoch),
            dispatchLeaseId = "dispatch-1",
            dispatchLeaseExpiresAtUtc = now.AddMinutes(10),
            intakeAttemptId = "attempt-1",
            intakeAttemptExpiresAtUtc = now.AddMinutes(5)
        }));

        using (var firstHost = McpTestHost.CreateProvider(
                   new McpHostOptions(fixture.RepositoryRoot),
                   fixture.RegisterProductionComposition))
        {
            fixture.SeedPlan(firstHost);
            var prepared = await firstHost.GetRequiredService<ToolRegistry>().InvokeAsync(
                DurablePrepareCommandDefinition.Definition.Name,
                arguments.RootElement);
            prepared.isError.ShouldBeFalse();
        }

        using var restartedHost = McpTestHost.CreateProvider(
            new McpHostOptions(fixture.RepositoryRoot),
            fixture.RegisterProductionComposition);
        var replay = await restartedHost.GetRequiredService<ToolRegistry>().InvokeAsync(
            DurablePrepareCommandDefinition.Definition.Name,
            arguments.RootElement);

        replay.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(replay.content);
        payload.RootElement.GetProperty("disposition").GetString().ShouldBe("Replay");
        payload.RootElement.GetProperty("state").GetString().ShouldBe("Prepared");
    }
}
