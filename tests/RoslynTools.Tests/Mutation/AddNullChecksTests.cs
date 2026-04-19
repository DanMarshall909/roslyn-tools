using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class AddNullChecksTests
{
    private const string ServiceSource = """
        public class OrderService
        {
            public void Process(string name, int count, object? tag)
            {
            }
        }
        """;

    private const string AlreadyGuardedSource = """
        using System;

        public class OrderService
        {
            public void Process(string name, object payload)
            {
                ArgumentNullException.ThrowIfNull(name);
                ArgumentNullException.ThrowIfNull(payload);
            }
        }
        """;

    private const string NullableAnnotatedSource = """
        public class OrderService
        {
            public void Process(string? name, object? payload)
            {
            }
        }
        """;

    // ──────────────────────────────────────────────────────────────────────────
    //  1. Eligible reference-type params get ThrowIfNull inserted at top
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddNullChecks_InsertsThrowIfNullForEligibleParams()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", ServiceSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddNullChecksCommand.ExecuteAsync("OrderService", "Process", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("OrderService.cs");
        Assert.Contains("ArgumentNullException.ThrowIfNull(name)", content);
        Assert.DoesNotContain("ThrowIfNull(count)", content);   // int is value type
        Assert.DoesNotContain("ThrowIfNull(tag)", content);     // object? is annotated nullable
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Checks are inserted at the TOP of the method body
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddNullChecks_InsertsAtTopOfBody()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", """
            public class OrderService
            {
                public void Process(string name, string value)
                {
                    var x = name.ToUpper();
                }
            }
            """);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddNullChecksCommand.ExecuteAsync("OrderService", "Process", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("OrderService.cs");

        // ThrowIfNull should appear before the existing statement
        var throwIdx = content.IndexOf("ThrowIfNull(name)", StringComparison.Ordinal);
        var existingIdx = content.IndexOf("name.ToUpper()", StringComparison.Ordinal);
        Assert.True(throwIdx < existingIdx, "ThrowIfNull should appear before existing statements");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Already-guarded params are skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddNullChecks_SkipsAlreadyGuardedParams()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", AlreadyGuardedSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddNullChecksCommand.ExecuteAsync("OrderService", "Process", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("OrderService.cs");

        // Should not have duplicated guards
        var count = System.Text.RegularExpressions.Regex
            .Matches(content, @"ThrowIfNull\(name\)")
            .Count;
        Assert.Equal(1, count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Nullable-annotated params (string?, object?) are skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddNullChecks_SkipsNullableAnnotatedParams()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", NullableAnnotatedSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddNullChecksCommand.ExecuteAsync("OrderService", "Process", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("OrderService.cs");
        Assert.DoesNotContain("ThrowIfNull", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddNullChecks_DryRun_NoWrites()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", ServiceSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("OrderService.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await AddNullChecksCommand.ExecuteAsync("OrderService", "Process", "ignored", dryRun: true);

        var after = await proj.ReadSourceAsync("OrderService.cs");
        Assert.Equal(before, after);
    }
}
