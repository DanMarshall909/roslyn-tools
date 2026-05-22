using System.Text.RegularExpressions;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class AddNullChecks : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public AddNullChecks(RtServicesFixture services) => _services = services;

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
    public async Task inserts_null_guards_for_eligible_parameters()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", ServiceSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("OrderService.cs");
        content.ShouldContain("ArgumentNullException.ThrowIfNull(name)", Case.Sensitive);
        content.ShouldNotContain("ThrowIfNull(count)", Case.Sensitive); // int is value type
        content.ShouldNotContain("ThrowIfNull(tag)", Case.Sensitive); // object? is annotated nullable
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Checks are inserted at the TOP of the method body
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task inserts_them_at_the_top_of_the_method_body()
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
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

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
    public async Task skips_already_guarded_params()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", AlreadyGuardedSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("OrderService.cs");

        // Should not have duplicated guards
        var count = Regex
            .Matches(content, @"ThrowIfNull\(name\)")
            .Count;
        count.ShouldBe(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Nullable-annotated params (string?, object?) are skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task skips_nullable_annotated_params()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", NullableAnnotatedSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("OrderService.cs");
        content.ShouldNotContain("ThrowIfNull", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", ServiceSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("OrderService.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", true);
        }

        var after = await proj.ReadSourceAsync("OrderService.cs");
        after.ShouldBe(before);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  6. Expression-bodied methods (no block body) are skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task skips_expression_bodied_methods()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", """
                                          public class OrderService
                                          {
                                              public string Process(string name) => name.ToUpper();
                                          }
                                          """);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("OrderService.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var after = await proj.ReadSourceAsync("OrderService.cs");
        // No ThrowIfNull inserted into an expression-bodied method
        after.ShouldNotContain("ThrowIfNull", Case.Sensitive);
        after.ShouldBe(before);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  7. All null-check patterns count as existing guards
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task recognizes_not_equal_null_pattern_as_existing_guard()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", """
                                          public class OrderService
                                          {
                                              public void Process(string name)
                                              {
                                                  if (name != null) { }
                                              }
                                          }
                                          """);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("OrderService.cs");
        content.ShouldNotContain("ThrowIfNull(name)", Case.Sensitive);
    }

    [Fact]
    public async Task recognizes_null_not_equal_pattern_as_existing_guard()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", """
                                          public class OrderService
                                          {
                                              public void Process(string name)
                                              {
                                                  if (null != name) { }
                                              }
                                          }
                                          """);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("OrderService.cs");
        content.ShouldNotContain("ThrowIfNull(name)", Case.Sensitive);
    }

    [Fact]
    public async Task recognizes_is_null_pattern_as_existing_guard()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", """
                                          public class OrderService
                                          {
                                              public void Process(string name)
                                              {
                                                  if (name is null) throw new System.ArgumentNullException(nameof(name));
                                              }
                                          }
                                          """);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("OrderService.cs");
        content.ShouldNotContain("ThrowIfNull(name)", Case.Sensitive);
    }

    [Fact]
    public async Task recognizes_equal_null_pattern_as_existing_guard()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", """
                                          public class OrderService
                                          {
                                              public void Process(string name)
                                              {
                                                  if (name == null) throw new System.ArgumentNullException(nameof(name));
                                              }
                                          }
                                          """);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "Process", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("OrderService.cs");
        content.ShouldNotContain("ThrowIfNull(name)", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  8. Throws when method is not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_when_method_is_not_found()
    {
        using var proj = new AdhocProjectBuilder("NullApp");
        proj.AddSource("OrderService.cs", ServiceSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _services.Resolve<AddNullChecksCommand>().ExecuteAsync("OrderService", "NonExistent", "ignored", false));
        }
    }
}
